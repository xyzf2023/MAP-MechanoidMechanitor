using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 作息 Sleep 时段的主动补电 JobGiver（三层架构中 C 层的“Recharge 执行入口”）。
    ///
    /// 只决定“为什么现在应该充电”，实际 Job 仍复用原版 MechCharge +
    /// JobGiver_GetEnergy_Charger 的寻站/预约/JobDriver 逻辑，不复制第二套充电实现。
    ///
    /// 开始条件：当前作息意图为 Recharge（Sleep）且当前能量低于个人 max。
    /// 注意：Sleep 的意义是“利用休息时间把能源向 max 补齐”，所以判定是 energy &lt; max 而非 &lt; min。
    ///
    /// 明确 Recharge 本体模式与 SelfShutdown 模式由其它逻辑/原版处理，这里直接放行返回 null，
    /// 不得削弱明确的 Recharge 指令。
    /// </summary>
    public sealed class JobGiver_MechanoidMechanitorScheduledRecharge : ThinkNode_JobGiver
    {
        // 软调度优先级：高于普通工作与自动冥想，但原位紧急工作（如救火）由更高层 giver 处理，
        // 不在本 Work 子树内，不会因此被掩盖。
        private const float ScheduledPriority = 9.6f;

        public override float GetPriority(Pawn pawn)
        {
            if (!MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
            {
                return 0f;
            }

            if (MechanoidMechanitorSelfWorkModeUtility.TryGetCurrentMode(
                    pawn,
                    out MechWorkModeDef? mode)
                && (MechanoidMechanitorSelfWorkModeUtility.IsRechargeMode(mode)
                    || MechanoidMechanitorSelfWorkModeUtility.IsSelfShutdownMode(mode)))
            {
                return 0f;
            }

            if (MechanoidMechanitorTimetableUtility.GetCurrentIntent(pawn)
                != MechanoidMechanitorScheduleIntent.Recharge)
            {
                return 0f;
            }

            return ScheduledPriority;
        }

        protected override Job? TryGiveJob(Pawn pawn)
        {
            if (!MechanoidMechanitorRechargeUtility.TryGetRechargeThresholds(
                    pawn,
                    out FloatRange thresholds))
            {
                return null;
            }

            // Sleep 补电：开始判定为 energy < max（向个人上限补齐），而非 < min。
            Need_MechEnergy energyNeed = pawn.needs.energy;
            if (energyNeed == null)
            {
                return null;
            }

            float maxEnergy = pawn.RaceProps.maxMechEnergy * thresholds.max;
            if (energyNeed.CurLevel >= maxEnergy)
            {
                return null;
            }

            Building_MechCharger closestCharger =
                JobGiver_GetEnergy_Charger.GetClosestCharger(pawn, pawn, forced: false);
            if (closestCharger == null)
            {
                return null;
            }

            Job job = JobMaker.MakeJob(JobDefOf.MechCharge, closestCharger);
            // 短到期 + 允许 override：周期性触发 Pawn_JobTracker.CheckForJobOverride，
            // 让 Sleep 结束/出现工作时能重新评估，而不是每 Tick 重启 Job。
            job.expiryInterval = 60;
            job.checkOverrideOnExpire = true;
            job.overrideFacing = Rot4.South;
            return job;
        }
    }
}
