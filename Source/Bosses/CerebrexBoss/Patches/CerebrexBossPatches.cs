using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 让被主脑 EMP 瘫痪的用电建筑在瘫痪期间报告断电。仅覆盖 PowerOn getter 的返回值，
    /// 不修改 powerOnInt、不破坏电网、不消耗燃料、不改变建筑 Faction 或造成任何伤害。
    /// 仅在奥德赛 DLC 启用时应用。
    /// </summary>
    [HarmonyPatch(typeof(CompPowerTrader), nameof(CompPowerTrader.PowerOn), MethodType.Getter)]
    public static class CompPowerTrader_PowerOn_CerebrexEmpPatch
    {
        public static bool Prepare() => ModsConfig.OdysseyActive;

        public static void Postfix(CompPowerTrader __instance, ref bool __result)
        {
            if (__result
                && __instance?.parent != null
                && CerebrexPowerDisruptionUtility.IsDisabled(__instance.parent))
            {
                __result = false;
            }
        }
    }

    /// <summary>
    /// 极窄地挂钩原版私有方法 CompCerebrexCore.StartCoreDeactivation（玩家选择“摧毁”或“拆取”时调用），
    /// 用于让主脑战斗控制器在关闭流程开始时完整清理技能状态。不修改原版方法本身，
    /// 也不触碰 900 tick 互动、结局、信件、奖励或派系停机逻辑。
    /// 仅在奥德赛 DLC 启用时应用。
    /// </summary>
    [HarmonyPatch]
    public static class CompCerebrexCore_StartCoreDeactivation_Patch
    {
        public static bool Prepare() => ModsConfig.OdysseyActive;

        public static MethodBase? TargetMethod()
        {
            return AccessTools.Method(typeof(CompCerebrexCore), "StartCoreDeactivation");
        }

        public static void Postfix(CompCerebrexCore __instance)
        {
            __instance?.parent?.TryGetComp<CompCerebrexBossController>()?.Notify_CoreDeactivationStarted();
        }
    }

    /// <summary>
    /// 在最后一个稳定器失效、原版主脑正式解除防御并允许互动后，
    /// 通知本MOD主脑战斗控制器结束BOSS战斗并清理仍在运行的技能。
    /// 不修改原版稳定器计数、信件、互动条件或结局逻辑。
    /// 仅在奥德赛DLC启用时应用。
    /// </summary>
    [HarmonyPatch]
    public static class CompCerebrexCore_LowerDefences_Patch
    {
        public static bool Prepare() => ModsConfig.OdysseyActive;

        public static MethodBase? TargetMethod()
        {
            return AccessTools.Method(typeof(CompCerebrexCore), "LowerDefences");
        }

        public static void Postfix(CompCerebrexCore __instance)
        {
            __instance?.parent?
                .TryGetComp<CompCerebrexBossController>()?
                .Notify_CoreDefencesLowered();
        }
    }
}
