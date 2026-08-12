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

    /// <summary>
    /// DEV 快照必须反映真实的触发前置条件：没有合法响应成员时，
    /// 正式 EvaluatePendingRaid 会直接退出，因此有效响应概率应为 0%。
    /// </summary>
    [HarmonyPatch(
        typeof(SymbiosisCovenantMilitaryAidUtility),
        nameof(SymbiosisCovenantMilitaryAidUtility.GetDevSnapshot))]
    public static class SymbiosisCovenantMilitaryAidDevSnapshotAccuracyPatch
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(
            SymbiosisCovenantMilitaryAidUtility.SymbiosisCovenantMilitaryAidDevSnapshot __result)
        {
            if (__result != null
                && __result.CurrentThreatFaction != null
                && __result.EligibleResponderCount <= 0)
            {
                __result.ResponderChanceBonus = 0f;
                __result.EffectiveOfferChance = 0f;
            }
        }
    }
}
