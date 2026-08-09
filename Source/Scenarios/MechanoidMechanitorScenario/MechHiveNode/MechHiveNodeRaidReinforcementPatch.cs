using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 针对原版敌对袭击 <see cref="IncidentWorker_RaidEnemy"/> 的统一窄补丁：
    /// 在袭击成功执行后分别通知机械巢节点与普通派系前哨的援军系统。
    /// 以 IncidentParms 实例为去重标识（ConditionalWeakTable，随实例回收，不长期泄漏），
    /// 同一成功执行的袭击即使 Postfix 意外多次也只分发一次。
    /// </summary>
    [HarmonyPatch(typeof(IncidentWorker_RaidEnemy), "TryExecuteWorker")]
    public static class MechHiveNodeRaidReinforcementPatch
    {
        private static readonly ConditionalWeakTable<IncidentParms, object> ProcessedRaids =
            new ConditionalWeakTable<IncidentParms, object>();

        private static readonly object ProcessedMarker = new object();

        [HarmonyPostfix]
        public static void Postfix(bool __result, IncidentParms parms)
        {
            if (!__result || parms == null)
            {
                return;
            }

            // 同一 IncidentParms 实例只处理一次，区分同 tick 的不同袭击。
            if (ProcessedRaids.TryGetValue(parms, out _))
            {
                return;
            }

            ProcessedRaids.Add(parms, ProcessedMarker);
            MechHiveNodeReinforcementService.NotifyHostileRaidExecuted(parms);
            FactionOutpostReinforcementService.NotifyHostileRaidExecuted(parms);
        }
    }
}
