using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    public class FloatMenuOptionProvider_MechanoidMechanitorUseBossChipForBandwidth : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;
        protected override bool MechanoidCanDo => true;

        protected override bool AppliesInt(FloatMenuContext context)
        {
            return context.FirstSelectedPawn != null;
        }

        public override IEnumerable<FloatMenuOption> GetOptionsFor(
            Thing clickedThing,
            FloatMenuContext context)
        {
            if (clickedThing == null)
            {
                yield break;
            }

            if (!MechanoidMechanitorBossChipBandwidthUtility.TryGetBandwidthPerChip(
                    clickedThing.def,
                    out int bandwidthPerChip))
            {
                yield break;
            }

            Pawn pawn = context.FirstSelectedPawn;
            if (!MechanoidMechanitorBossChipBandwidthUtility.CanUpgradeBandwidth(pawn))
            {
                yield break;
            }

            if (MechanoidMechanitorBossChipBandwidthUtility.GetRemainingIntrinsicBandwidth(pawn) <= 0)
            {
                yield return new FloatMenuOption(
                    MechanoidMechanitorBossChipBandwidthUtility.AtCapLabel(),
                    null);
                yield break;
            }

            foreach (FloatMenuOption option in BuildUseOptions(
                         pawn,
                         clickedThing,
                         bandwidthPerChip,
                         1))
            {
                yield return option;
            }

            if (clickedThing.stackCount <= 1)
            {
                yield break;
            }

            foreach (FloatMenuOption option in BuildUseOptions(
                         pawn,
                         clickedThing,
                         bandwidthPerChip,
                         clickedThing.stackCount))
            {
                yield return option;
            }
        }

        private static IEnumerable<FloatMenuOption> BuildUseOptions(
            Pawn pawn,
            Thing chip,
            int bandwidthPerChip,
            int count)
        {
            string label = count <= 1
                ? MechanoidMechanitorBossChipBandwidthUtility.UseOneLabel(chip.LabelNoCount, bandwidthPerChip)
                : MechanoidMechanitorBossChipBandwidthUtility.UseAllLabel(
                    chip.LabelNoCount,
                    bandwidthPerChip * count);

            if (!pawn.CanReach(chip, PathEndMode.Touch, Danger.Deadly))
            {
                yield return new FloatMenuOption(
                    label + ": " + "NoPath".Translate().CapitalizeFirst(),
                    null);
                yield break;
            }

            if (!pawn.CanReserve(chip, 1, count))
            {
                yield return new FloatMenuOption(
                    label + ": " + "Reserved".Translate().CapitalizeFirst(),
                    null);
                yield break;
            }

            int remaining = MechanoidMechanitorBossChipBandwidthUtility
                .GetRemainingIntrinsicBandwidth(pawn);
            int theoreticalAmount = bandwidthPerChip * count;
            Action startJob = () => StartJob(pawn, chip, count);

            if (theoreticalAmount > remaining)
            {
                startJob = () =>
                {
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        MechanoidMechanitorBossChipBandwidthUtility.WasteConfirmText(
                            pawn.LabelShort,
                            MechanoidMechanitorBossChipBandwidthUtility.GetMaxIntrinsicBandwidth(pawn)),
                        () => StartJob(pawn, chip, count)));
                };
            }

            yield return FloatMenuUtility.DecoratePrioritizedTask(
                new FloatMenuOption(label, startJob),
                pawn,
                chip,
                reservedText: "Reserved");
        }

        private static void StartJob(Pawn pawn, Thing chip, int count)
        {
            chip.SetForbidden(false, false);
            Job job = JobMaker.MakeJob(
                MAPMechanitor_JobDefOf.MAP_MechanoidMechanitorUseBossChipForBandwidth,
                chip);
            job.count = count;
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }
    }
}
