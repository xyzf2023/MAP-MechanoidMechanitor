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
            // 原版 ShouldAutoRecharge 判断 energy + 0.1 < GetMinAutorechargeThreshold；
            // 把 min 设成 0 会使该式恒为 false，从而普通 JobGiver_GetEnergy_Charger 不产生 Job。
            // 注意：明确 Recharge 本体模式的判定已在上方优先返回 maxMechEnergy，不受此处影响。
            // 该状态完全由 CurJob 派生，不会残留为永久脏标记。
            if (MechanoidMechanitorTimetableUtility.IsExecutingManagedScheduledRecharge(pawn)
                && MechanoidMechanitorTimetableUtility.GetCurrentIntent(pawn)
                    != MechanoidMechanitorScheduleIntent.Recharge)
            {
                __result = 0;
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

            // 明确 Recharge 本体模式：充至 100%（绝对能量值，不是 0~1 比例）。
            if (MechanoidMechanitorSelfWorkModeUtility.TryGetCurrentMode(
                    pawn,
                    out MechWorkModeDef? mode)
                && MechanoidMechanitorSelfWorkModeUtility.IsRechargeMode(mode))
            {
                __result = pawn.RaceProps.maxMechEnergy;
                return;
            }

            // 普通自律状态：读取个人 RechargeThresholds.max 换算为“绝对能量值”。
            // 原版 GetMaxRechargeLimit 返回的是绝对 mech energy，不是百分比。
            // 例：maxMechEnergy=100、max=0.8 -> 返回 80，而非 0.8。
            __result = MechanoidMechanitorRechargeUtility.GetStopRechargeEnergy(pawn);
        }
    }

    /// <summary>
    /// 精准绕过原版 ThinkNode_ConditionalRecharging：Sleep 结束后让 WorkMode 获得一次正常评估。
    /// 仅当“当前 Job 是作息时间充电 giver 发起的 MechCharge + 当前已不是 Sleep + 本体非明确 Recharge”
    /// 时才把 Satisfied 改为 false，使原版“保持当前充电”分支暂时失效，让工作有机会抢占续充。
    /// 普通机械族 / 普通机械族机械师正常自动充电 / 明确 Recharge 模式 / 仍在 Sleep 均不受影响。
    /// </summary>
    [HarmonyPatch(typeof(ThinkNode_ConditionalRecharging), "Satisfied")]
    public static class Patch_ThinkNode_ConditionalRecharging_Satisfied
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn pawn, ref bool __result)
        {
            // 1. 原版本就判定为“正在充电”（Satisfied == true）。
            if (!__result)
            {
                return;
            }

            // 2. 具备托管作息行为能力。
            if (!MechanoidMechanitorCapabilityUtility.HasCapability(
                    pawn, MechanoidMechanitorCapability.ManagedSchedule))
            {
                return;
            }

            // 3. 当前 Job 由本 MOD 作息充电 giver 发起（managed scheduled recharge）。
            if (!MechanoidMechanitorTimetableUtility.IsExecutingManagedScheduledRecharge(pawn))
            {
                return;
            }

            // 4. 当前作息已不是 Sleep（Sleep 已结束）。
            if (MechanoidMechanitorTimetableUtility.GetCurrentIntent(pawn)
                == MechanoidMechanitorScheduleIntent.Recharge)
            {
                return;
            }

            // 5. 本体模式不是明确 Recharge（明确 Recharge 模式不得被强制 false）。
            if (MechanoidMechanitorSelfWorkModeUtility.TryGetCurrentMode(
                    pawn,
                    out MechWorkModeDef? mode)
                && MechanoidMechanitorSelfWorkModeUtility.IsRechargeMode(mode))
            {
                return;
            }

            __result = false;
        }
    }
}
