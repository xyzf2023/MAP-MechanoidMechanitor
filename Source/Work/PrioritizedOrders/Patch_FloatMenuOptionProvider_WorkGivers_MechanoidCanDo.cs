using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 仅 Patch FloatMenuOptionProvider_WorkGivers 的 MechanoidCanDo getter。
    /// 不修改 FloatMenuMakerMap、不修改 Pawn.CanTakeOrder、不修改其它 FloatMenuOptionProvider。
    ///
    /// 原版该 getter 恒返回 false，导致基类 FloatMenuOptionProvider.SelectedPawnValid 中
    ///   !MechanoidCanDo &amp;&amp; pawn.RaceProps.IsMechanoid
    /// 对所有机械族成立，从而把机械族整体排除在 WorkGivers 右键菜单之外。
    ///
    /// 本 Patch 在功能开启且当前 pawn 为玩家机械族时返回 true，让该 Provider 对该 pawn 生效；
    /// 其余情况（功能关闭 / 非玩家机械族 / 当前 pawn 无法定位 / 设置未就绪）一律维持原版 false，
    /// 行为完全等同原版。原版的 directOrderable / forced / WorkType / HasJobOnThing / JobOnThing /
    /// 预约 / 可达性 / 区域检查仍由 FloatMenuOptionProvider_WorkGivers 与 WorkGiver 自己执行，
    /// 本 Patch 不提前生成 Job，也不调用 TryTakeOrderedJobPrioritizedWork。
    /// </summary>
    [HarmonyPatch(
        typeof(FloatMenuOptionProvider_WorkGivers),
        "MechanoidCanDo",
        MethodType.Getter)]
    public static class Patch_FloatMenuOptionProvider_WorkGivers_MechanoidCanDo
    {
        [HarmonyPrefix]
        public static bool Prefix(ref bool __result)
        {
            // 设置未开启：保持原版 false，机械族行为完全等同原版。
            if (MAPMechanitorMod.Settings == null ||
                !MAPMechanitorMod.Settings.enableMechanoidPrioritizedWorkOrders)
            {
                return true;
            }

            // FloatMenuMakerMap.makingFor 在生成非多选右键菜单时被设为当前选中的唯一 pawn。
            // WorkGivers Provider 的 Multiselect 为 false，因此该路径恒为单一 pawn，与校验上下文一致。
            Pawn pawn = FloatMenuMakerMap.makingFor;
            if (pawn == null)
            {
                return true;
            }

            // 只对玩家机械族放开；敌对机械族与非机械族保持原版 false。
            if (!MechanoidPrioritizedWorkUtility.CanUsePrioritizedWorkOrders(pawn))
            {
                return true;
            }

            // 在进入真正的菜单生成前，确保 workSettings 已初始化并受限，避免后续 NRE。
            // 初始化失败则维持原版 false，让该 pawn 与原版一样无法直接进入 WorkGivers 菜单，
            // 而不是在 GetWorkGiverOption 中触发 NullReferenceException。
            if (!MechanoidPrioritizedWorkUtility.PreparePawnForPrioritizedWorkOrders(pawn))
            {
                return true;
            }

            __result = true;
            return false; // 跳过原版 getter，固定返回 true
        }
    }
}
