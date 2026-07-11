using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    public class JobDriver_MechanoidMechanitorUseBossChipForBandwidth : JobDriver
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

            AddFailCondition(() =>
                !MechanoidMechanitorBossChipBandwidthUtility.CanUpgradeBandwidth(pawn));

            AddFailCondition(() =>
                Chip?.def == null
                || !MechanoidMechanitorBossChipBandwidthUtility.TryGetBandwidthPerChip(Chip.def, out _));

            AddFailCondition(() =>
                MechanoidMechanitorBossChipBandwidthUtility.GetRemainingIntrinsicBandwidth(pawn) <= 0);

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
            if (pawn == null
                || pawn.Destroyed
                || !pawn.Spawned
                || pawn.Map == null)
            {
                return;
            }

            Thing chip = Chip;
            if (chip == null
                || chip.Destroyed
                || !chip.Spawned
                || chip.Map != pawn.Map
                || chip.IsForbidden(pawn))
            {
                return;
            }

            if (!MechanoidMechanitorBossChipBandwidthUtility.TryGetBandwidthPerChip(
                    chip.def,
                    out int bandwidthPerChip))
            {
                return;
            }

            if (!MechanoidMechanitorBossChipBandwidthUtility.CanUpgradeBandwidth(pawn)
                || MechanoidMechanitorBossChipBandwidthUtility.GetRemainingIntrinsicBandwidth(pawn) <= 0)
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
            int actualAdded = MechanoidMechanitorBossChipBandwidthUtility.AddChipBandwidth(
                pawn,
                theoreticalAmount);
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
                MechanoidMechanitorBossChipBandwidthUtility.SuccessMessage(pawn.LabelShort, actualAdded),
                pawn,
                MessageTypeDefOf.PositiveEvent);
        }
    }
}
