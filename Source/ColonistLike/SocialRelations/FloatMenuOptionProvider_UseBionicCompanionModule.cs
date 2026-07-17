using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    public class FloatMenuOptionProvider_UseBionicCompanionModule : FloatMenuOptionProvider
    {
        private const string BionicCompanionModuleDefName = "MAP_BionicCompanionModule";
        private const string InstallLabel = "安装仿生伴侣模块";
        private const string AlreadyHasStateSuffix = "：已经拥有仿生伴侣功能";

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
            if (clickedThing?.def?.defName != BionicCompanionModuleDefName)
            {
                yield break;
            }

            Pawn pawn = context.FirstSelectedPawn;
            if (!IsEligibleInstaller(pawn))
            {
                yield break;
            }

            if (SyntheticCompanionStateUtility.HasState(pawn))
            {
                yield return new FloatMenuOption(
                    InstallLabel + AlreadyHasStateSuffix,
                    null);
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
                MAPMechanitor_JobDefOf.MAP_UseBionicCompanionModule,
                module);
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }
    }
}
