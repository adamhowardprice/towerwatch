using System.Collections.Generic;
using System.Linq;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace TowerArchers.Prefabs
{
    /// <summary>An ammo-free fire bow for NPCs, cloned from BowFineWood.</summary>
    internal static class ArcherBow
    {
        public const string PrefabName = "ArcherBowFire";
        private const string BasePrefab = "BowFineWood";
        private const string FireArrowPrefab = "ArrowFire";
        private const string DraugrPrefab = "Draugr_Ranged";

        private static ItemDrop.ItemData.SharedData _shared;

        public static GameObject Prefab { get; private set; }

        public static void Register()
        {
            var item = new CustomItem(PrefabName, BasePrefab);
            Prefab = item.ItemPrefab;
            _shared = item.ItemDrop.m_itemData.m_shared;

            var arrow = PrefabManager.Instance.GetPrefab(FireArrowPrefab).GetComponent<ItemDrop>().m_itemData.m_shared;
            var draugrBow = FindBow(PrefabManager.Instance.GetPrefab(DraugrPrefab).GetComponent<Humanoid>());

            // The player attack needs a draw value that NPCs never set. Attack.FireProjectileBurst
            // multiplies damage by this value, so a player attack does no damage from an NPC.
            // The Draugr attack also has triggers that exist on the Draugr animator.
            if (draugrBow != null)
            {
                _shared.m_attack = draugrBow.m_attack.Clone();
                _shared.m_aiAttackMaxAngle = draugrBow.m_aiAttackMaxAngle;
            }
            else
            {
                Jotunn.Logger.LogWarning($"{DraugrPrefab} has no bow. {PrefabName} keeps the {BasePrefab} attack.");
            }
            _shared.m_attack.m_bowDraw = false;
            _shared.m_attack.m_attackProjectile = arrow.m_attack.m_attackProjectile;

            // An empty ammo type makes HaveAmmo and UseAmmo succeed with no ammo item.
            _shared.m_ammoType = "";
            _shared.m_useDurability = false;
            _shared.m_aiAttackRangeMin = 0f;
            _shared.m_aiTargetType = ItemDrop.ItemData.AiTarget.Enemy;

            // With no ammo item, Attack adds no arrow damage to the hit. The bow carries it instead.
            _shared.m_damages.Add(arrow.m_damages);
            _shared.m_attackForce += arrow.m_attackForce;
            _shared.m_attackStatusEffect = arrow.m_attackStatusEffect;
            _shared.m_attackStatusEffectChance = arrow.m_attackStatusEffectChance;

            ApplyConfig();
            ItemManager.Instance.AddItem(item);

            var attack = _shared.m_attack;
            Jotunn.Logger.LogInfo($"{PrefabName}: projectile={attack.m_attackProjectile?.name}, animation={attack.m_attackAnimation}, " +
                $"velocity={attack.m_projectileVel}, accuracy={attack.m_projectileAccuracy}, fire={_shared.m_damages.m_fire}, pierce={_shared.m_damages.m_pierce}");
        }

        public static void ApplyConfig()
        {
            if (_shared != null)
            {
                ApplyConfig(_shared);
            }
        }

        public static void ApplyConfig(ItemDrop.ItemData.SharedData shared)
        {
            shared.m_aiAttackRange = ModConfig.AttackRange.Value;
            shared.m_aiAttackInterval = ModConfig.AttackInterval.Value;
        }

        private static ItemDrop.ItemData.SharedData FindBow(Humanoid draugr)
        {
            IEnumerable<GameObject> candidates = draugr.m_defaultItems
                .Concat(draugr.m_randomWeapon)
                .Concat(draugr.m_randomSets.SelectMany(set => set.m_items));

            return candidates
                .Where(prefab => prefab)
                .Select(prefab => prefab.GetComponent<ItemDrop>()?.m_itemData.m_shared)
                .FirstOrDefault(shared => shared?.m_itemType == ItemDrop.ItemData.ItemType.Bow);
        }
    }
}
