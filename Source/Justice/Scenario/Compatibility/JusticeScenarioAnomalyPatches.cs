using HarmonyLib;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(
        typeof(IncidentWorker_VoidCuriosity),
        "TryExecuteWorker",
        new[] { typeof(IncidentParms) })]
    public static class JusticeScenario_VoidCuriosity_Patch
    {
        [HarmonyPrepare]
        public static bool Prepare()
        {
            return ModsConfig.AnomalyActive;
        }

        [HarmonyPrefix]
        public static bool Prefix(ref bool __result)
        {
            if (!GameComponent_JusticeScenarioState.IsEnabled)
            {
                return true;
            }

            __result = true;
            return false;
        }
    }
}
