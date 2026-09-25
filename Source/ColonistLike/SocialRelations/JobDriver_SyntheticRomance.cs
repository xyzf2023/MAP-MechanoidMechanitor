using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>沿用原版接近及互动等待 Toil；仅最后一次有效互动建立关系。</summary>
    public sealed class JobDriver_SyntheticRomance : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => job.targetA.Pawn == null
                || SyntheticCompanionRelationshipUtility.PursuitReason(pawn, job.targetA.Pawn) != null);
            Toil approach = Toils_Interpersonal.GotoInteractablePosition(TargetIndex.A);
            approach.socialMode = RandomSocialMode.Off;
            yield return approach;
            yield return Toils_Interpersonal.WaitToBeAbleToInteract(pawn);
            Toil finalApproach = Toils_Interpersonal.GotoInteractablePosition(TargetIndex.A);
            finalApproach.socialMode = RandomSocialMode.Off;
            yield return finalApproach;
            yield return Toils_General.Do(() =>
            {
                Pawn target = job.targetA.Pawn;
                if (target == null || !SyntheticCompanionRelationshipUtility.IsDirectedPursuit(pawn, target)
                    || !pawn.interactions.TryInteractWith(target, InteractionDefOf.RomanceAttempt))
                    EndJobWith(JobCondition.Incompletable);
            });
        }
    }
}
