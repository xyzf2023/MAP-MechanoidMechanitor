using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    public sealed class JobDriver_MechanicalFlightEmergencyLanding : JobDriver
    {
        protected override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            yield return new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Never
            };
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // 飞行目标可能无法从地面到达；预留由状态机复核，但必须真实持有目标格。
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }
    }
}
