using HarmonyLib;
using RimWorld;

namespace MAP_MechanoidMechanitor.Scenarios
{
    [HarmonyPatch(typeof(IncidentWorker), nameof(IncidentWorker.CanFireNow))]
    public static class MechanoidMechanitorInsectIncident_CanFireNow_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(
            IncidentWorker __instance,
            ref bool __result)
        {
            if (!MechanoidMechanitorInsectIncidentPolicy
                .ShouldBlock(__instance.def))
            {
                return true;
            }

            MechanoidMechanitorInsectBlockLogUtility.WarnIncidentBlocked(
                __instance.def,
                "IncidentWorker.CanFireNow",
                throttled: true);

            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(IncidentWorker), nameof(IncidentWorker.TryExecute))]
    public static class MechanoidMechanitorInsectIncident_TryExecute_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(
            IncidentWorker __instance,
            ref bool __result)
        {
            if (!MechanoidMechanitorInsectIncidentPolicy
                .ShouldBlock(__instance.def))
            {
                return true;
            }

            MechanoidMechanitorInsectBlockLogUtility.WarnIncidentBlocked(
                __instance.def,
                "IncidentWorker.TryExecute",
                throttled: false);

            __result = false;
            return false;
        }
    }
}
