using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace Scenario
{
    /// <summary>
    /// Runs the mod in a live world on a dedicated server, with no player. SCENARIO_PHASE selects
    /// "first" (place a post, then test combat, movement, and area reload) or "reload" (the same
    /// world after a server restart). The result is the "SCENARIO" lines in the log.
    /// </summary>
    [BepInPlugin("com.adamprice.towerarchers.scenario", "TowerArchersScenario", "0.1.0")]
    [BepInDependency("com.adamprice.towerarchers")]
    public class ScenarioPlugin : BaseUnityPlugin
    {
        private const string ArcherPrefab = "TowerArcher";
        private const string PostPrefab = "WatchPost";
        private const float TowerHeight = 8f;

        internal static Character Archer;
        internal static Character Target;
        internal static int Shots;
        internal static int AttackStarts;
        internal static int AttackRefusals;
        internal static int AttackTriggers;
        internal static int Hits;
        internal static float LastHitFire;
        internal static Vector3? Focus;

        private bool _areaReady;
        private int _failures;
        private Vector3 _site;

        private void Awake()
        {
            new Harmony("com.adamprice.towerarchers.scenario").PatchAll(typeof(ScenarioPlugin).Assembly);
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            var phase = Environment.GetEnvironmentVariable("SCENARIO_PHASE") ?? "first";
            Report($"START phase={phase}");

            while (!ZNet.instance || !ZNetScene.instance || !ZoneSystem.instance || !ZoneSystem.instance.LocationsGenerated || WorldGenerator.instance == null || !ObjectDB.instance)
            {
                yield return new WaitForSeconds(1f);
            }

            yield return phase == "reload" ? StartCoroutine(ReloadPhase()) : StartCoroutine(FirstPhase());
            Report($"DONE phase={phase} failures={_failures}");
        }

        private IEnumerator FirstPhase()
        {
            CheckPieceRegistration();
            LogBallistics();

            if (!FindSite(out _site))
            {
                Check("site", false, "no flat Meadows site found");
                yield break;
            }
            yield return StartCoroutine(ActivateArea(_site));
            if (!_areaReady)
            {
                yield break;
            }

            ZoneSystem.instance.GetSolidHeight(_site, out float ground);
            var postPosition = new Vector3(_site.x, ground + TowerHeight, _site.z);

            var post = Instantiate(ZNetScene.instance.GetPrefab(PostPrefab), postPosition, Quaternion.identity);
            // The test has no tower below the post, so it must not collapse.
            post.GetComponent<WearNTear>().m_noSupportWear = false;
            foreach (var placed in post.GetComponents<IPlaced>())
            {
                placed.OnPlaced();
            }
            yield return new WaitForSeconds(2f);

            var postZdo = post.GetComponent<ZNetView>().GetZDO();
            var archerId = postZdo.GetConnectionZDOID(ZDOExtraData.ConnectionType.Spawned);
            var archerObject = ZNetScene.instance.FindInstance(archerId);
            Check("post spawns archer", archerObject, $"id={archerId}");
            Check("exactly one archer", CountZdos(ArcherPrefab) == 1, $"count={CountZdos(ArcherPrefab)}");
            if (!archerObject)
            {
                yield break;
            }

            Check("post stores position", postZdo.GetVec3("TowerArchers.ArcherPosition", Vector3.zero) != Vector3.zero, "");
            Prepare(archerObject);
            CheckArcherSetup(archerObject);
            var ai = archerObject.GetComponent<MonsterAI>();
            Check("patrol point set", ai.GetPatrolPoint(out var anchor), $"anchor={anchor}");

            yield return StartCoroutine(CombatRound(archerObject, anchor, 15f));
            yield return StartCoroutine(CombatRound(archerObject, anchor, 30f));
            yield return StartCoroutine(CombatRound(archerObject, anchor, 45f));
            yield return StartCoroutine(FriendlyRound(archerObject, anchor));
            yield return StartCoroutine(ShoveRound(archerObject, anchor));
            yield return StartCoroutine(AreaReloadRound(archerId, anchor));

            var statePath = Environment.GetEnvironmentVariable("SCENARIO_STATE");
            File.WriteAllText(statePath, string.Format(CultureInfo.InvariantCulture, "{0} {1} {2}", _site.x, _site.y, _site.z));
            LogZdoState("before save");
            ZNet.instance.Save(sync: true);
            Report($"INFO saved world, archer id before restart={archerId}");
        }

        private IEnumerator ReloadPhase()
        {
            var parts = File.ReadAllText(Environment.GetEnvironmentVariable("SCENARIO_STATE")).Split(' ');
            _site = new Vector3(
                float.Parse(parts[0], CultureInfo.InvariantCulture),
                float.Parse(parts[1], CultureInfo.InvariantCulture),
                float.Parse(parts[2], CultureInfo.InvariantCulture));
            yield return StartCoroutine(ActivateArea(_site));
            if (!_areaReady)
            {
                yield break;
            }
            LogZdoState("after restart, area active");
            yield return new WaitForSeconds(5f);
            LogZdoState("after restart, 5s later");

            Check("one post after restart", CountZdos(PostPrefab) == 1, $"count={CountZdos(PostPrefab)}");
            Check("one archer after restart", CountZdos(ArcherPrefab) == 1, $"count={CountZdos(ArcherPrefab)}");

            var post = UnityEngine.Object.FindObjectsByType<Piece>(FindObjectsSortMode.None)
                .FirstOrDefault(piece => piece.name.StartsWith(PostPrefab));
            Check("post instance after restart", post, "");
            if (!post)
            {
                yield break;
            }

            var id = post.GetComponent<ZNetView>().GetZDO().GetConnectionZDOID(ZDOExtraData.ConnectionType.Spawned);
            var archerObject = ZNetScene.instance.FindInstance(id);
            Report($"INFO archer id after restart={id}");
            Check("post link survives restart", archerObject, $"id={id}");
            if (!archerObject)
            {
                yield break;
            }

            Prepare(archerObject);
            var character = archerObject.GetComponent<Character>();
            Check("archer still tamed", character.IsTamed(), "");
            var ai = archerObject.GetComponent<MonsterAI>();
            Check("patrol point survives restart", ai.GetPatrolPoint(out var anchor), $"anchor={anchor}");
            yield return StartCoroutine(CombatRound(archerObject, anchor, 15f));

            post.GetComponent<WearNTear>().Remove();
            yield return new WaitForSeconds(5f);
            Check("destroying post despawns archer", CountZdos(ArcherPrefab) == 0, $"archers={CountZdos(ArcherPrefab)}");
            Check("post removed", CountZdos(PostPrefab) == 0, $"posts={CountZdos(PostPrefab)}");
        }

        private IEnumerator CombatRound(GameObject archerObject, Vector3 anchor, float distance)
        {
            var name = $"combat {distance:0}m";
            ClearOtherCharacters(archerObject);
            var archer = archerObject.GetComponent<Character>();
            archer.SetHealth(archer.GetMaxHealth());

            var target = SpawnOnGround("Greydwarf", _site + new Vector3(distance, 0f, 0f));
            Prepare(target);
            var targetCharacter = target.GetComponent<Character>();
            // A still target with high health gives a hit rate that depends only on the arrows.
            targetCharacter.m_speed = targetCharacter.m_walkSpeed = targetCharacter.m_runSpeed = 0f;
            targetCharacter.SetMaxHealth(100000f);
            targetCharacter.SetHealth(100000f);

            Target = targetCharacter;
            Shots = 0;
            Hits = 0;
            LastHitFire = 0f;
            var maxDrift = 0f;
            var firstShot = -1f;
            var burned = false;
            AttackStarts = 0;
            AttackRefusals = 0;
            AttackTriggers = 0;
            var nextSample = 0f;
            var start = Time.time;
            while (Time.time - start < 30f)
            {
                if (Time.time - start >= nextSample)
                {
                    if (nextSample == 5f)
                    {
                        LogLineOfSight(name, archerObject, targetCharacter);
                    }
                    nextSample += 5f;
                    LogAttackState(name, archerObject, targetCharacter);
                }
                maxDrift = Mathf.Max(maxDrift, Vector3.Distance(archerObject.transform.position, anchor));
                if (firstShot < 0f && Shots > 0)
                {
                    firstShot = Time.time - start;
                }
                burned |= targetCharacter.GetSEMan().HaveStatusEffect("Burning".GetStableHashCode());
                yield return null;
            }

            var damage = 100000f - targetCharacter.GetHealth();
            Report($"INFO {name}: shots={Shots} hits={Hits} firstShot={firstShot:0.0}s damage={damage:0} arrowFire={LastHitFire:0} burned={burned} maxDrift={maxDrift:0.00}m archerHealth={archer.GetHealth():0} distanceToTarget={Vector3.Distance(archerObject.transform.position, target.transform.position):0.0}m");
            Check($"{name} fires", Shots > 0, $"shots={Shots}");
            Check($"{name} stays on post", maxDrift < 1f, $"maxDrift={maxDrift:0.00}m");
            if (distance <= 15f)
            {
                Check($"{name} hits with fire damage", Hits > 0 && LastHitFire > 0f, $"hits={Hits} fire={LastHitFire:0}");
                Check($"{name} burns target", burned, "");
            }

            Target = null;
            ZNetScene.instance.Destroy(target);
        }

        // Reports each stage that an attack must pass, to show where an attack stops.
        private void LogAttackState(string round, GameObject archerObject, Character target)
        {
            var humanoid = archerObject.GetComponent<Humanoid>();
            var ai = archerObject.GetComponent<MonsterAI>();
            var weapon = humanoid.GetCurrentWeapon();
            var animator = archerObject.GetComponentInChildren<Animator>();
            var state = animator.GetCurrentAnimatorStateInfo(0);
            var targetName = ai.GetTargetCreature() ? ai.GetTargetCreature().name : "none";
            Report($"DIAG {round}: target={targetName} alerted={ai.IsAlerted()} see={ai.CanSeeTarget(target)} hear={ai.CanHearTarget(target)} " +
                $"weapon={weapon?.m_dropPrefab?.name ?? weapon?.m_shared.m_name ?? "none"} rightItem={humanoid.m_rightItem?.m_shared.m_name ?? "none"} " +
                $"looking={ai.IsLookingAt(target.transform.position, 15f)} inAttack={humanoid.InAttack()} sinceLast={humanoid.GetTimeSinceLastAttack():0.0} " +
                $"starts={AttackStarts} refusals={AttackRefusals} triggers={AttackTriggers} shots={Shots} " +
                $"animState={state.shortNameHash} animTime={state.normalizedTime:0.00} animEnabled={animator.enabled} canMove={humanoid.CanMove()} sleeping={ai.IsSleeping()} eye={humanoid.m_eye.position}");
        }

        // Repeats the tests in BaseAI.CanSeeTarget, to show which test fails.
        private void LogLineOfSight(string round, GameObject archerObject, Character target)
        {
            var character = archerObject.GetComponent<Character>();
            var ai = archerObject.GetComponent<MonsterAI>();
            var eye = character.m_eye.position;
            var targetEye = target.IsCrouching() ? target.GetCenterPoint() : target.m_eye.position;
            var ray = targetEye - eye;
            var hits = Physics.RaycastAll(eye, ray.normalized, ray.magnitude, BaseAI.m_viewBlockMask)
                .OrderBy(hit => hit.distance)
                .Select(hit => $"{hit.collider.transform.root.name}/{hit.collider.name}[{LayerMask.LayerToName(hit.collider.gameObject.layer)}]@{hit.distance:0.0}m");
            Report($"DIAG sight {round}: distance={Vector3.Distance(target.transform.position, archerObject.transform.position):0.0} viewRange={ai.m_viewRange} viewAngle={ai.m_viewAngle} " +
                $"hearRange={ai.m_hearRange} alertRange={ai.m_alertRange} stealth={target.GetStealthFactor():0.00} noise={target.GetNoiseRange():0.0} mist={ParticleMist.IsMistBlocked(eye, targetEye)} " +
                $"eye={eye} targetPos={target.transform.position} targetEye={targetEye} rayLength={ray.magnitude:0.0} hits=[{string.Join(" ", hits)}]");
        }

        private void LogZdoState(string label)
        {
            var sector = ZoneSystem.GetZone(_site);
            var nearby = new List<ZDO>();
            ZDOMan.instance.FindSectorObjects(sector, new SimulationDistance(1, 0), nearby);
            var groups = nearby.GroupBy(zdo => zdo.Persistent).Select(group => $"persistent={group.Key}:{group.Count()}");
            var posts = new List<ZDO>();
            var index = 0;
            while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(PostPrefab, posts, ref index))
            {
            }
            var post = posts.FirstOrDefault();
            Report($"DIAG zdo {label}: zone={sector} nearby={nearby.Count} [{string.Join(" ", groups)}] totalZdos={ZDOMan.instance.m_objectsByID.Count} " +
                $"posts={posts.Count} postPersistent={post?.Persistent} postPosition={post?.GetPosition()} generated={ZoneSystem.instance.IsZoneGenerated(sector)} placedLocations={ZoneSystem.instance.m_generatedZones.Count}");
        }

        private IEnumerator FriendlyRound(GameObject archerObject, Vector3 anchor)
        {
            ClearOtherCharacters(archerObject);
            var ai = archerObject.GetComponent<MonsterAI>();

            var tamed = SpawnOnGround("Boar", _site + new Vector3(12f, 0f, 0f));
            tamed.GetComponent<Character>().SetTamed(true);
            // No player exists on the server. An untamed Players faction creature takes the same
            // IsEnemy branch as a player.
            var ally = SpawnOnGround("Boar", _site + new Vector3(-12f, 0f, 0f));
            ally.GetComponent<Character>().m_faction = Character.Faction.Players;
            yield return new WaitForSeconds(1f);

            Shots = 0;
            var targeted = false;
            var start = Time.time;
            while (Time.time - start < 15f)
            {
                targeted |= ai.GetTargetCreature() != null;
                yield return null;
            }
            Check("ignores tamed animal and Players faction", !targeted && Shots == 0, $"targeted={targeted} shots={Shots}");

            ZNetScene.instance.Destroy(tamed);
            ZNetScene.instance.Destroy(ally);
        }

        private IEnumerator ShoveRound(GameObject archerObject, Vector3 anchor)
        {
            ClearOtherCharacters(archerObject);
            var character = archerObject.GetComponent<Character>();

            Move(character, anchor + new Vector3(0.6f, 0f, 0f));
            yield return new WaitForSeconds(5f);
            var offset = Vector3.Distance(archerObject.transform.position, anchor);
            Check("walks back after a small shove", offset < 0.4f, $"offset={offset:0.00}m");

            Move(character, anchor + new Vector3(12f, -TowerHeight, 0f));
            yield return new WaitForSeconds(3f);
            offset = Vector3.Distance(archerObject.transform.position, anchor);
            Check("returns after a fall from the tower", offset < 1f, $"offset={offset:0.00}m");
        }

        private IEnumerator AreaReloadRound(ZDOID archerId, Vector3 anchor)
        {
            Focus = _site + new Vector3(3000f, 0f, 0f);
            var start = Time.time;
            while (ZNetScene.instance.FindInstance(archerId) && Time.time - start < 60f)
            {
                yield return new WaitForSeconds(1f);
            }
            Check("archer unloads with the area", !ZNetScene.instance.FindInstance(archerId), "");
            Check("archer ZDO survives unload", ZDOMan.instance.GetZDO(archerId) != null, "");
            Check("no despawn on unload", CountZdos(ArcherPrefab) == 1, $"count={CountZdos(ArcherPrefab)}");

            yield return StartCoroutine(ActivateArea(_site));
            if (!_areaReady)
            {
                yield break;
            }
            yield return new WaitForSeconds(5f);

            var archerObject = ZNetScene.instance.FindInstance(archerId);
            Check("archer reloads with the area", archerObject, "");
            Check("one archer after area reload", CountZdos(ArcherPrefab) == 1, $"count={CountZdos(ArcherPrefab)}");
            if (archerObject)
            {
                Prepare(archerObject);
                var ai = archerObject.GetComponent<MonsterAI>();
                var keptAnchor = ai.GetPatrolPoint(out var point) && Vector3.Distance(point, anchor) < 0.01f;
                Check("patrol point survives area reload", keptAnchor, $"anchor={point}");
                Check("archer tamed after area reload", archerObject.GetComponent<Character>().IsTamed(), "");
            }
        }

        private void CheckPieceRegistration()
        {
            var hammer = ObjectDB.instance.GetItemPrefab("Hammer").GetComponent<ItemDrop>().m_itemData.m_shared.m_buildPieces;
            var prefab = hammer.m_pieces.FirstOrDefault(piece => piece.name == PostPrefab);
            Check("Hammer has WatchPost", prefab, "");
            if (prefab)
            {
                var piece = prefab.GetComponent<Piece>();
                var cost = piece.m_resources.FirstOrDefault();
                Check("WatchPost is in Misc", piece.m_category == Piece.PieceCategory.Misc, $"category={piece.m_category}");
                Check("WatchPost costs 10 FineWood", cost?.m_resItem?.name == "FineWood" && cost.m_amount == 10, $"item={cost?.m_resItem?.name} amount={cost?.m_amount}");
            }
        }

        private void CheckArcherSetup(GameObject archerObject)
        {
            var character = archerObject.GetComponent<Humanoid>();
            Check("archer name", character.m_name == "Tower Archer", character.m_name);
            Check("archer faction", character.GetFaction() == Character.Faction.Players, character.GetFaction().ToString());
            Check("archer tamed", character.IsTamed(), "");
            Check("archer health", Mathf.Approximately(character.GetMaxHealth(), 150f), $"max={character.GetMaxHealth()}");
            Check("archer has no drops", !archerObject.GetComponent<CharacterDrop>(), "");

            var items = character.GetInventory().GetAllItems();
            var names = string.Join(",", items.Select(item => item.m_dropPrefab ? item.m_dropPrefab.name : item.m_shared.m_name));
            Check("bow is the only item", items.Count == 1 && names == "ArcherBowFire", names);
        }

        private void LogBallistics()
        {
            var bow = ObjectDB.instance.GetItemPrefab("ArcherBowFire").GetComponent<ItemDrop>().m_itemData.m_shared;
            var attack = bow.m_attack;
            var projectile = attack.m_attackProjectile.GetComponent<Projectile>();
            Report($"INFO ballistics: velocity={attack.m_projectileVel} velocityMin={attack.m_projectileVelMin} randomVelocity={attack.m_randomVelocity} accuracy={attack.m_projectileAccuracy} accuracyMin={attack.m_projectileAccuracyMin} launchAngle={attack.m_launchAngle} gravity={projectile.m_gravity} drag={projectile.m_drag} ttl={projectile.m_ttl} bowDraw={attack.m_bowDraw} aiMaxAngle={bow.m_aiAttackMaxAngle} attackHeight={attack.m_attackHeight}");

            var prefab = ZNetScene.instance.GetPrefab(ArcherPrefab);
            var animator = prefab.GetComponentInChildren<Animator>();
            Report($"INFO archer animator: culling={animator.cullingMode} speed={prefab.GetComponent<Character>().m_speed}");
        }

        // No camera exists on a server. A culled animator fires no animation events, and an attack
        // needs an event to release the arrow. This limit belongs to the test, not to the mod.
        private static void Prepare(GameObject creature)
        {
            foreach (var animator in creature.GetComponentsInChildren<Animator>())
            {
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
            if (creature.GetComponent<Humanoid>() is Humanoid humanoid && creature.name.StartsWith(ArcherPrefab))
            {
                Archer = humanoid;
            }
        }

        private static GameObject SpawnOnGround(string prefab, Vector3 position)
        {
            ZoneSystem.instance.GetSolidHeight(position, out float ground);
            position.y = ground + 0.5f;
            return UnityEngine.Object.Instantiate(ZNetScene.instance.GetPrefab(prefab), position, Quaternion.identity);
        }

        private static void Move(Character character, Vector3 position)
        {
            character.m_body.position = position;
            character.m_body.linearVelocity = Vector3.zero;
            character.transform.position = position;
        }

        private static void ClearOtherCharacters(GameObject keep)
        {
            foreach (var character in Character.GetAllCharacters().ToList())
            {
                if (character.gameObject != keep)
                {
                    ZNetScene.instance.Destroy(character.gameObject);
                }
            }
        }

        private static int CountZdos(string prefab)
        {
            var zdos = new List<ZDO>();
            var index = 0;
            while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(prefab, zdos, ref index))
            {
            }
            return zdos.Count;
        }

        private IEnumerator ActivateArea(Vector3 position)
        {
            Focus = position;
            var start = Time.time;
            _areaReady = false;
            while (!_areaReady && Time.time - start < 120f)
            {
                _areaReady = ZoneSystem.instance.IsZoneLoaded(position) && ZNetScene.instance.IsAreaReady(position);
                yield return new WaitForSeconds(1f);
            }
            Check("area becomes active", _areaReady, $"position={position} after {Time.time - start:0}s");
            yield return new WaitForSeconds(3f);
        }

        private static bool FindSite(out Vector3 site)
        {
            for (var radius = 200f; radius < 4000f; radius += 100f)
            {
                for (var angle = 0f; angle < 360f; angle += 15f)
                {
                    var x = radius * Mathf.Cos(angle * Mathf.Deg2Rad);
                    var z = radius * Mathf.Sin(angle * Mathf.Deg2Rad);
                    if (WorldGenerator.instance.GetBiome(x, z) != Heightmap.Biome.Meadows)
                    {
                        continue;
                    }

                    var height = WorldGenerator.instance.GetHeight(x, z);
                    var flat = new[] { 15f, 30f, 45f }.All(offset => Mathf.Abs(WorldGenerator.instance.GetHeight(x + offset, z) - height) < 3f);
                    if (height > 33f && height < 60f && flat)
                    {
                        site = new Vector3(x, height, z);
                        return true;
                    }
                }
            }
            site = Vector3.zero;
            return false;
        }

        private void Check(string name, bool passed, string detail)
        {
            if (!passed)
            {
                _failures++;
            }
            Report($"{(passed ? "PASS" : "FAIL")} {name}{(detail.Length > 0 ? ": " + detail : "")}");
        }

        private void Report(string message)
        {
            Logger.LogInfo("SCENARIO " + message);
        }
    }

    // Game.FixedUpdate sets the reference position to a far point on each physics tick. A player
    // normally overrides this, but the server has no player. The reference position selects the
    // active area, so the scenario replaces each write with its own position.
    [HarmonyPatch(typeof(ZNet), nameof(ZNet.SetReferencePosition))]
    internal static class SetReferencePositionPatch
    {
        private static void Prefix(ref Vector3 pos)
        {
            if (ScenarioPlugin.Focus.HasValue)
            {
                pos = ScenarioPlugin.Focus.Value;
            }
        }
    }

    // Humanoid.StartAttack sets the animation trigger, or refuses the attack.
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
    internal static class StartAttackPatch
    {
        private static void Postfix(Humanoid __instance, bool __result)
        {
            if (__instance == ScenarioPlugin.Archer)
            {
                if (__result)
                {
                    ScenarioPlugin.AttackStarts++;
                }
                else
                {
                    ScenarioPlugin.AttackRefusals++;
                }
            }
        }
    }

    // An animation event calls Humanoid.OnAttackTrigger to release the attack.
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.OnAttackTrigger))]
    internal static class OnAttackTriggerPatch
    {
        private static void Prefix(Humanoid __instance)
        {
            if (__instance == ScenarioPlugin.Archer)
            {
                ScenarioPlugin.AttackTriggers++;
            }
        }
    }

    // Attack.FireProjectileBurst releases the arrows. Counts the shots that the archer fires.
    [HarmonyPatch(typeof(Attack), nameof(Attack.FireProjectileBurst))]
    internal static class FireProjectileBurstPatch
    {
        private static void Prefix(Attack __instance)
        {
            if (__instance.m_character == ScenarioPlugin.Archer)
            {
                ScenarioPlugin.Shots++;
            }
        }
    }

    // Character.Damage receives each hit. Pierce damage separates an arrow from a burn tick.
    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    internal static class CharacterDamagePatch
    {
        private static void Prefix(Character __instance, HitData hit)
        {
            if (__instance == ScenarioPlugin.Target && hit.GetAttacker() == ScenarioPlugin.Archer && hit.m_damage.m_pierce > 0f)
            {
                ScenarioPlugin.Hits++;
                ScenarioPlugin.LastHitFire = hit.m_damage.m_fire;
            }
        }
    }
}
