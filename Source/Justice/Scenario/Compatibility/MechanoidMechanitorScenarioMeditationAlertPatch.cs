using HarmonyLib;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(
        typeof(Alert_NeedMeditationSpot),
        nameof(Alert_NeedMeditationSpot.GetReport))]
    public static class MechanoidMechanitorScenario_Alert_NeedMeditationSpot_GetReport_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ref AlertReport __result)
        {
            if (!GameComponent_MechanoidMechanitorScenarioState.IsEnabled)
            {
                return true;
            }

            __result = AlertReport.Inactive;
            return false;
        }
    }
}
