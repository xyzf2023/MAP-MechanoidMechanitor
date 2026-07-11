using HarmonyLib;
using RimWorld;

namespace MAP_MechanoidMechanitor.Scenarios
{
    [HarmonyPatch(typeof(GameEnder), nameof(GameEnder.CheckOrUpdateGameOver))]
    public static class MechanoidMechanitorScenario_GameEnder_CheckOrUpdateGameOver_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(GameEnder __instance)
        {
            if (!MechanoidMechanitorScenarioUtility.ShouldPreventGameOver)
            {
                return true;
            }

            MechanoidMechanitorScenarioUtility.CancelGameOverState(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(GameEnder), nameof(GameEnder.GameEndTick))]
    public static class MechanoidMechanitorScenario_GameEnder_GameEndTick_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(GameEnder __instance)
        {
            if (!__instance.gameEnding && !MechanoidMechanitorScenarioUtility.HasGameEndedLetter)
            {
                return true;
            }

            if (!MechanoidMechanitorScenarioUtility.ShouldPreventGameOver)
            {
                return true;
            }

            MechanoidMechanitorScenarioUtility.CancelGameOverState(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(GameEnder), nameof(GameEnder.CanSpawnNewWanderers))]
    public static class MechanoidMechanitorScenario_GameEnder_CanSpawnNewWanderers_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ref bool __result)
        {
            if (!MechanoidMechanitorScenarioUtility.ShouldPreventGameOver)
            {
                return true;
            }

            __result = false;
            return false;
        }
    }
}
