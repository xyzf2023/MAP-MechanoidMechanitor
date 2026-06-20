using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    public class JobDriver_JusticeUseBossChipForBandwidth : JobDriver
    {
        private const int UseDurationTicks = 180;

        private Thing Chip => TargetThingA;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            int reserveCount = job.count > 0 ? job.count : 1;
            return pawn.Reserve(
                job.GetTarget(TargetIndex.A),
                job,
                1,
                reserveCount,
                null,
                errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddFailCondition(() =>
                Chip == null
                || Chip.Destroyed
                || !Chip.Spawned
                || Chip.IsForbidden(pawn));

            AddFailCondition(() => !JusticeBossChipBandwidthUtility.TryGetUpgradeNode(pawn, out _));

            AddFailCondition(() =>
                Chip?.def == null
                || !JusticeBossChipBandwidthUtility.TryGetBandwidthPerChip(Chip.def, out _));

            AddFailCondition(() =>
            {
                if (!JusticeBossChipBandwidthUtility.TryGetUpgradeNode(pawn, out CompMAPMechanitorNode nodeComp))
                {
                    return true;
                }

                return nodeComp.RemainingIntrinsicBandwidth <= 0;
            });

            yield return Toils_Reserve.Reserve(
                TargetIndex.A,
                stackCount: job.count > 0 ? job.count : 1);

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            Toil wait = Toils_General.Wait(UseDurationTicks, TargetIndex.A);
            wait.WithProgressBarToilDelay(TargetIndex.A);
            yield return wait;

            yield return Toils_General.Do(ApplyBandwidthUpgrade);
        }

        private void ApplyBandwidthUpgrade()
        {
            Thing chip = Chip;
            if (chip == null || chip.Destroyed)
            {
                return;
            }

            if (!JusticeBossChipBandwidthUtility.TryGetBandwidthPerChip(chip.def, out int bandwidthPerChip))
            {
                return;
            }

            if (!JusticeBossChipBandwidthUtility.TryGetUpgradeNode(pawn, out CompMAPMechanitorNode nodeComp))
            {
                return;
            }

            if (nodeComp.RemainingIntrinsicBandwidth <= 0)
            {
                return;
            }

            int useCount = job.count > 0
                ? Math.Min(job.count, chip.stackCount)
                : 1;
            if (useCount <= 0)
            {
                return;
            }

            int theoreticalAmount = bandwidthPerChip * useCount;
            int actualAdded = nodeComp.AddChipBandwidth(theoreticalAmount);
            if (actualAdded <= 0)
            {
                return;
            }

            if (useCount >= chip.stackCount)
            {
                chip.Destroy(DestroyMode.Vanish);
            }
            else
            {
                chip.SplitOff(useCount).Destroy(DestroyMode.Vanish);
            }

            Messages.Message(
                JusticeBossChipBandwidthUtility.SuccessMessage(pawn.LabelShort, actualAdded),
                pawn,
                MessageTypeDefOf.PositiveEvent);
        }
    }
}
