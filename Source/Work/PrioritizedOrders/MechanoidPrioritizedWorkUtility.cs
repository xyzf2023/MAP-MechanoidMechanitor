using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 玩家机械族「右键优先工作指令」的资格判断与 workSettings 准备辅助。
    /// 本工具只负责解除 FloatMenuOptionProvider_WorkGivers 对机械族的默认禁用，
    /// 不复制、不替换任何原版 WorkGiver / Job / HasJobOnThing / JobOnThing 逻辑。
    /// </summary>
    public static class MechanoidPrioritizedWorkUtility
    {
        /// <summary>
        /// 判断指定 pawn 是否可以通过右键菜单接收优先工作指令。
        /// 仅玩家所属机械族、且功能开启、且存活时返回 true。
        /// </summary>
        public static bool CanUsePrioritizedWorkOrders(Pawn pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            // 设置未开启时完全等同原版：机械族无法进入 WorkGivers 右键菜单。
            if (MAPMechanitorMod.Settings == null ||
                !MAPMechanitorMod.Settings.enableMechanoidPrioritizedWorkOrders)
            {
                return false;
            }

            // 必须使用 IsColonyMech 而非 RaceProps.IsMechanoid：
            // 只有玩家所属的机械族才能获得玩家命令，敌对机械族必须被排除。
            if (!pawn.IsColonyMech)
            {
                return false;
            }

            if (pawn.Dead)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 在进入 WorkGivers 右键菜单生成前，确保玩家机械族的 workSettings 已初始化并受限，
        /// 避免后续 GetWorkGiverOption 访问 pawn.workSettings 时触发 NullReferenceException。
        /// 仅在首次初始化时调用 RestrictToMechEnabledWorkTypes，
        /// 避免覆盖玩家已经手动调整过的工作设置。
        /// </summary>
        /// <returns>准备成功返回 true；不满足资格或初始化失败返回 false。</returns>
        public static bool PreparePawnForPrioritizedWorkOrders(Pawn pawn)
        {
            if (!CanUsePrioritizedWorkOrders(pawn))
            {
                return false;
            }

            bool wasInitialized = pawn.workSettings != null && pawn.workSettings.Initialized;

            if (!MechWorkSettingsUtility.TryEnsureWorkSettingsInitialized(pawn))
            {
                return false;
            }

            // 仅当本次为首次初始化时才限制到 mechEnabledWorkTypes，
            // 防止重复刷新玩家已经手动调整过的工作优先级。
            if (!wasInitialized)
            {
                MechWorkSettingsUtility.RestrictToMechEnabledWorkTypes(pawn);
            }

            return true;
        }
    }
}
