using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 阵列在读档 PostSpawnSetup/首个 CompTick 中可能早于机械师身份与数据处理校正运行。
    /// 此阶段不允许依据临时资格状态解除目标、回收分配或刷新意识；只记录待刷新目标。
    /// </summary>
    [HarmonyPatch(
        typeof(CompParallelThoughtArray),
        nameof(CompParallelThoughtArray.ReevaluateOperatingState))]
    internal static class MechanoidMechanitorPostLoadArrayReevaluationGuard
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(CompParallelThoughtArray __instance)
        {
            if (!MechanoidMechanitorPostLoadSafetyCoordinator.ShouldDeferPositiveRestore)
            {
                return true;
            }

            MechanoidMechanitorPostLoadSafetyCoordinator
                .QueueDynamicConsciousnessRefresh(__instance?.Target);
            return false;
        }
    }
}
