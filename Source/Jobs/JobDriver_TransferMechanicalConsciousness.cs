using System.Collections.Generic;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    public class JobDriver_TransferMechanicalConsciousness : JobDriver
    {
        private const int TransferDurationTicks = 600;
        private const string FailedKey = "MAP_MechanoidMechanitor.ConsciousnessTransfer.Failed";

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddFailCondition(() => !CanTransferNow());

            yield return Toils_General.StopDead();

            Toil wait = Toils_General.Wait(TransferDurationTicks, TargetIndex.A);
            wait.FailOn(() => !CanTransferNow());
            wait.WithProgressBarToilDelay(TargetIndex.A);
            yield return wait;

            yield return Toils_General.Do(ApplyTransfer);
        }

        private Pawn? GetSourceFromJob()
        {
            return job.GetTarget(TargetIndex.A).Pawn;
        }

        private Pawn? GetTargetFromJob()
        {
            return job.GetTarget(TargetIndex.B).Pawn;
        }

        private bool CanTransferNow()
        {
            Pawn? source = GetSourceFromJob();
            Pawn? target = GetTargetFromJob();
            if (source != pawn)
            {
                return false;
            }

            return MechanicalConsciousnessTransferUtility.CanTransferMechanicalConsciousness(
                source,
                target);
        }

        private void ApplyTransfer()
        {
            Pawn? source = GetSourceFromJob();
            Pawn? target = GetTargetFromJob();
            if (source != pawn
                || !MechanicalConsciousnessTransferUtility.CanTransferMechanicalConsciousness(
                    source,
                    target))
            {
                ShowFailureMessage();
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            if (!MechanicalConsciousnessTransferUtility.TryTransferMechanicalConsciousness(
                    source,
                    target))
            {
                ShowFailureMessage();
                EndJobWith(JobCondition.Incompletable);
            }
        }

        private void ShowFailureMessage()
        {
            Messages.Message(
                FailedKey.Translate(),
                pawn,
                MessageTypeDefOf.RejectInput);
        }
    }
}
