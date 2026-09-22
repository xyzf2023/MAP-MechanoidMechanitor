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
    /// 重要：本节点位于普通 ThinkNode_Priority 之内。原版 ThinkNode_Priority 按 subNodes
    /// 顺序直接调用 TryIssueJobPackage，进而调用 TryGiveJob，不会先调用 GetPriority()。
    /// 因此 GetPriority 返回 0 并不能阻止 TryGiveJob 被调用。所有资格判断必须集中在
    /// CanRun，GetPriority 与 TryGiveJob 共用同一 helper，避免二者漂移。
    ///
    /// 开始条件（CanRun）：是机械族机械师、本体模式非 Recharge/SelfShutdown、
    /// 当前作息意图为 Recharge（Sleep）、且有合法 mech energy need。
    /// 同层实际顺序由 XML 决定（ScheduledRecharge 位于 ConditionalRecharging 之前），
    /// ScheduledPriority 仅为常规占位，不表示比工作更高的优先级。
    /// </summary>
    public sealed class JobGiver_MechanoidMechanitorScheduledRecharge : ThinkNode_JobGiver
    {
        // 常规占位优先级，仅用于 GetPriority 返回；同层行为顺序由 XML 决定，与数值无关。
        private const float ScheduledPriority = 9.6f;

        private static bool CanRun(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Destroyed)
            {
                return false;
            }

            if (!MechanoidMechanitorCapabilityUtility.HasCapability(pawn, MechanoidMechanitorCapability.ManagedSchedule))
            {
                return false;
            }

            // 明确 Recharge / SelfShutdown 本体模式由原版/本体逻辑处理，不得被 Sleep 软调度覆盖。
            if (MechanoidMechanitorSelfWorkModeUtility.TryGetCurrentMode(
                    pawn,
                    out MechWorkModeDef? mode)
                && (MechanoidMechanitorSelfWorkModeUtility.IsRechargeMode(mode)
                    || MechanoidMechanitorSelfWorkModeUtility.IsSelfShutdownMode(mode)))
            {
                return false;
            }

            if (MechanoidMechanitorTimetableUtility.GetCurrentIntent(pawn)
                != MechanoidMechanitorScheduleIntent.Recharge)
            {
                return false;
            }

            return pawn.needs.energy != null;
        }

        public override float GetPriority(Pawn pawn)
        {
            return CanRun(pawn) ? ScheduledPriority : 0f;
        }

        protected override Job? TryGiveJob(Pawn pawn)
        {
            if (!CanRun(pawn))
            {
                return null;
            }

            if (!MechanoidMechanitorRechargeUtility.TryGetRechargeThresholds(
                    pawn,
                    out FloatRange thresholds))
            {
                return null;
            }

            // Sleep 补电：开始判定为 energy 低于个人 max（向个人上限补齐），而非低于 min。
            Need_MechEnergy energyNeed = pawn.needs.energy!;
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
