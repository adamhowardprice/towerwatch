using BepInEx;
using HarmonyLib;
using Jotunn.Managers;
using Jotunn.Utils;
using TowerArchers.Components;
using TowerArchers.Prefabs;
using UnityEngine;

namespace TowerArchers
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    // Every peer must have the same prefabs, so every peer must have the mod.
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.adamprice.towerarchers";
        public const string PluginName = "TowerArchers";
        public const string PluginVersion = "0.1.0";

        private void Awake()
        {
            ModConfig.Bind(Config);
            new Harmony(PluginGuid).PatchAll(typeof(Plugin).Assembly);

            PrefabManager.OnVanillaPrefabsAvailable += RegisterPrefabs;
            SynchronizationManager.OnConfigurationSynchronized += (_, _) => ApplyConfig();
            Config.SettingChanged += (_, _) => ApplyConfig();
        }

        // Jotunn fires this event on each main menu load. Prefabs must register once only.
        private void RegisterPrefabs()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= RegisterPrefabs;

            ArcherBow.Register();
            TowerArcher.Register();
            WatchPostPiece.Register();
            Jotunn.Logger.LogInfo("Registered ArcherBowFire, TowerArcher, and WatchPost.");
        }

        private static void ApplyConfig()
        {
            ArcherBow.ApplyConfig();
            TowerArcher.ApplyConfig();
            WatchPostPiece.ApplyConfig();
            TowerArcherGuard.ApplyConfigToActive();
        }
    }
}
