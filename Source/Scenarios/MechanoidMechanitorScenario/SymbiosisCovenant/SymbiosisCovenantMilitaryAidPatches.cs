using HarmonyLib;
using RimWorld;

namespace MAP_MechanoidMechanitor.Scenarios
{
    [HarmonyPatch(typeof(IncidentWorker_RaidEnemy), "TryExecuteWorker")]
    public static class SymbiosisCovenantMilitaryAidRaidListenerPatch
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(
            IncidentWorker_RaidEnemy __instance,
            IncidentParms parms,
            bool __result)
        {
            if (__result)
            {
                SymbiosisCovenantMilitaryAidUtility.NotifyRaidSucceeded(__instance, parms);
            }
        }
    }

    [HarmonyPatch(
        typeof(GameComponent_SymbiosisCovenantState),
        nameof(GameComponent_SymbiosisCovenantState.GameComponentTick))]
    public static class SymbiosisCovenantMilitaryAidGameComponentTickPatch
    {
        public static void Postfix(GameComponent_SymbiosisCovenantState __instance)
        {
            SymbiosisCovenantMilitaryAidUtility.Tick(__instance);
        }
    }

    [HarmonyPatch(
        typeof(GameComponent_SymbiosisCovenantState),
        nameof(GameComponent_SymbiosisCovenantState.ExposeData))]
    public static class SymbiosisCovenantMilitaryAidExposeDataPatch
    {
        public static void Postfix(GameComponent_SymbiosisCovenantState __instance)
        {
            SymbiosisCovenantMilitaryAidUtility.ExposeData(__instance);
        }
    }
}
