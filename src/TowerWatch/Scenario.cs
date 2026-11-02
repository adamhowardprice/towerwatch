using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace TowerWatch
{
    /// <summary>
    /// CI test mode for the proof of concept. It runs only when TOWERWATCH_PHASE is set, and it
    /// plays the part of a player: it places a floor and a sign, and it spawns targets.
    /// </summary>
    public class Scenario : MonoBehaviour
    {
        internal static Character Archer;
        internal static Character Target;
        internal static int Shots;
        internal static int Starts;
        internal static int Refusals;
        internal static int Triggers;
        internal static int Hits;
        internal static float LastHitFire;

        private int _failures;
        private Vector3 _site;

        private IEnumerator Start()
        {
            var phase = Environment.GetEnvironmentVariable("TOWERWATCH_PHASE");
            Report($"START phase={phase} backend={ZNet.m_onlineBackend}");
            while (!ServerOnlyPlugin.WorldReady())
            {
                yield return new WaitForSeconds(1f);
            }
            Report($"INFO world ready, backend={ZNet.m_onlineBackend} dedicated={ZNet.instance.IsDedicated()}");

            // Test only: the post has no tower below it.
            foreach (var prefab in new[] { "wood_floor", ServerOnlyPlugin.SignPrefab })
            {
                ZNetScene.instance.GetPrefab(prefab).GetComponent<WearNTear>().m_noSupportWear = false;
            }

            if (phase == "first")
            {
                yield return StartCoroutine(FirstPhase());
            }
            else if (phase == "reload")
            {
                yield return StartCoroutine(ReloadPhase());
            }
            else
            {
                yield return new WaitForSeconds(60f);
            }
            Report($"DONE phase={phase} failures={_failures}");
        }

        private IEnumerator FirstPhase()
        {
            FindSite();
            ServerOnlyPlugin.Focus = _site;
            yield return StartCoroutine(WaitForArea());
            if (!ZNetScene.instance.IsAreaReady(_site))
            {
                yield break;
            }

            ZoneSystem.instance.GetSolidHeight(_site, out float ground);
            var floorPosition = new Vector3(_site.x, ground + 8f, _site.z);
            Instantiate(ZNetScene.instance.GetPrefab("wood_floor"), floorPosition, Quaternion.identity);
            var sign = Instantiate(ZNetScene.instance.GetPrefab(ServerOnlyPlugin.SignPrefab), floorPosition + new Vector3(0f, 0.6f, 0f), Quaternion.identity);
            sign.GetComponent<ZNetView>().GetZDO().Set(ZDOVars.s_text, ServerOnlyPlugin.SignText);

            var archerObject = default(GameObject);
            var waited = 0f;
            while (!archerObject && waited < 30f)
            {
                yield return new WaitForSeconds(1f);
                waited += 1f;
                var archerZdo = ServerOnlyPlugin.FindZdos(ServerOnlyPlugin.ArcherPrefab).FirstOrDefault(ServerOnlyPlugin.IsArcher);
                archerObject = archerZdo != null ? ZNetScene.instance.FindInstance(archerZdo)?.gameObject : null;
            }
            Check("sign spawns archer", archerObject, $"after {waited:0}s");
            if (!archerObject)
            {
                yield break;
            }

            yield return new WaitForSeconds(3f);
            var character = archerObject.GetComponent<Humanoid>();
            Archer = character;
            var ai = archerObject.GetComponent<MonsterAI>();
            var items = character.GetInventory().GetAllItems();
            Check("archer is tamed", character.IsTamed(), $"faction={character.GetFaction()}");
            Check("archer has health", Mathf.Approximately(character.GetMaxHealth(), ServerOnlyPlugin.ArcherHealth), $"max={character.GetMaxHealth()}");
            Check("archer has a bow", items.Any(item => item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Bow), string.Join(",", items.Select(item => item.m_shared.m_name)));
            Check("patrol point set", ai.GetPatrolPoint(out var anchor), $"anchor={anchor}");

            yield return StartCoroutine(OwnershipCheck("before combat", archerObject));
            yield return StartCoroutine(CombatRound(archerObject, anchor, 15f, true));
            yield return StartCoroutine(CombatRound(archerObject, anchor, 30f, false));
            yield return StartCoroutine(CombatRound(archerObject, anchor, 45f, false));
            yield return StartCoroutine(FriendlyRound(archerObject));

            File.WriteAllText(Environment.GetEnvironmentVariable("TOWERWATCH_STATE"), string.Format(CultureInfo.InvariantCulture, "{0} {1} {2}", _site.x, _site.y, _site.z));
            ZNet.instance.Save(sync: true);
            Report($"INFO saved world, archer id before restart={archerObject.GetComponent<ZNetView>().GetZDO().m_uid}");
        }

        private IEnumerator ReloadPhase()
        {
            var parts = File.ReadAllText(Environment.GetEnvironmentVariable("TOWERWATCH_STATE")).Split(' ');
            _site = new Vector3(float.Parse(parts[0], CultureInfo.InvariantCulture), float.Parse(parts[1], CultureInfo.InvariantCulture), float.Parse(parts[2], CultureInfo.InvariantCulture));

            // The plugin must find the sign and move the active area without help from this test.
            yield return new WaitForSeconds(15f);
            Check("plugin focuses the saved sign", ServerOnlyPlugin.Focus.HasValue, $"focus={ServerOnlyPlugin.Focus}");
            yield return StartCoroutine(WaitForArea());
            yield return new WaitForSeconds(10f);

            var archers = ServerOnlyPlugin.FindZdos(ServerOnlyPlugin.ArcherPrefab).Where(ServerOnlyPlugin.IsArcher).ToList();
            Check("one archer after restart", archers.Count == 1, $"count={archers.Count}");
            if (archers.Count == 0)
            {
                yield break;
            }

            var archerObject = ZNetScene.instance.FindInstance(archers[0])?.gameObject;
            Check("archer instance after restart", archerObject, $"id={archers[0].m_uid}");
            if (!archerObject)
            {
                yield break;
            }

            Archer = archerObject.GetComponent<Character>();
            var ai = archerObject.GetComponent<MonsterAI>();
            Check("guard back after restart", archerObject.GetComponent<ArcherGuard>(), "");
            Check("still tamed after restart", Archer.IsTamed(), "");
            Check("patrol point after restart", ai.GetPatrolPoint(out var anchor), $"anchor={anchor}");
            yield return StartCoroutine(OwnershipCheck("after restart", archerObject));
            yield return StartCoroutine(CombatRound(archerObject, anchor, 15f, true));

            var sign = ServerOnlyPlugin.FindZdos(ServerOnlyPlugin.SignPrefab).First();
            var signInstance = ZNetScene.instance.FindInstance(sign);
            signInstance.ClaimOwnership();
            ZNetScene.instance.Destroy(signInstance.gameObject);
            yield return new WaitForSeconds(12f);
            var left = ServerOnlyPlugin.FindZdos(ServerOnlyPlugin.ArcherPrefab).Count(ServerOnlyPlugin.IsArcher);
            Check("removing the sign despawns the archer", left == 0, $"archers={left}");
        }

        // A player's game must run all objects except the archers, so count what the server owns.
        private IEnumerator OwnershipCheck(string label, GameObject archerObject)
        {
            yield return new WaitForSeconds(6f);
            var session = ZDOMan.GetSessionID();
            var nearby = new List<ZDO>();
            ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(ZNet.instance.GetReferencePosition()), new SimulationDistance(1, 0), nearby);
            var owned = nearby.Where(zdo => zdo.Persistent && zdo.GetOwner() == session).ToList();
            var others = owned.Where(zdo => !ServerOnlyPlugin.IsArcher(zdo)).ToList();
            var names = others.Take(8).Select(zdo => ZNetScene.instance.GetPrefab(zdo.GetPrefab())?.name);
            Check($"server owns only archers ({label})", others.Count == 0 && owned.Count == 1, $"nearby={nearby.Count} owned={owned.Count} nonArchers={others.Count} [{string.Join(",", names)}]");
        }

        private IEnumerator CombatRound(GameObject archerObject, Vector3 anchor, float distance, bool mustHit)
        {
            var name = $"combat {distance:0}m";
            var archer = archerObject.GetComponent<Character>();
            archer.SetHealth(archer.GetMaxHealth());

            var target = SpawnOnGround("Greydwarf", _site + new Vector3(distance, 0f, 0f));
            var targetCharacter = target.GetComponent<Character>();
            // A still target with high health gives a hit rate that depends only on the arrows.
            targetCharacter.m_speed = targetCharacter.m_walkSpeed = targetCharacter.m_runSpeed = 0f;
            targetCharacter.SetMaxHealth(100000f);
            targetCharacter.SetHealth(100000f);
            foreach (var animator in target.GetComponentsInChildren<Animator>())
            {
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
            yield return StartCoroutine(ClearView(archer, targetCharacter));

            Target = targetCharacter;
            Shots = Starts = Refusals = Triggers = Hits = 0;
            LastHitFire = 0f;
            var maxDrift = 0f;
            var burned = false;
            var nextSample = 0f;
            var start = Time.time;
            while (Time.time - start < 30f)
            {
                if (Time.time - start >= nextSample)
                {
                    nextSample += 10f;
                    var ai = archerObject.GetComponent<MonsterAI>();
                    var targetName = ai.GetTargetCreature() ? ai.GetTargetCreature().name : "none";
                    var weapon = archerObject.GetComponent<Humanoid>().GetCurrentWeapon();
                    var toTarget = targetCharacter.transform.position - archerObject.transform.position;
                    toTarget.y = 0f;
                    var forward = archerObject.transform.forward;
                    forward.y = 0f;
                    Report($"DIAG {name}: target={targetName} alerted={ai.IsAlerted()} see={ai.CanSeeTarget(targetCharacter)} weapon={weapon?.m_shared.m_name ?? "none"} range={weapon?.m_shared.m_aiAttackRange} interval={weapon?.m_shared.m_aiAttackInterval} maxAngle={weapon?.m_shared.m_aiAttackMaxAngle} " +
                        $"facingError={Vector3.Angle(forward, toTarget):0}deg anchorOffset={Utils.DistanceXZ(archerObject.transform.position, anchor):0.00}m sinceAttack={archer.GetTimeSinceLastAttack():0.0} inAttack={archer.InAttack()} canUse={(weapon != null && ai.CanUseAttack(weapon))} " +
                        $"starts={Starts} refusals={Refusals} triggers={Triggers} shots={Shots} hits={Hits}");
                }
                maxDrift = Mathf.Max(maxDrift, Vector3.Distance(archerObject.transform.position, anchor));
                burned |= targetCharacter.GetSEMan().HaveStatusEffect("Burning".GetStableHashCode());
                yield return null;
            }

            Report($"INFO {name}: starts={Starts} refusals={Refusals} triggers={Triggers} shots={Shots} hits={Hits} damage={100000f - targetCharacter.GetHealth():0} arrowFire={LastHitFire:0} burned={burned} maxDrift={maxDrift:0.00}m distance={Vector3.Distance(archerObject.transform.position, target.transform.position):0.0}m");
            Check($"{name} fires", Shots > 0, $"starts={Starts} triggers={Triggers} shots={Shots}");
            Check($"{name} stays on post", maxDrift < 1f, $"maxDrift={maxDrift:0.00}m");
            if (mustHit)
            {
                Check($"{name} hits with fire", Hits > 0 && LastHitFire > 0f, $"hits={Hits} fire={LastHitFire:0}");
            }

            Target = null;
            ServerOnlyPlugin.ExtraOwned.Remove(target.GetComponent<ZNetView>().GetZDO().m_uid);
            ZNetScene.instance.Destroy(target);
        }

        private IEnumerator FriendlyRound(GameObject archerObject)
        {
            var archer = archerObject.GetComponent<Character>();
            var ai = archerObject.GetComponent<MonsterAI>();
            var tamed = SpawnOnGround("Boar", _site + new Vector3(12f, 0f, 0f));
            tamed.GetComponent<Character>().SetTamed(true);
            // No player exists on the server. An untamed Players faction creature takes the same
            // IsEnemy branch as a player.
            var ally = SpawnOnGround("Boar", _site + new Vector3(20f, 0f, 3f));
            ally.GetComponent<Character>().m_faction = Character.Faction.Players;
            yield return new WaitForSeconds(1f);
            yield return StartCoroutine(ClearView(archer, tamed.GetComponent<Character>()));
            yield return StartCoroutine(ClearView(archer, ally.GetComponent<Character>()));
            Check("friendly creatures are visible", ai.CanSeeTarget(tamed.GetComponent<Character>()) && ai.CanSeeTarget(ally.GetComponent<Character>()), "");

            Shots = 0;
            var targeted = false;
            var start = Time.time;
            while (Time.time - start < 15f)
            {
                targeted |= ai.GetTargetCreature() != null;
                yield return null;
            }
            Check("ignores tamed animal and Players faction", !targeted && Shots == 0, $"targeted={targeted} shots={Shots}");

            foreach (var creature in new[] { tamed, ally })
            {
                ServerOnlyPlugin.ExtraOwned.Remove(creature.GetComponent<ZNetView>().GetZDO().m_uid);
                ZNetScene.instance.Destroy(creature);
            }
        }

        // A real base has no forest around its tower. Removes what blocks the view of a target.
        private IEnumerator ClearView(Character viewer, Character target)
        {
            var removed = 0;
            var remaining = "";
            for (var pass = 0; pass < 3; pass++)
            {
                var eye = viewer.m_eye.position;
                var ray = target.m_eye.position - eye;
                remaining = "";
                foreach (var hit in Physics.RaycastAll(eye, ray.normalized, ray.magnitude, BaseAI.m_viewBlockMask))
                {
                    var view = hit.collider.GetComponentInParent<ZNetView>();
                    if (view && view.GetZDO() != null && !view.GetComponent<Piece>() && !view.GetComponent<Character>())
                    {
                        view.ClaimOwnership();
                        ZNetScene.instance.Destroy(view.gameObject);
                        removed++;
                    }
                    else
                    {
                        remaining += $" {hit.collider.transform.root.name}/{hit.collider.name}@{hit.distance:0.0}m";
                    }
                }
                yield return new WaitForFixedUpdate();
                Physics.SyncTransforms();
            }
            Report($"INFO cleared view of {target.name}: removed={removed} remaining=[{remaining}]");
        }

        private static GameObject SpawnOnGround(string prefab, Vector3 position)
        {
            ZoneSystem.instance.GetSolidHeight(position, out float ground);
            position.y = ground + 0.5f;
            var creature = Instantiate(ZNetScene.instance.GetPrefab(prefab), position, Quaternion.identity);
            // No player exists to own the target, so the server must own it to take damage.
            ServerOnlyPlugin.ExtraOwned.Add(creature.GetComponent<ZNetView>().GetZDO().m_uid);
            return creature;
        }

        private IEnumerator WaitForArea()
        {
            var start = Time.time;
            var position = ServerOnlyPlugin.Focus ?? _site;
            while (!ZNetScene.instance.IsAreaReady(position) && Time.time - start < 120f)
            {
                yield return new WaitForSeconds(1f);
            }
            Check("area becomes active", ZNetScene.instance.IsAreaReady(position), $"position={position} after {Time.time - start:0}s");
            yield return new WaitForSeconds(3f);
        }

        private void FindSite()
        {
            for (var radius = 200f; radius < 4000f; radius += 100f)
            {
                for (var angle = 0f; angle < 360f; angle += 15f)
                {
                    var x = radius * Mathf.Cos(angle * Mathf.Deg2Rad);
                    var z = radius * Mathf.Sin(angle * Mathf.Deg2Rad);
                    var height = WorldGenerator.instance.GetHeight(x, z);
                    var flat = new[] { 15f, 30f, 45f }.All(offset => Mathf.Abs(WorldGenerator.instance.GetHeight(x + offset, z) - height) < 3f);
                    if (WorldGenerator.instance.GetBiome(x, z) == Heightmap.Biome.Meadows && height > 33f && height < 60f && flat)
                    {
                        _site = new Vector3(x, height, z);
                        return;
                    }
                }
            }
            Check("site", false, "no flat Meadows site found");
        }

        private void Check(string name, bool passed, string detail)
        {
            if (!passed)
            {
                _failures++;
            }
            Report($"{(passed ? "PASS" : "FAIL")} {name}{(detail.Length > 0 ? ": " + detail : "")}");
        }

        private static void Report(string message)
        {
            Debug.Log("SCENARIO " + message);
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
    internal static class StartAttackPatch
    {
        private static void Postfix(Humanoid __instance, bool __result)
        {
            if (__instance == Scenario.Archer)
            {
                if (__result)
                {
                    Scenario.Starts++;
                }
                else
                {
                    Scenario.Refusals++;
                }
            }
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.OnAttackTrigger))]
    internal static class OnAttackTriggerPatch
    {
        private static void Prefix(Humanoid __instance)
        {
            if (__instance == Scenario.Archer)
            {
                Scenario.Triggers++;
            }
        }
    }

    [HarmonyPatch(typeof(Attack), nameof(Attack.FireProjectileBurst))]
    internal static class FireProjectileBurstPatch
    {
        private static void Prefix(Attack __instance)
        {
            if (__instance.m_character == Scenario.Archer)
            {
                Scenario.Shots++;
            }
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    internal static class CharacterDamagePatch
    {
        private static void Prefix(Character __instance, HitData hit)
        {
            if (__instance == Scenario.Target && hit.GetAttacker() == Scenario.Archer && hit.m_damage.m_pierce > 0f)
            {
                Scenario.Hits++;
                Scenario.LastHitFire = hit.m_damage.m_fire;
            }
        }
    }
}
