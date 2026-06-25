using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    public class FloatMenuOptionProvider_UseBandwidthLink : FloatMenuOptionProvider
    {
        private const string BandwidthLinkDefName = "MAP_MechBandwidthLink";
        private const string InstallLabel = "安装带宽协调模块";
        private const string AlreadyMechanitorSuffix = "：已经是机械族机械师";

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
            if (clickedThing?.def?.defName != BandwidthLinkDefName)
            {
                yield break;
            }

            Pawn pawn = context.FirstSelectedPawn;
            if (!IsEligibleInstaller(pawn))
            {
                yield break;
            }

            if (MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
            {
                yield return new FloatMenuOption(
                    InstallLabel + AlreadyMechanitorSuffix,
                    null);
                yield break;
            }

            if (!MechanoidMechanitorRoleUtility.CanBecomeAcquiredMechanoidMechanitor(pawn))
            {
                yield break;
            }

            if (!pawn.CanReach(clickedThing, PathEndMode.Touch, Danger.Deadly))
            {
                yield return new FloatMenuOption(
                    InstallLabel + ": " + "NoPath".Translate().CapitalizeFirst(),
                    null);
                yield break;
            }

            if (!pawn.CanReserve(clickedThing))
            {
                yield return new FloatMenuOption(
                    InstallLabel + ": " + "Reserved".Translate().CapitalizeFirst(),
                    null);
                yield break;
            }

            Action startJob = () => StartJob(pawn, clickedThing);
            yield return FloatMenuUtility.DecoratePrioritizedTask(
                new FloatMenuOption(InstallLabel, startJob),
                pawn,
                clickedThing,
                reservedText: "Reserved");
        }

        private static bool IsEligibleInstaller(Pawn? pawn)
        {
            return ModsConfig.BiotechActive
                && pawn != null
                && !pawn.Dead
                && !pawn.Destroyed
                && pawn.RaceProps.IsMechanoid
                && pawn.Faction != null
                && pawn.Faction.IsPlayerSafe()
                && pawn.health?.hediffSet != null;
        }

        private static void StartJob(Pawn pawn, Thing module)
        {
            module.SetForbidden(false, false);
            Job job = JobMaker.MakeJob(
                MAPMechanitor_JobDefOf.MAP_UseBandwidthLink,
                module);
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }
    }
}
