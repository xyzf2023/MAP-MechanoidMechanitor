using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class JobDriver_BroadcastSymbiosisDeclaration : JobDriver
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
                    (Toil toil) =>
                    {
                        Thing? thing =
                            toil.actor.jobs.curJob.GetTarget(TargetIndex.A).Thing;
                        return thing is not Building_CommsConsole console
                            || !console.CanUseCommsNow;
                    });

            Toil wait = Toils_General
                .Wait(
                    SymbiosisCovenantCommunicationUtility.DeclarationDurationTicks,
                    TargetIndex.A)
                .WithProgressBarToilDelay(TargetIndex.A);

            wait.FailOn(
                toil =>
                {
                    Thing? thing =
                        toil.actor.jobs.curJob.GetTarget(TargetIndex.A).Thing;
                    return thing is not Building_CommsConsole console
                        || !console.Spawned
                        || !console.CanUseCommsNow
                        || GameComponent_SymbiosisCovenantState
                            .CurrentComponent?.PublicDeclarationBroadcast == true;
                });

            yield return wait;

            Toil broadcast = ToilMaker.MakeToil("BroadcastSymbiosisDeclaration");
            broadcast.initAction = delegate
            {
                GameComponent_SymbiosisCovenantState.CurrentComponent
                    ?.TryBroadcastPublicDeclaration();
            };
            yield return broadcast;
        }
    }
}
