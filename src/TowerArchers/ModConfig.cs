using BepInEx.Configuration;

namespace TowerArchers
{
    /// <summary>Numeric tuning. The server value overrides the client value through Jotunn.</summary>
    internal static class ModConfig
    {
        public static ConfigEntry<float> ArcherHealth;
        public static ConfigEntry<float> ViewRange;
        public static ConfigEntry<float> HearRange;
        public static ConfigEntry<float> AttackRange;
        public static ConfigEntry<float> AttackInterval;
        public static ConfigEntry<float> LeashDistance;
        public static ConfigEntry<int> WatchPostCost;

        public static void Bind(ConfigFile config)
        {
            ArcherHealth = Bind(config, "Archer", "Health", 150f, "Maximum health of a Tower Archer.", 1f, 5000f);
            ViewRange = Bind(config, "Archer", "ViewRange", 50f, "Distance at which an archer sees a target, and at which it keeps a target.", 1f, 200f);
            HearRange = Bind(config, "Archer", "HearRange", 50f, "Distance at which an archer hears a target.", 1f, 200f);
            LeashDistance = Bind(config, "Archer", "LeashDistance", 6f, "An archer farther than this from its post teleports back to the post.", 1f, 100f);
            AttackRange = Bind(config, "Bow", "AttackRange", 50f, "Distance at which an archer fires.", 1f, 200f);
            AttackInterval = Bind(config, "Bow", "AttackInterval", 2.5f, "Minimum seconds between shots.", 0.1f, 30f);
            WatchPostCost = Bind(config, "WatchPost", "FineWoodCost", 10, "Fine Wood cost of a Watch Post.", 1, 100);
        }

        private static ConfigEntry<T> Bind<T>(ConfigFile config, string section, string key, T value, string description, T min, T max)
            where T : System.IComparable
        {
            return config.Bind(section, key, value, new ConfigDescription(
                description,
                new AcceptableValueRange<T>(min, max),
                new ConfigurationManagerAttributes { IsAdminOnly = true }));
        }
    }
}
