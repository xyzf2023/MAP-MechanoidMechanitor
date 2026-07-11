using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    public class HediffCompProperties_MechHackNoMove : HediffCompProperties
    {
        public int gotoCheckIntervalTicks = 300;

        public HediffCompProperties_MechHackNoMove()
        {
            compClass = typeof(HediffComp_MechHackNoMove);
        }
    }

    public class HediffComp_MechHackNoMove : HediffComp
    {
        public HediffCompProperties_MechHackNoMove Props =>
            (HediffCompProperties_MechHackNoMove)props;

        public override void CompPostTick(ref float severityAdjustment)
        {
            Pawn targetPawn = parent.pawn;
            if (targetPawn == null || !targetPawn.Spawned)
            {
                return;
            }

            targetPawn.pather?.StopDead();

            if (!targetPawn.IsHashIntervalTick(Props.gotoCheckIntervalTicks))
            {
                return;
            }

            if (targetPawn.CurJobDef == JobDefOf.Goto)
            {
                targetPawn.jobs?.EndCurrentJob(JobCondition.InterruptForced);
            }
        }
    }
}
