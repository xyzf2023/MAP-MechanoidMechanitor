using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 机械族机械师本体没有 Overseer/控制组时，原版会把充电阈值退回 maxMechEnergy，
    /// 导致自律指令下几乎一直去充电。此处仅对无控制组的机械族机械师覆盖默认阈值。
    /// </summary>
    [HarmonyPatch(
        typeof(JobGiver_GetEnergy),
        nameof(JobGiver_GetEnergy.GetMinAutorechargeThreshold))]
    public static class Patch_JobGiver_GetEnergy_GetMinAutorechargeThreshold
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn pawn, ref int __result)
        {
            if (!MechanoidMechanitorSelfWorkModeUtility.ShouldApplyDefaultRechargeThresholds(pawn))
            {
                return;
            }

            // 充电模式：未满电即主动充电（与原版 ignoreGroupChargeLimits 语义一致）
            if (MechanoidMechanitorSelfWorkModeUtility.TryGetCurrentMode(
                    pawn,
                    out MechWorkModeDef? mode)
                && MechanoidMechanitorSelfWorkModeUtility.IsRechargeMode(mode))
            {
                __result = pawn.RaceProps.maxMechEnergy;
                return;
            }

            // 自律指令等：使用原版控制组默认最低阈值（5%）
            __result = Mathf.RoundToInt(
                pawn.RaceProps.maxMechEnergy
                * MechanitorControlGroup.DefaultMechRechargeThresholds.min);
        }
    }

    [HarmonyPatch(
        typeof(JobGiver_GetEnergy),
        nameof(JobGiver_GetEnergy.GetMaxRechargeLimit))]
    public static class Patch_JobGiver_GetEnergy_GetMaxRechargeLimit
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn pawn, ref float __result)
        {
            if (!MechanoidMechanitorSelfWorkModeUtility.ShouldApplyDefaultRechargeThresholds(pawn))
            {
                return;
            }

            // 所有本体模式统一充至 100%
            __result = pawn.RaceProps.maxMechEnergy;
        }
    }
}
