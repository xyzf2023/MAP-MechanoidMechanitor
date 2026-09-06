using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    public sealed class FloatMenuOptionProvider_MindMappingCore : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;
        protected override bool MechanoidCanDo => true;

        protected override bool AppliesInt(FloatMenuContext context)
            => context.FirstSelectedPawn != null;

        public override IEnumerable<FloatMenuOption> GetOptionsFor(
            Thing clickedThing,
            FloatMenuContext context)
        {
            CompMindMappingCore? core = clickedThing?.TryGetComp<CompMindMappingCore>();
            Pawn pawn = context.FirstSelectedPawn;
            if (core == null || pawn == null || !pawn.RaceProps.IsMechanoid)
            {
                yield break;
            }

            if (core.IsBlank)
            {
                if (!MindMappingUtility.HasMappedPersonality(pawn))
                {
                    yield break;
                }

                yield return MakeOption(
                    pawn,
                    clickedThing,
                    "MAP_MindMapping.Command.Export".Translate(),
                    MAPMechanitor_JobDefOf.MAP_ExportMindData);

                string copyLabel = "MAP_MindMapping.Command.Copy".Translate();
                if (MindMappingUtility.HasCopyCooldown(pawn))
                {
                    yield return new FloatMenuOption(
                        copyLabel + ": " + "MAP_MindMapping.Reason.CopyCooldown".Translate(),
                        null);
                }
                else
                {
                    yield return MakeOption(
                        pawn,
                        clickedThing,
                        copyLabel,
                        MAPMechanitor_JobDefOf.MAP_CopyMindData);
                }

                yield break;
            }

            if (MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
            {
                if (!MindMappingUtility.HasMappedPersonality(pawn))
                {
                    yield return MakeOption(
                        pawn,
                        clickedThing,
                        "MAP_MindMapping.Command.Import".Translate(),
                        MAPMechanitor_JobDefOf.MAP_ImportMindData);
                }

                yield break;
            }

            if (MechanoidMechanitorRoleUtility.CanBecomeAcquiredMechanoidMechanitor(pawn))
            {
                yield return MakeOption(
                    pawn,
                    clickedThing,
                    "MAP_MindMapping.Command.Ascend".Translate(),
                    MAPMechanitor_JobDefOf.MAP_AscendWithMindData);
            }
        }

        private static FloatMenuOption MakeOption(
            Pawn pawn,
            Thing core,
            string label,
            JobDef jobDef)
        {
            if (!pawn.CanReach(core, PathEndMode.Touch, Danger.Deadly))
            {
                return new FloatMenuOption(
                    label + ": " + "NoPath".Translate().CapitalizeFirst(),
                    null);
            }

            if (!pawn.CanReserve(core))
            {
                return new FloatMenuOption(
                    label + ": " + "Reserved".Translate().CapitalizeFirst(),
                    null);
            }

            Action action = () =>
            {
                core.SetForbidden(false, false);
                pawn.jobs.TryTakeOrderedJob(
                    JobMaker.MakeJob(jobDef, core),
                    JobTag.Misc);
            };
            return FloatMenuUtility.DecoratePrioritizedTask(
                new FloatMenuOption(label, action),
                pawn,
                core,
                reservedText: "Reserved");
        }
    }

    public sealed class JobDriver_UseMindMappingCore : JobDriver
    {
        private const int WorkTicks = 600;

        private bool exportConfirmed;

        [Unsaved(false)]
        private bool confirmationWindowOpen;

        private Thing Core => TargetThingA;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(
                job.GetTarget(TargetIndex.A),
                job,
                1,
                -1,
                null,
                errorOnFailed);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref exportConfirmed, "exportConfirmed", false);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddFailCondition(() => !MindMappingUtility.CanPerform(pawn, Core, job.def));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            if (job.def == MAPMechanitor_JobDefOf.MAP_ExportMindData)
            {
                Toil confirmation = new Toil
                {
                    defaultCompleteMode = ToilCompleteMode.Never
                };
                confirmation.tickAction = () => TickExportConfirmation(confirmation.actor);
                yield return confirmation;
            }

            Toil wait = Toils_General.Wait(WorkTicks, TargetIndex.A);
            wait.WithProgressBarToilDelay(TargetIndex.A);
            yield return wait;
            yield return Toils_General.Do(ApplyOperation);
        }

        private void TickExportConfirmation(Pawn actor)
        {
            if (exportConfirmed)
            {
                actor.jobs.curDriver.ReadyForNextToil();
                return;
            }

            if (confirmationWindowOpen)
            {
                return;
            }

            confirmationWindowOpen = true;
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "MAP_MindMapping.Export.Confirm".Translate(),
                confirmedAct: () =>
                {
                    confirmationWindowOpen = false;
                    exportConfirmed = true;
                },
                cancelAct: () =>
                {
                    confirmationWindowOpen = false;
                    if (actor.jobs?.curDriver == this)
                    {
                        actor.jobs.EndCurrentJob(JobCondition.InterruptForced);
                    }
                },
                destructive: true));
        }

        private void ApplyOperation()
        {
            if (!MindMappingUtility.CanPerform(pawn, Core, job.def))
            {
                Messages.Message(
                    "MAP_MindMapping.Message.Invalidated".Translate(),
                    pawn,
                    MessageTypeDefOf.RejectInput);
                return;
            }

            CompMindMappingCore core = Core.TryGetComp<CompMindMappingCore>();
            bool success;
            if (job.def == MAPMechanitor_JobDefOf.MAP_AscendWithMindData)
            {
                success = MindMappingUtility.TryAscend(pawn, core);
                if (success)
                {
                    Core.Destroy(DestroyMode.Vanish);
                }
            }
            else if (job.def == MAPMechanitor_JobDefOf.MAP_ImportMindData)
            {
                success = MindMappingUtility.TryImport(pawn, core);
            }
            else if (job.def == MAPMechanitor_JobDefOf.MAP_ExportMindData)
            {
                success = exportConfirmed && MindMappingUtility.TryExport(pawn, core);
            }
            else
            {
                success = MindMappingUtility.TryCopy(pawn, core);
            }

            Messages.Message(
                success
                    ? "MAP_MindMapping.Message.Success".Translate()
                    : "MAP_MindMapping.Message.Invalidated".Translate(),
                pawn,
                success ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.RejectInput);
        }
    }

    internal static class MindMappingUtility
    {
        public static bool HasMappedPersonality(Pawn? pawn)
        {
            return GameComponent_MechanoidMechanitorRegistry.TryGetMechanitorRecord(
                    pawn,
                    out MechanoidMechanitorRecord? record)
                && record?.HasMappedPersonality == true;
        }

        public static bool HasCopyCooldown(Pawn? pawn)
        {
            return pawn?.health?.hediffSet != null
                && pawn.health.hediffSet.HasHediff(MAPMechanitor_HediffDefOf.MAP_MindMappingLoad);
        }

        public static bool CanPerform(Pawn? pawn, Thing? coreThing, JobDef? operation)
        {
            if (pawn == null
                || coreThing == null
                || coreThing.Destroyed
                || !coreThing.Spawned
                || !pawn.RaceProps.IsMechanoid)
            {
                return false;
            }

            CompMindMappingCore? core = coreThing.TryGetComp<CompMindMappingCore>();
            if (core == null)
            {
                return false;
            }

            if (operation == MAPMechanitor_JobDefOf.MAP_AscendWithMindData)
            {
                return !core.IsBlank
                    && MechanoidMechanitorRoleUtility.CanBecomeAcquiredMechanoidMechanitor(pawn);
            }

            if (operation == MAPMechanitor_JobDefOf.MAP_ImportMindData)
            {
                return !core.IsBlank
                    && MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn)
                    && !HasMappedPersonality(pawn);
            }

            if (operation == MAPMechanitor_JobDefOf.MAP_ExportMindData)
            {
                return core.IsBlank && HasMappedPersonality(pawn);
            }

            if (operation == MAPMechanitor_JobDefOf.MAP_CopyMindData)
            {
                return core.IsBlank && HasMappedPersonality(pawn) && !HasCopyCooldown(pawn);
            }

            return false;
        }

        public static bool TryAscend(Pawn pawn, CompMindMappingCore core)
        {
            MindMappingData? data = core.Data;
            if (data == null
                || !MechanoidMechanitorRoleUtility.PromoteToAcquiredMechanoidMechanitor(pawn))
            {
                return false;
            }

            data.ApplyTo(pawn);
            return TrySetMappedPersonality(pawn, true);
        }

        public static bool TryImport(Pawn pawn, CompMindMappingCore core)
        {
            MindMappingData? data = core.Data;
            if (data == null
                || !GameComponent_MechanoidMechanitorRegistry.TryGetMechanitorRecord(pawn, out _))
            {
                return false;
            }

            data.ApplyTo(pawn);
            if (!TrySetMappedPersonality(pawn, true))
            {
                return false;
            }

            core.Clear();
            return true;
        }

        public static bool TryExport(Pawn pawn, CompMindMappingCore core)
        {
            if (!core.IsBlank
                || !GameComponent_MechanoidMechanitorRegistry.TryGetMechanitorRecord(pawn, out _))
            {
                return false;
            }

            MindMappingData data = MindMappingData.Capture(pawn);
            MindMappingData.ResetToAcquiredBaseline(pawn);
            core.Store(data);
            return TrySetMappedPersonality(pawn, false);
        }

        public static bool TryCopy(Pawn pawn, CompMindMappingCore core)
        {
            if (!core.IsBlank || HasCopyCooldown(pawn))
            {
                return false;
            }

            core.Store(MindMappingData.Capture(pawn));
            pawn.health.AddHediff(MAPMechanitor_HediffDefOf.MAP_MindMappingLoad);
            return true;
        }

        private static bool TrySetMappedPersonality(Pawn pawn, bool value)
        {
            if (!GameComponent_MechanoidMechanitorRegistry.TryGetMechanitorRecord(
                    pawn,
                    out MechanoidMechanitorRecord? record)
                || record == null)
            {
                return false;
            }

            record.HasMappedPersonality = value;
            return true;
        }
    }
}
