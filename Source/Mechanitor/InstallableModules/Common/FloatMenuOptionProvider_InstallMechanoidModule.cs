using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 机械族模块通用右键安装入口。
    /// 只处理拥有 CompInstallableMechanoidModule 的物品，不硬编码任何模块的 defName。
    /// </summary>
    public class FloatMenuOptionProvider_InstallMechanoidModule : FloatMenuOptionProvider
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

            CompInstallableMechanoidModule? comp =
                clickedThing.TryGetComp<CompInstallableMechanoidModule>();
            if (comp == null)
            {
                yield break;
            }

            Pawn pawn = context.FirstSelectedPawn;
            if (!comp.IsValidInstaller(pawn))
            {
                yield break;
            }

            CompMechanoidModuleInstallEffect? effect = comp.TryGetInstallEffect();
            if (effect == null)
            {
                yield break;
            }

            string baseLabel = comp.InstallOptionLabel;

            if (!effect.ShouldShowOption(pawn, out string? disabledReason))
            {
                yield break;
            }

            if (!disabledReason.NullOrEmpty())
            {
                yield return new FloatMenuOption(baseLabel + disabledReason, null);
                yield break;
            }

            if (!pawn.CanReach(clickedThing, PathEndMode.Touch, Danger.Deadly))
            {
                yield return new FloatMenuOption(
                    baseLabel + ": " + "NoPath".Translate().CapitalizeFirst(),
                    null);
                yield break;
            }

            if (!pawn.CanReserve(clickedThing))
            {
                yield return new FloatMenuOption(
                    baseLabel + ": " + "Reserved".Translate().CapitalizeFirst(),
                    null);
                yield break;
            }

            Action startJob = () => StartJob(pawn, clickedThing);
            yield return FloatMenuUtility.DecoratePrioritizedTask(
                new FloatMenuOption(baseLabel, startJob),
                pawn,
                clickedThing,
                reservedText: "Reserved");
        }

        private static void StartJob(Pawn pawn, Thing module)
        {
            module.SetForbidden(false, false);
            Job job = JobMaker.MakeJob(
                MAPMechanitor_JobDefOf.MAP_InstallMechanoidModule,
                module);
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }
    }
}
