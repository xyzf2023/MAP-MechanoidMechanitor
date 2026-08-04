using HarmonyLib;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// LoadedGame 阶段的休眠监管者擦除会进一步 ClearTarget，可能提前删除活体目标的
    /// 正面指令聚焦。加载安全屏障释放前先跳过；首个安全 GameComponentTick 中，
    /// 现有 DataProcessingPawnLifecycleCoordinator 周期扫描会按最终状态重新执行清理。
    /// </summary>
    [HarmonyPatch(
        typeof(DataProcessingPawnLifecycleCoordinator),
        nameof(DataProcessingPawnLifecycleCoordinator.SuspendOverseerRuntime))]
    internal static class MechanoidMechanitorPostLoadDormantSuspendGuard
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix()
        {
            return !MechanoidMechanitorPostLoadSafetyCoordinator
                .ShouldDeferPositiveRestore;
        }
    }
}
