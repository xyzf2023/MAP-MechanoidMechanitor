using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// Joy 作息时段的调度娱乐 JobGiver（位于紧急/普通 JobGiver_Work 之后、普通工作之前）。
    ///
    /// 重要：本节点位于普通 ThinkNode_Priority 之内，原版按 subNodes 顺序直接调用
    /// TryGiveJob，不会先调用 GetPriority()。所有资格判断集中在 CanRun，
    /// GetPriority 与 TryGiveJob 共用同一 helper。
    ///
    /// 复用现有机械族娱乐系统：CanUseRecreationSystem + TryMakeRecreationJob。
    /// Joy 时段玩家已明确安排娱乐，因此不 Roll IdleRecreationChance；
    /// 若没有合法娱乐设施则 return null，允许正常工作/其他行为，原地发呆。
    /// </summary>
    public sealed class JobGiver_MechanoidMechanitorScheduledRecreation : ThinkNode_JobGiver
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
                != MechanoidMechanitorScheduleIntent.Recreation)
            {
                return false;
            }

            return MechanoidMechanitorRecreationUtility.CanUseRecreationSystem(pawn);
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

            MechanoidMechanitorRecreationUtility.EnsureReadingTracker(pawn);

            // Joy 时段不 Roll IdleRecreationChance；无合法娱乐则返回 null，让工作等行为继续。
            return MechanoidMechanitorRecreationUtility.TryMakeRecreationJob(pawn);
        }
    }
}
