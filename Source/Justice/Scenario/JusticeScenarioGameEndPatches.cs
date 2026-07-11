using HarmonyLib;
using RimWorld;

namespace MAP_MechanoidMechanitor.Scenarios
{
    [HarmonyPatch(typeof(GameEnder), nameof(GameEnder.CheckOrUpdateGameOver))]
    public static class JusticeScenario_GameEnder_CheckOrUpdateGameOver_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(GameEnder __instance)
        {
            if (!JusticeScenarioUtility.ShouldPreventGameOver)
            {
                return true;
            }

            JusticeScenarioUtility.CancelGameOverState(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(GameEnder), nameof(GameEnder.GameEndTick))]
    public static class JusticeScenario_GameEnder_GameEndTick_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(GameEnder __instance)
        {
            if (!__instance.gameEnding && !JusticeScenarioUtility.HasGameEndedLetter)
            {
                return true;
            }

            if (!JusticeScenarioUtility.ShouldPreventGameOver)
            {
                return true;
            }

            JusticeScenarioUtility.CancelGameOverState(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(GameEnder), nameof(GameEnder.CanSpawnNewWanderers))]
    public static class JusticeScenario_GameEnder_CanSpawnNewWanderers_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ref bool __result)
        {
            if (!JusticeScenarioUtility.ShouldPreventGameOver)
            {
                return true;
            }

            __result = false;
            return false;
        }
    }
}
