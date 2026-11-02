using HarmonyLib;
using TowerArchers.Components;

namespace TowerArchers.Patches
{
    /// <summary>
    /// Hooks MonsterAI.UpdateAI, the per-frame AI method that runs on the owner of a creature.
    /// Vanilla walks toward a target that it hears but cannot see, or that is out of range.
    /// Vanilla also never returns to a patrol point when m_randomMoveRange is 0. Either one
    /// makes an archer leave or fall from a tower. This postfix runs after vanilla chooses a
    /// movement and replaces that movement for Tower Archers only. Vanilla targeting, aiming,
    /// and attacks are unchanged, because they do not use the movement direction.
    /// </summary>
    [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.UpdateAI))]
    internal static class MonsterAIUpdateAIPatch
    {
        private static void Postfix(MonsterAI __instance, bool __result)
        {
            // False means that the base method did not run the AI, for example on a dead creature.
            if (!__result)
            {
                return;
            }

            var guard = __instance.GetComponent<TowerArcherGuard>();
            if (guard)
            {
                guard.Hold();
            }
        }
    }
}
