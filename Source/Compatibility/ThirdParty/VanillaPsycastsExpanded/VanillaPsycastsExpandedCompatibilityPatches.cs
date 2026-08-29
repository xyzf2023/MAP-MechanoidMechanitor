using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.VanillaPsycastsExpanded
{
    /// <summary>
    /// 原版灵能拓展（VPE）兼容的显式 Harmony Postfix。
    /// 本文件不添加任何 [HarmonyPatch] 特性，不使用 PatchAll；
    /// 两个 Postfix 均由 VanillaPsycastsExpandedCompatibility.Apply
    /// 通过传入的 Harmony 实例显式 harmony.Patch() 安装。
    /// </summary>
    internal static class VanillaPsycastsExpandedCompatibilityPatches
    {
        /// <summary>
        /// 即时角色初始化入口：MechanoidMechanitorRoleUtility.EnsureRoleState(Pawn) 的 Postfix。
        /// 覆盖新生成机械师、后天转化机械师，以及其他主动执行 EnsureRoleState 的修复路径。
        /// </summary>
        public static void Postfix_EnsureRoleState(Pawn? pawn)
        {
            VanillaPsycastsExpandedCompatibilityRuntime.EnsurePawnState(pawn);
        }

        /// <summary>
        /// 旧档安全入口：MechanoidMechanitorPostLoadSafetyCoordinator.
        /// TryRestorePositiveSources() 的 Postfix。
        /// 仅当原方法返回 true（正面来源恢复成功）时执行旧档批量修复；
        /// 不改变原方法返回值，不干扰读档安全协调器的重试与死亡保护逻辑；
        /// 自身错误在 Runtime 内部被拦截，不会让 TryRestorePositiveSources 失败。
        /// </summary>
        public static void Postfix_TryRestorePositiveSources(bool __result)
        {
            if (__result)
            {
                VanillaPsycastsExpandedCompatibilityRuntime
                    .EnsureAllRegisteredMechanitors();
            }
        }
    }
}
