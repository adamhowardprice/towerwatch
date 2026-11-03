using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace TowerWatch
{
    /// <summary>
    /// Only the dedicated server has this DLL. A vanilla sign with the
    /// text "[archer]" gets one tamed vanilla Draugr_Ranged. The server keeps the active area on
    /// the tower, owns only the archers, and leaves all other objects to the players.
    /// </summary>
    [BepInPlugin("com.adamprice.towerwatch", "TowerWatch", "0.0.1")]
    public class ServerOnlyPlugin : BaseUnityPlugin
    {
        public const string ArcherPrefab = "Draugr_Ranged";
        public const string SignPrefab = "sign";
        public const string SignText = "[archer]";
        public const string ArcherMarker = "towerwatch_archer";
        public const string SpawnedMarker = "towerwatch_spawned";

        public const float ViewRange = 50f;
        public const float HearRange = 50f;
        public const float AttackRange = 50f;
        public const float AttackInterval = 2.5f;
        public const float ArcherHealth = 150f;
        public const float LeashDistance = 6f;
        public const float ArrowVelocity = 60f;
        public const float ArrowSpread = 1f;

        public static float ArrowGravity;
        public static Vector3? Focus;
        public static readonly HashSet<ZDOID> ExtraOwned = new HashSet<ZDOID>();

        private void Awake()
        {
            new Harmony("com.adamprice.towerwatch").PatchAll(typeof(ServerOnlyPlugin).Assembly);
            StartCoroutine(Manage());
            if (Environment.GetEnvironmentVariable("TOWERWATCH_PHASE") != null)
            {
                gameObject.AddComponent<Scenario>();
            }
        }

        public static bool WorldReady()
        {
            return ZNet.instance && ZNetScene.instance && ZoneSystem.instance && ZoneSystem.instance.LocationsGenerated
                && WorldGenerator.instance != null && ObjectDB.instance;
        }

        private IEnumerator Manage()
        {
            while (!WorldReady())
            {
                yield return new WaitForSeconds(1f);
            }
            if (!ZNet.instance.IsDedicated())
            {
                Logger.LogWarning("TowerWatch does nothing unless it runs on a dedicated server.");
                yield break;
            }

            ConfigureBow();
            while (true)
            {
                ScanSigns();
                yield return new WaitForSeconds(5f);
            }
        }

        // The server fires each arrow, so these values need to change only on the server.
        private void ConfigureBow()
        {
            var draugr = ZNetScene.instance.GetPrefab(ArcherPrefab).GetComponent<Humanoid>();
            var bow = draugr.m_defaultItems.Concat(draugr.m_randomWeapon).Concat(draugr.m_randomSets.SelectMany(set => set.m_items))
                .Where(prefab => prefab)
                .Select(prefab => prefab.GetComponent<ItemDrop>()?.m_itemData.m_shared)
                .First(shared => shared?.m_itemType == ItemDrop.ItemData.ItemType.Bow);
            var arrow = ZNetScene.instance.GetPrefab("ArrowFire").GetComponent<ItemDrop>().m_itemData.m_shared;

            bow.m_attack.m_attackProjectile = arrow.m_attack.m_attackProjectile;
            bow.m_damages.Add(arrow.m_damages);
            bow.m_attackStatusEffect = arrow.m_attackStatusEffect;
            bow.m_attackStatusEffectChance = arrow.m_attackStatusEffectChance;
            bow.m_aiAttackRange = AttackRange;
            bow.m_aiAttackRangeMin = 0f;
            bow.m_aiAttackInterval = AttackInterval;

            var attack = bow.m_attack;
            var projectile = attack.m_attackProjectile.GetComponent<Projectile>();
            Logger.LogInfo($"Bow before tuning: velocity={attack.m_projectileVel} velocityMin={attack.m_projectileVelMin} randomVelocity={attack.m_randomVelocity} accuracy={attack.m_projectileAccuracy} accuracyMin={attack.m_projectileAccuracyMin} skillAccuracy={attack.m_skillAccuracy} launchAngle={attack.m_launchAngle} gravity={projectile.m_gravity} drag={projectile.m_drag} ttl={projectile.m_ttl}");

            // With m_skillAccuracy, an attacker with no skill uses m_projectileAccuracyMin.
            attack.m_skillAccuracy = false;
            attack.m_projectileAccuracy = ArrowSpread;
            attack.m_projectileAccuracyMin = ArrowSpread;
            attack.m_randomVelocity = false;
            attack.m_projectileVel = ArrowVelocity;
            attack.m_projectileVelMin = ArrowVelocity;
            ArrowGravity = projectile.m_gravity;
            Logger.LogInfo($"Bow: projectile={projectile.name} animation={attack.m_attackAnimation} velocity={attack.m_projectileVel} spread={attack.m_projectileAccuracy} bowDraw={attack.m_bowDraw} fire={bow.m_damages.m_fire} pierce={bow.m_damages.m_pierce}");
        }

        public static List<ZDO> FindZdos(string prefab)
        {
            var zdos = new List<ZDO>();
            var index = 0;
            while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(prefab, zdos, ref index))
            {
            }
            return zdos;
        }

        public static bool IsArcher(ZDO zdo)
        {
            return zdo.GetBool(ArcherMarker);
        }

        private void ScanSigns()
        {
            var signs = FindZdos(SignPrefab)
                .Where(zdo => zdo.GetString(ZDOVars.s_text, "").IndexOf(SignText, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            // The server has one active area, so the first sign selects it.
            if (signs.Count > 0)
            {
                Focus = signs[0].GetPosition();
            }

            var linked = new HashSet<ZDOID>();
            foreach (var sign in signs)
            {
                var id = sign.GetConnectionZDOID(ZDOExtraData.ConnectionType.Spawned);
                if (!id.IsNone())
                {
                    linked.Add(id);
                }
                if (!sign.GetBool(SpawnedMarker) && ZNetScene.instance.IsAreaReady(sign.GetPosition()))
                {
                    linked.Add(SpawnArcher(sign));
                }
            }

            // A sign owns its archer. When the sign goes, the archer goes.
            foreach (var archer in FindZdos(ArcherPrefab).Where(IsArcher).Where(zdo => !linked.Contains(zdo.m_uid)))
            {
                Despawn(archer);
            }
        }

        private ZDOID SpawnArcher(ZDO sign)
        {
            // A player can own the sign, and only the owner can write to its ZDO.
            sign.SetOwner(ZDOMan.GetSessionID());

            // The archer stands in front of the sign, because the sign has a collider.
            var position = FindStandingPoint(sign.GetPosition() + sign.GetRotation() * Vector3.forward * 0.8f);
            var archer = Instantiate(ZNetScene.instance.GetPrefab(ArcherPrefab), position, Quaternion.identity);
            var zdo = archer.GetComponent<ZNetView>().GetZDO();
            zdo.Set(ArcherMarker, true);
            archer.AddComponent<ArcherGuard>();

            sign.SetConnection(ZDOExtraData.ConnectionType.Spawned, zdo.m_uid);
            sign.Set(SpawnedMarker, true);
            Logger.LogInfo($"Spawned archer {zdo.m_uid} at {position} for the sign at {sign.GetPosition()}");
            return zdo.m_uid;
        }

        // Finds the highest floor below the sign, and ignores the sign.
        private static Vector3 FindStandingPoint(Vector3 signPosition)
        {
            var mask = LayerMask.GetMask("piece", "static_solid", "Default", "terrain");
            var hits = Physics.RaycastAll(signPosition + Vector3.up, Vector3.down, 12f, mask)
                .Where(hit => !hit.collider.GetComponentInParent<Sign>())
                .OrderBy(hit => hit.distance);
            foreach (var hit in hits)
            {
                return hit.point + Vector3.up * 0.1f;
            }
            return signPosition;
        }

        private static void Despawn(ZDO archer)
        {
            var instance = ZNetScene.instance.FindInstance(archer);
            if (instance)
            {
                instance.ClaimOwnership();
                ZNetScene.instance.Destroy(instance.gameObject);
            }
            else
            {
                archer.SetOwner(ZDOMan.GetSessionID());
                ZDOMan.instance.DestroyZDO(archer);
            }
        }
    }

    /// <summary>Configures one archer on the server, and holds it at its patrol point.</summary>
    public class ArcherGuard : MonoBehaviour
    {
        private Humanoid _humanoid;
        private MonsterAI _ai;
        private ZNetView _nview;

        private void Awake()
        {
            _humanoid = GetComponent<Humanoid>();
            _ai = GetComponent<MonsterAI>();
            _nview = GetComponent<ZNetView>();

            var drops = GetComponent<CharacterDrop>();
            if (drops)
            {
                Destroy(drops);
            }

            _ai.m_randomMoveRange = 0f;
            _ai.m_circulateWhileCharging = false;
            _ai.m_circleTargetInterval = 0f;
            _ai.m_minAttackInterval = 0f;
            _ai.m_viewAngle = 360f;
            _ai.m_viewRange = ServerOnlyPlugin.ViewRange;
            _ai.m_hearRange = ServerOnlyPlugin.HearRange;
            _ai.m_alertRange = ServerOnlyPlugin.ViewRange;
            _ai.m_fleeIfHurtWhenTargetCantBeReached = false;
            _ai.m_fleeIfNotAlerted = false;
            _ai.m_fleeIfLowHealth = 0f;
            _ai.m_afraidOfFire = false;
            _ai.m_avoidFire = false;
            _ai.m_enableHuntPlayer = false;
            _ai.m_attackPlayerObjects = false;
            _ai.m_sleeping = false;

            // A server has no camera. A culled animator fires no event, so no arrow would release.
            foreach (var animator in GetComponentsInChildren<Animator>())
            {
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
        }

        private void Start()
        {
            if (!_nview.IsValid() || !_nview.IsOwner())
            {
                return;
            }

            _humanoid.SetTamed(true);
            _humanoid.SetMaxHealth(ServerOnlyPlugin.ArcherHealth);
            if (!_ai.GetPatrolPoint(out _))
            {
                _ai.SetPatrolPoint();
            }
        }

        public void Hold()
        {
            if (!_ai.GetPatrolPoint(out var anchor))
            {
                return;
            }

            var offset = anchor - transform.position;
            if (offset.magnitude > ServerOnlyPlugin.LeashDistance)
            {
                _humanoid.m_body.position = anchor;
                _humanoid.m_body.linearVelocity = Vector3.zero;
                transform.position = anchor;
                _ai.StopMoving();
                return;
            }

            offset.y = 0f;
            if (offset.magnitude < 0.25f)
            {
                _ai.StopMoving();
            }
            else
            {
                // BaseAI.MoveTowards also turns the character toward the anchor, and an archer
                // that faces away from its target never attacks. SetMoveDir keeps the aim.
                _humanoid.SetMoveDir(offset.normalized);
                _humanoid.SetRun(false);
            }
        }
    }

    // After an area reload or a server restart, Unity creates a new instance of each archer.
    // This hook finds the marker in the ZDO, and adds the guard again.
    [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.Awake))]
    internal static class MonsterAIAwakePatch
    {
        private static void Postfix(MonsterAI __instance)
        {
            var zdo = __instance.m_nview ? __instance.m_nview.GetZDO() : null;
            if (zdo != null && ServerOnlyPlugin.IsArcher(zdo) && ZNet.instance && ZNet.instance.IsDedicated() && !__instance.GetComponent<ArcherGuard>())
            {
                __instance.gameObject.AddComponent<ArcherGuard>();
            }
        }
    }

    // MonsterAI.UpdateAI walks toward targets that the archer hears or cannot reach. This runs
    // after vanilla chooses a movement, and replaces it. Aiming and attacks do not use it.
    [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.UpdateAI))]
    internal static class MonsterAIUpdateAIPatch
    {
        private static void Postfix(MonsterAI __instance, bool __result)
        {
            if (__result)
            {
                var guard = __instance.GetComponent<ArcherGuard>();
                if (guard)
                {
                    guard.Hold();
                }
            }
        }
    }

    // Attack.GetProjectileSpawnPoint aims straight at the center of the target. An arrow falls and
    // the target moves during the flight, so archers aim ahead of the target and above it.
    // Spread and launch angle apply after this hook.
    [HarmonyPatch(typeof(Attack), nameof(Attack.GetProjectileSpawnPoint))]
    internal static class GetProjectileSpawnPointPatch
    {
        private static void Postfix(Attack __instance, Vector3 spawnPoint, ref Vector3 aimDir)
        {
            if (!__instance.m_character || !__instance.m_character.GetComponent<ArcherGuard>() || !__instance.m_baseAI)
            {
                return;
            }

            var target = __instance.m_baseAI.GetTargetCreature();
            if (!target)
            {
                return;
            }

            var center = target.GetCenterPoint();
            var velocity = target.GetVelocity();
            var speed = __instance.m_projectileVel;
            var flightTime = Vector3.Distance(center, spawnPoint) / speed;
            var aimPoint = center;
            for (var i = 0; i < 3; i++)
            {
                aimPoint = center + velocity * flightTime;
                flightTime = Vector3.Distance(aimPoint, spawnPoint) / speed;
            }
            aimPoint.y += 0.5f * ServerOnlyPlugin.ArrowGravity * flightTime * flightTime;
            aimDir = (aimPoint - spawnPoint).normalized;
        }
    }

    // Game.FixedUpdate moves the server reference position far away on each physics tick, so
    // nothing runs on a vanilla server. This keeps the active area on the tower.
    [HarmonyPatch(typeof(ZNet), nameof(ZNet.SetReferencePosition))]
    internal static class SetReferencePositionPatch
    {
        private static void Prefix(ref Vector3 pos)
        {
            if (ServerOnlyPlugin.Focus.HasValue)
            {
                pos = ServerOnlyPlugin.Focus.Value;
            }
        }
    }

    // ZDOMan.ReleaseNearbyZDOS gives each peer the unowned objects near it. Vanilla would give the
    // server all objects near the tower, and creature AI would then run on the server. This keeps
    // only the archers on the server. Peers use vanilla logic, and they cannot take an archer
    // because its owner, the server, stays in its active area.
    [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.ReleaseNearbyZDOS))]
    internal static class ReleaseNearbyZdosPatch
    {
        private static bool Prefix(ZDOMan __instance, Vector3 refPosition, long uid)
        {
            if (uid != ZDOMan.GetSessionID() || !ServerOnlyPlugin.Focus.HasValue)
            {
                return true;
            }

            var zone = ZoneSystem.GetZone(refPosition);
            var distance = ZNet.instance.GetSyncedSimulationDistance();
            var nearby = new List<ZDO>();
            __instance.FindSectorObjects(zone, new SimulationDistance(distance.NearSimulationDistance, 0, distance.IsClassic), nearby);
            foreach (var zdo in nearby)
            {
                if (!zdo.Persistent)
                {
                    continue;
                }

                var keep = (ServerOnlyPlugin.IsArcher(zdo) || ServerOnlyPlugin.ExtraOwned.Contains(zdo.m_uid)) && ZNetScene.InActiveArea(zdo.GetPosition(), zone);
                if (keep && zdo.GetOwner() != uid)
                {
                    zdo.SetOwner(uid);
                }
                else if (!keep && zdo.GetOwner() == uid)
                {
                    zdo.SetOwner(0L);
                }
            }
            return false;
        }
    }
}
