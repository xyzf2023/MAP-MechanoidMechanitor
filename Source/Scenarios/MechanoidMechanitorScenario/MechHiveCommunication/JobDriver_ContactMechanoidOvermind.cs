using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public class JobDriver_ContactMechanoidOvermind : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.InteractionCell)
                .FailOn(
                    (Toil to) =>
                    {
                        Thing? thing = to.actor.jobs.curJob.GetTarget(TargetIndex.A).Thing;
                        return thing is not Building_CommsConsole console || !console.CanUseCommsNow;
                    });

            Toil openComms = ToilMaker.MakeToil("OpenMechanoidOvermindComms");
            openComms.initAction = delegate
            {
                Pawn actor = openComms.actor;
                Thing? thing = actor.jobs.curJob.GetTarget(TargetIndex.A).Thing;
                if (thing is Building_CommsConsole console
                    && console.Spawned
                    && console.CanUseCommsNow)
                {
                    MechanoidMechanitorMechHiveCommunicationUtility.TryOpenContactOvermindDialog(
                        actor,
                        console.Map);
                }
            };
            yield return openComms;
        }
    }
}
