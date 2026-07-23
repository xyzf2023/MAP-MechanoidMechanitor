using HarmonyLib;
using RimWorld;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 仅针对原版敌对袭击 <see cref="IncidentWorker_RaidEnemy"/> 的窄补丁：
    /// 在袭击成功执行后检查是否应由完整机械巢节点提供机械族盟军援军。
    /// 只在袭击确定成功执行时触发一次，避免袭击失败却仍到达援军或重复执行。
    /// </summary>
    [HarmonyPatch(typeof(IncidentWorker_RaidEnemy), "TryExecuteWorker")]
    public static class MechHiveNodeRaidReinforcementPatch
    {
        [HarmonyPostfix]
        public static void Postfix(bool __result, IncidentParms parms)
        {
            if (!__result)
            {
                return;
            }

            MechHiveNodeReinforcementService.NotifyHostileRaidExecuted(parms);
        }
    }
}
