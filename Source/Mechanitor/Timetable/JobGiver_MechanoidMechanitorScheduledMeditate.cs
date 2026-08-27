using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// Meditate 作息时段的调度冥想 JobGiver（位于紧急/普通 JobGiver_Work 之后、普通工作之前）。
    ///
    /// 重要：本节点位于普通 ThinkNode_Priority 之内，原版按 subNodes 顺序直接调用
    /// TryGiveJob，不会先调用 GetPriority()。所有资格判断集中在 CanRun，
    /// GetPriority 与 TryGiveJob 共用同一 helper。
    ///
    /// 完全复用现有 ShouldAutoMeditate / TryMakeAssignedMeditationJob，
    /// 不重复 HasPsylink / Psyfocus / 安全环境 / Royalty 等判定。
    /// 无 Royalty 时 ShouldAutoMeditate 已返回 false，本 giver 安全失效、不报错。
    /// </summary>
    public sealed class JobGiver_MechanoidMechanitorScheduledMeditate : ThinkNode_JobGiver
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

            if (MechanoidMechanitorTimetableUtility.GetCurrentIntent(pawn)
                != MechanoidMechanitorScheduleIntent.Meditation)
            {
                return false;
            }

            // 资格（Psylink / Psyfocus / 安全环境 / Royalty）统由现有 PsycastUtility 负责。
            return MechanoidMechanitorPsycastUtility.ShouldAutoMeditate(pawn, out _);
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

            // 直接复用现有统一冥想资格判定与 Job 生成，不另行实现第二套冥想资格系统。
            return MechanoidMechanitorPsycastUtility.TryMakeAssignedMeditationJob(pawn);
        }
    }
}
