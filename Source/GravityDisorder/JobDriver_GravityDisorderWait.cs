using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    public sealed class JobGiver_GravityDisorder : ThinkNode_JobGiver
    {
        protected override Job? TryGiveJob(Pawn pawn) =>
            GravityDisorderUtility.IsAffected(pawn)
                ? GravityDisorderUtility.GetControlJob(pawn) : null;
    }

    public sealed class JobDriver_GravityDisorderWait : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Toil wait = ToilMaker.MakeToil("GravityDisorderWait");
            wait.defaultCompleteMode = ToilCompleteMode.Never;
            // 兼容同时存在的 EMP/其他眩晕，不能让失能任务连第一个步骤都无法进入。
            wait.atomicWithPrevious = true;
            wait.socialMode = RandomSocialMode.Off;
            wait.initAction = () => pawn.pather?.StopDead();
            wait.AddEndCondition(() => GravityDisorderUtility.IsAffected(pawn)
                ? JobCondition.Ongoing : JobCondition.InterruptForced);
            yield return wait;
        }
    }
}
