using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.VanillaPsycastsExpanded
{
    /// <summary>
    /// 原版灵能拓展（VPE）兼容的显式 Harmony 补丁。
    /// 本文件不添加任何 [HarmonyPatch] 特性，不使用 PatchAll；
    /// 全部补丁均由 VanillaPsycastsExpandedCompatibility.Apply
    /// 通过传入的 Harmony 实例显式 harmony.Patch() 安装。
    /// 当前共四个入口：
    /// - Postfix_EnsureRoleState：机械师身份初始化（EnsureRoleState Postfix）；
    /// - Postfix_TryRestorePositiveSources：旧档恢复（TryRestorePositiveSources Postfix）；
    /// - Prefix_TryGainPsylinkLevel：已有启灵神经的升级前修复（TryGainPsylinkLevel Prefix）；
    /// - Postfix_TryGainPsylinkLevel：首次新增启灵神经后的即时修复（TryGainPsylinkLevel Postfix）。
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

        /// <summary>
        /// 灵能中枢升级前修复：PsychicCoreUtility.TryGainPsylinkLevel(Pawn) 的 Prefix。
        /// 在原方法正式执行前调用 Runtime.EnsurePawnState：
        /// - 若 Pawn 已拥有启灵神经但缺少 VPE_PsycastAbilityImplant，先补齐灵拓状态，
        ///   保证随后原方法执行 psylink.ChangeLevel(1) 时，灵拓自己的 ChangeLevel 补丁
        ///   能够取得有效的灵拓状态并正常升级；
        /// - 若 Pawn 尚未拥有启灵神经，EnsurePawnState 会安全返回，不产生副作用。
        /// 所有前置条件判断与异常边界由 Runtime 统一负责，本方法不做任何自行判断。
        /// </summary>
        public static void Prefix_TryGainPsylinkLevel(Pawn? pawn)
        {
            VanillaPsycastsExpandedCompatibilityRuntime.EnsurePawnState(pawn);
        }

        /// <summary>
        /// 灵能中枢完成后的即时修复：PsychicCoreUtility.TryGainPsylinkLevel(Pawn) 的 Postfix。
        /// 在原方法正常完成后再次调用 Runtime.EnsurePawnState：
        /// - 原方法新增启灵神经后立即补齐 VPE_PsycastAbilityImplant，
        ///   让玩家阵营机械族机械师立即满足 VPE 灵能 ITab 的可见条件；
        /// - 若灵拓自己的 Hediff_Psylink.PostAdd 补丁已经成功添加该 Hediff，
        ///   本入口因状态已存在而幂等返回，不重复添加、不重复初始化、不重置灵拓数据；
        /// - 若本次没有成功获得启灵神经，EnsurePawnState 会安全返回。
        /// </summary>
        public static void Postfix_TryGainPsylinkLevel(Pawn? pawn)
        {
            VanillaPsycastsExpandedCompatibilityRuntime.EnsurePawnState(pawn);
        }
    }
}
