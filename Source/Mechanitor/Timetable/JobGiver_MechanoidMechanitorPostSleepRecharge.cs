using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// Sleep 结束后的续充兜底 JobGiver（位于普通 JobGiver_Work 之后、Neutral 自动冥想之前）。
    ///
    /// 重要：本节点位于普通 ThinkNode_Priority 之内，原版按 subNodes 顺序直接调用
    /// TryGiveJob，不会先调用 GetPriority()。所有资格判断集中在 CanRun，
    /// GetPriority 与 TryGiveJob 共用同一 helper。
    ///
    /// 语义（CanRun）：仅当 Pawn 当前正在执行由本 MOD 作息充电 giver 发起的 MechCharge
    /// （“续充探测阶段”），且 Sleep 已结束（意图 != Recharge）、本体模式非 Recharge/SelfShutdown、
    /// 且有合法 mech energy need 时，本 giver 才可能生效。
    ///
    /// “有工作 -> Work 在前面拿走 Job；没工作 -> 才续充”的优先级由 ThinkTree 顺序自然达成：
    /// 续充期间若 JobGiver_Work 在 override 重评估时拿到工作，会中断续充并去工作，
    /// 此时 CurJob 不再是本 MOD 续充 Job，IsExecutingManagedScheduledRecharge 立即为假，
    /// 本 giver 返回 null，整体状态干净退出，不残留任何脏标记。
    /// </summary>
    public sealed class JobGiver_MechanoidMechanitorPostSleepRecharge : ThinkNode_JobGiver
    {
        private const float ScheduledPriority = 9.6f;

        private static bool CanRun(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Destroyed)
            {
                return false;
            }

            if (!MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
            {
                return false;
            }

            // Sleep 仍有效时由 ScheduledRecharge 负责；这里只在 Sleep 结束后续充。
            if (MechanoidMechanitorTimetableUtility.GetCurrentIntent(pawn)
                == MechanoidMechanitorScheduleIntent.Recharge)
            {
                return false;
            }

            // 仅在进行“由本 MOD 作息充电发起的续充”期间生效。
            if (!MechanoidMechanitorTimetableUtility.IsExecutingManagedScheduledRecharge(pawn))
            {
                return false;
            }

            // 明确 Recharge / SelfShutdown 本体模式不得被续充覆盖。
            if (MechanoidMechanitorSelfWorkModeUtility.TryGetCurrentMode(
                    pawn,
                    out MechWorkModeDef? mode)
                && (MechanoidMechanitorSelfWorkModeUtility.IsRechargeMode(mode)
                    || MechanoidMechanitorSelfWorkModeUtility.IsSelfShutdownMode(mode)))
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
            job.expiryInterval = 60;
            job.checkOverrideOnExpire = true;
            job.overrideFacing = Rot4.South;
            return job;
        }
    }
}
