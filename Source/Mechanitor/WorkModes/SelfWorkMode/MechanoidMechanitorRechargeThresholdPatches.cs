using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 原版 JobGiver_GetEnergy 两个充电阈值入口的补丁。
    /// 这是机械族机械师本体接入原版充电机制的唯一入口，不得新增第二套互相竞争的充电阈值补丁。
    ///
    /// 普通自律状态：读取本 MOD 个人 RechargeThresholds（通过唯一入口 MechanoidMechanitorRechargeUtility）。
    /// 明确 Recharge 本体模式：开始/停止阈值都强制为 100%，不削弱玩家明确的全充指令。
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_GetEnergy), nameof(JobGiver_GetEnergy.GetMinAutorechargeThreshold))]
    public static class Patch_JobGiver_GetEnergy_GetMinAutorechargeThreshold
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn pawn, ref int __result)
        {
            if (!MechanoidMechanitorSelfWorkModeUtility.ShouldApplySelfRechargeThresholds(pawn))
            {
                return;
            }

            // 明确 Recharge 本体模式：未满（低于 100%）即主动去充。
            if (MechanoidMechanitorSelfWorkModeUtility.TryGetCurrentMode(
                    pawn,
                    out MechWorkModeDef? mode)
                && MechanoidMechanitorSelfWorkModeUtility.IsRechargeMode(mode))
            {
                __result = pawn.RaceProps.maxMechEnergy;
                return;
            }

            // Sleep 结束后的续充探测阶段：暂时抑制原版普通自动充电抢在 Work 前面，
            // 让 JobGiver_Work 在每轮 override 重评估时优先拿到工作。
            // 该状态完全由 CurJob 派生，不会残留为永久脏标记。
            if (MechanoidMechanitorTimetableUtility.IsExecutingManagedScheduledRecharge(pawn)
                && MechanoidMechanitorTimetableUtility.GetCurrentIntent(pawn)
                    != MechanoidMechanitorScheduleIntent.Recharge)
            {
                __result = pawn.RaceProps.maxMechEnergy;
                return;
            }

            // 普通自律状态：读取个人 RechargeThresholds.min（绝对能量值）。
            __result = MechanoidMechanitorRechargeUtility.GetStartRechargeEnergy(pawn);
        }
    }

    [HarmonyPatch(typeof(JobGiver_GetEnergy), nameof(JobGiver_GetEnergy.GetMaxRechargeLimit))]
    public static class Patch_JobGiver_GetEnergy_GetMaxRechargeLimit
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn pawn, ref float __result)
        {
            if (!MechanoidMechanitorSelfWorkModeUtility.ShouldApplySelfRechargeThresholds(pawn))
            {
                return;
            }

            // 明确 Recharge 本体模式：充至 100%。
            if (MechanoidMechanitorSelfWorkModeUtility.TryGetCurrentMode(
                    pawn,
                    out MechWorkModeDef? mode)
                && MechanoidMechanitorSelfWorkModeUtility.IsRechargeMode(mode))
            {
                __result = 1f;
                return;
            }

            // 普通自律状态：读取个人 RechargeThresholds.max（0~1 比例）。
            __result = MechanoidMechanitorRechargeUtility.GetStopRechargeThreshold(pawn);
        }
    }
}
