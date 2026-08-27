using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// Meditate 作息时段的调度冥想 JobGiver（高优先级入口，位于紧急/普通 JobGiver_Work 之后）。
    ///
    /// 完全复用现有 ShouldAutoMeditate / TryMakeAssignedMeditationJob，
    /// 不重复 HasPsylink / Psyfocus / 安全环境 / Royalty 等判定。
    /// 无 Royalty 时 ShouldAutoMeditate 已返回 false，本 giver 安全失效、不报错。
    /// </summary>
    public sealed class JobGiver_MechanoidMechanitorScheduledMeditate : ThinkNode_JobGiver
    {
        private const float ScheduledPriority = 9.6f;

        public override float GetPriority(Pawn pawn)
        {
            if (!MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
            {
                return 0f;
            }

            if (MechanoidMechanitorTimetableUtility.GetCurrentIntent(pawn)
                != MechanoidMechanitorScheduleIntent.Meditation)
            {
                return 0f;
            }

            return ScheduledPriority;
        }

        protected override Job? TryGiveJob(Pawn pawn)
        {
            // 直接复用现有统一冥想资格判定与 Job 生成，不另行实现第二套冥想资格系统。
            return MechanoidMechanitorPsycastUtility.TryMakeAssignedMeditationJob(pawn);
        }
    }
}
