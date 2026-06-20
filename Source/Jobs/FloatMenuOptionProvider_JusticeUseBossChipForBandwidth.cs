using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    public class FloatMenuOptionProvider_JusticeUseBossChipForBandwidth : FloatMenuOptionProvider
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

            if (!JusticeBossChipBandwidthUtility.TryGetBandwidthPerChip(
                    clickedThing.def,
                    out int bandwidthPerChip))
            {
                yield break;
            }

            Pawn pawn = context.FirstSelectedPawn;
            if (CompJusticeSelfWorkMode.GetFor(pawn) == null)
            {
                yield break;
            }

            if (!JusticeBossChipBandwidthUtility.TryGetUpgradeNode(pawn, out CompMAPMechanitorNode nodeComp))
            {
                yield break;
            }

            if (nodeComp.RemainingIntrinsicBandwidth <= 0)
            {
                yield return new FloatMenuOption(
                    JusticeBossChipBandwidthUtility.AtCapLabel(),
                    null);
                yield break;
            }

            if (clickedThing.stackCount <= 1)
            {
                foreach (FloatMenuOption option in BuildUseOptions(
                             pawn,
                             clickedThing,
                             nodeComp,
                             bandwidthPerChip,
                             1))
                {
                    yield return option;
                }

                yield break;
            }

            foreach (FloatMenuOption option in BuildUseOptions(
                         pawn,
                         clickedThing,
                         nodeComp,
                         bandwidthPerChip,
                         1))
            {
                yield return option;
            }

            foreach (FloatMenuOption option in BuildUseOptions(
                         pawn,
                         clickedThing,
                         nodeComp,
                         bandwidthPerChip,
                         clickedThing.stackCount))
            {
                yield return option;
            }
        }

        private static IEnumerable<FloatMenuOption> BuildUseOptions(
            Pawn pawn,
            Thing chip,
            CompMAPMechanitorNode nodeComp,
            int bandwidthPerChip,
            int count)
        {
            string label = count <= 1
                ? JusticeBossChipBandwidthUtility.UseOneLabel(chip.LabelNoCount, bandwidthPerChip)
                : JusticeBossChipBandwidthUtility.UseAllLabel(
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

            int remaining = nodeComp.RemainingIntrinsicBandwidth;
            int theoreticalAmount = bandwidthPerChip * count;
            Action startJob = () => StartJob(pawn, chip, count);

            if (theoreticalAmount > remaining)
            {
                startJob = () =>
                {
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        JusticeBossChipBandwidthUtility.WasteConfirmText(
                            pawn.LabelShort,
                            nodeComp.MaxIntrinsicBandwidth),
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
                MAPMechanitor_JobDefOf.MAP_JusticeUseBossChipForBandwidth,
                chip);
            job.count = count;
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }
    }
}
