using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using TowerArchers.Components;
using UnityEngine;

namespace TowerArchers.Prefabs
{
    /// <summary>A friendly stationary archer, cloned from Draugr_Ranged.</summary>
    internal static class TowerArcher
    {
        public const string PrefabName = "TowerArcher";
        private const string BasePrefab = "Draugr_Ranged";

        private static GameObject _prefab;

        public static void Register()
        {
            var creature = new CustomCreature(PrefabName, BasePrefab, new CreatureConfig
            {
                Name = "Tower Archer",
                Faction = Character.Faction.Players,
            });
            _prefab = creature.Prefab;

            var humanoid = _prefab.GetComponent<Humanoid>();
            humanoid.m_group = "";
            humanoid.m_defaultItems = new[] { ArcherBow.Prefab };
            humanoid.m_randomWeapon = new GameObject[0];
            humanoid.m_randomArmor = new GameObject[0];
            humanoid.m_randomShield = new GameObject[0];
            humanoid.m_randomSets = new Humanoid.ItemSet[0];
            humanoid.m_randomItems = new Humanoid.RandomItem[0];

            var drops = _prefab.GetComponent<CharacterDrop>();
            if (drops)
            {
                Object.DestroyImmediate(drops);
            }

            var ai = _prefab.GetComponent<MonsterAI>();
            ai.m_randomMoveRange = 0f;
            ai.m_circulateWhileCharging = false;
            ai.m_circleTargetInterval = 0f;
            ai.m_minAttackInterval = 0f;
            // The vision cone must not depend on the direction the archer faces.
            ai.m_viewAngle = 360f;
            // These settings make vanilla AI run away, or leave the tower to reach a target.
            ai.m_fleeIfHurtWhenTargetCantBeReached = false;
            ai.m_fleeIfNotAlerted = false;
            ai.m_fleeIfLowHealth = 0f;
            ai.m_afraidOfFire = false;
            ai.m_avoidFire = false;
            ai.m_enableHuntPlayer = false;
            ai.m_attackPlayerObjects = false;
            ai.m_sleeping = false;

            ApplyConfig(humanoid, ai);
            _prefab.AddComponent<TowerArcherGuard>();
            CreatureManager.Instance.AddCreature(creature);
        }

        public static void ApplyConfig()
        {
            if (_prefab)
            {
                ApplyConfig(_prefab.GetComponent<Character>(), _prefab.GetComponent<MonsterAI>());
            }
        }

        public static void ApplyConfig(Character character, MonsterAI ai)
        {
            character.m_health = ModConfig.ArcherHealth.Value;
            ai.m_viewRange = ModConfig.ViewRange.Value;
            ai.m_hearRange = ModConfig.HearRange.Value;
            // MonsterAI.UpdateTarget drops the target of a tamed creature when the target is
            // farther than m_alertRange from the patrol point. Attacks also need the alert state.
            ai.m_alertRange = ModConfig.ViewRange.Value;
        }
    }
}
