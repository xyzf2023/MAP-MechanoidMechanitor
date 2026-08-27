using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// Joy 作息时段的调度娱乐 JobGiver（位于紧急/普通 JobGiver_Work 之后）。
    ///
    /// 复用现有机械族娱乐系统：CanUseRecreationSystem + TryMakeRecreationJob。
    /// Joy 时段玩家已明确安排娱乐，因此不 Roll IdleRecreationChance；
    /// 若没有合法娱乐设施则 return null，允许正常工作/其他行为，原地发呆。
    /// </summary>
    public sealed class JobGiver_MechanoidMechanitorScheduledRecreation : ThinkNode_JobGiver
    {
        private const float ScheduledPriority = 9.6f;

        public override float GetPriority(Pawn pawn)
        {
            if (!MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
            {
                return 0f;
            }

            if (MechanoidMechanitorTimetableUtility.GetCurrentIntent(pawn)
                != MechanoidMechanitorScheduleIntent.Recreation)
            {
                return 0f;
            }

            return ScheduledPriority;
        }

        protected override Job? TryGiveJob(Pawn pawn)
        {
            if (!MechanoidMechanitorRecreationUtility.CanUseRecreationSystem(pawn))
            {
                return null;
            }

            MechanoidMechanitorRecreationUtility.EnsureReadingTracker(pawn);

            // Joy 时段不 Roll IdleRecreationChance；无合法娱乐则返回 null，让工作等行为继续。
            return MechanoidMechanitorRecreationUtility.TryMakeRecreationJob(pawn);
        }
    }
}
