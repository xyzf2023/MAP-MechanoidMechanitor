using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// Sleep 结束后的续充兜底 JobGiver。
    ///
    /// 位置：普通 JobGiver_Work 之后。语义：
    /// 仅当 Pawn 当前正在执行由本 MOD 作息充电 giver 发起的 MechCharge（即“续充探测阶段”），
    /// 且 Sleep 已结束、仍低于个人 max、且有合法 Charger 时，继续生成原版 MechCharge。
    ///
    /// 这样“有工作 -> Work 在前面拿走 Job；没工作 -> 才续充”的优先级由 ThinkTree 自然达成：
    /// 续充期间若 JobGiver_Work 在 override 重评估时拿到工作，会中断续充并去工作，
    /// 此时 CurJob 不再是本 MOD 续充 Job，IsExecutingManagedScheduledRecharge 立即为假，
    /// 本 giver 返回 null，整体状态干净退出，不残留任何脏标记。
    /// </summary>
    public sealed class JobGiver_MechanoidMechanitorPostSleepRecharge : ThinkNode_JobGiver
    {
        private const float ScheduledPriority = 9.6f;

        public override float GetPriority(Pawn pawn)
        {
            if (!MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
            {
                return 0f;
            }

            // Sleep 仍有效时由 ScheduledRecharge 负责，这里不参与。
            if (MechanoidMechanitorTimetableUtility.GetCurrentIntent(pawn)
                == MechanoidMechanitorScheduleIntent.Recharge)
            {
                return 0f;
            }

            // 仅在进行“由本 MOD 作息充电发起的续充”期间生效。
            if (!MechanoidMechanitorTimetableUtility.IsExecutingManagedScheduledRecharge(pawn))
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
            job.expiryInterval = 60;
            job.checkOverrideOnExpire = true;
            job.overrideFacing = Rot4.South;
            return job;
        }
    }
}
