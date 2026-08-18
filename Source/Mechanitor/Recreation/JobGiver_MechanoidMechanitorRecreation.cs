using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 机械族机械师空闲娱乐 JobGiver。
    /// 插入到原版“Idle player mech → Idle → WanderColony”之前，
    /// 优先级低于充能、工作、自动冥想、Lord、征召等更高优先级行为。
    /// 不继承 JobGiver_GetJoy，以避免读取并依赖 Need_Joy。
    /// </summary>
    public sealed class JobGiver_MechanoidMechanitorRecreation : ThinkNode_JobGiver
    {
        protected override Job? TryGiveJob(Pawn pawn)
        {
            if (!MechanoidMechanitorRecreationUtility.CanUseRecreationSystem(pawn))
            {
                return null;
            }

            // 每次实际尝试娱乐前都再确认一次 ReadingTracker，
            // 用于处理玩家在游戏过程中刚开启设置的情况。
            MechanoidMechanitorRecreationUtility.EnsureReadingTracker(pawn);

            bool force =
                MechanoidMechanitorRecreationUtility
                    .TryConsumeDebugForceNextRecreation(pawn);

            if (!force
                && !Rand.Chance(
                    MechanoidMechanitorRecreationUtility.IdleRecreationChance))
            {
                return null;
            }

            return MechanoidMechanitorRecreationUtility.TryMakeRecreationJob(pawn);
        }
    }
}
