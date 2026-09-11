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
            // 飞行目标可能无法从地面到达；状态机在选点时已经检查占用与预留。
            return true;
        }
    }
}
