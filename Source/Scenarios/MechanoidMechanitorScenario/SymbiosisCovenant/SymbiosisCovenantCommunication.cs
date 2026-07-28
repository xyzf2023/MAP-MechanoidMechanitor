using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public static class SymbiosisCovenantCommunicationUtility
    {
        private const string AccessJobDefName = "MAP_AccessSymbiosisCovenant";
        public const string DeclarationJobDefName = "MAP_BroadcastSymbiosisDeclaration";
        public const int DeclarationDurationTicks = 900;

        public static string AccessLabel =>
            "MAP_MechanoidMechanitor.Symbiosis.Access".Translate();

        public static string DeclarationLabel =>
            "MAP_MechanoidMechanitor.Symbiosis.AccessDeclaration".Translate();

        public static bool IsValidContactPawn(Pawn? pawn)
        {
            return pawn != null
                && !pawn.Dead
                && !pawn.Destroyed
                && pawn.Faction != null
                && pawn.Faction.IsPlayerSafe();
        }

        public static bool CanAccessCovenant()
        {
            if (!GameComponent_SymbiosisCovenantState.IsActive)
            {
                return false;
            }

            GameComponent_SymbiosisCovenantState? state =
                GameComponent_SymbiosisCovenantState.CurrentComponent;
            if (state == null)
            {
                return false;
            }

            state.SynchronizeNow();
            return state.Initialized && state.GetRecordsSorted().Count > 0;
        }

        public static bool CanBroadcastDeclaration()
        {
            if (!GameComponent_SymbiosisCovenantState.IsActive)
            {
                return false;
            }

            GameComponent_SymbiosisCovenantState? state =
                GameComponent_SymbiosisCovenantState.CurrentComponent;
            if (state == null)
            {
                return false;
            }

            state.SynchronizeNow();
            return state.Initialized && !state.PublicDeclarationBroadcast;
        }

        public static void ShowDeclarationConfirmation(
            Pawn pawn,
            Building_CommsConsole console)
        {
            if (!IsValidContactPawn(pawn) || console == null || !console.Spawned)
            {
                return;
            }

            if (!CanBroadcastDeclaration())
            {
                return;
            }

            Find.WindowStack.Add(
                new Dialog_MessageBox(
                    "MAP_MechanoidMechanitor.Symbiosis.Declaration.Confirm.Text"
                        .Translate(),
                    "MAP_MechanoidMechanitor.Symbiosis.Declaration.Confirm.Continue"
                        .Translate(),
                    () => TryOrderDeclarationJob(pawn, console),
                    "MAP_MechanoidMechanitor.Symbiosis.Declaration.Confirm.Cancel"
                        .Translate(),
                    null,
                    "MAP_MechanoidMechanitor.Symbiosis.Declaration.Confirm.Title"
                        .Translate()));
        }

        public static void TryOrderDeclarationJob(
            Pawn? pawn,
            Building_CommsConsole? console)
        {
            if (!IsValidContactPawn(pawn)
                || console == null
                || !console.Spawned
                || pawn!.Map == null
                || console.Map == null
                || pawn.Map != console.Map
                || !console.CanUseCommsNow
                || !CanBroadcastDeclaration())
            {
                return;
            }

            JobDef? jobDef = DefDatabase<JobDef>.GetNamedSilentFail(DeclarationJobDefName);
            if (jobDef == null)
            {
                Log.Error(
                    "[MAP-机械族机械师] 无法广播脱离声明：缺少 "
                    + DeclarationJobDefName
                    + "。");
                return;
            }

            Job job = JobMaker.MakeJob(jobDef, console);
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            PlayerKnowledgeDatabase.KnowledgeDemonstrated(
                ConceptDefOf.OpeningComms,
                KnowledgeAmount.Total);
        }

        public static void TryOrderAccessJob(
            Pawn? pawn,
            Building_CommsConsole? console)
        {
            if (!IsValidContactPawn(pawn)
                || console == null
                || !console.Spawned
                || pawn!.Map == null
                || console.Map == null
                || pawn.Map != console.Map
                || !console.CanUseCommsNow
                || !CanAccessCovenant())
            {
                return;
            }

            JobDef? jobDef = DefDatabase<JobDef>.GetNamedSilentFail(AccessJobDefName);
            if (jobDef == null)
            {
                Log.Error(
                    "[MAP-机械族机械师] 无法接入共生盟约：缺少 "
                    + AccessJobDefName
                    + "。");
                return;
            }

            Job job = JobMaker.MakeJob(jobDef, console);
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            PlayerKnowledgeDatabase.KnowledgeDemonstrated(
                ConceptDefOf.OpeningComms,
                KnowledgeAmount.Total);
        }

        public static void TryOpenDialog(Pawn? pawn)
        {
            if (!IsValidContactPawn(pawn) || !CanAccessCovenant())
            {
                return;
            }

            Find.WindowStack.Add(new Dialog_SymbiosisCovenant());
        }
    }

    public sealed class FloatMenuOptionProvider_AccessSymbiosisCovenant
        : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;

        protected override bool Undrafted => true;

        protected override bool Multiselect => false;

        protected override bool MechanoidCanDo => true;

        protected override bool AppliesInt(FloatMenuContext context)
        {
            return SymbiosisCovenantCommunicationUtility.IsValidContactPawn(
                context.FirstSelectedPawn);
        }

        public override IEnumerable<FloatMenuOption> GetOptionsFor(
            Thing clickedThing,
            FloatMenuContext context)
        {
            if (clickedThing is not Building_CommsConsole console)
            {
                yield break;
            }

            Pawn? selectedPawn = context.FirstSelectedPawn;
            if (!SymbiosisCovenantCommunicationUtility.IsValidContactPawn(selectedPawn)
                || !SymbiosisCovenantCommunicationUtility.CanAccessCovenant())
            {
                yield break;
            }

            Pawn pawn = selectedPawn!;
            if (!pawn.CanReach(console, PathEndMode.InteractionCell, Danger.Some))
            {
                yield return new FloatMenuOption("CannotUseNoPath".Translate(), null);
                yield break;
            }

            if (console.Spawned
                && console.Map != null
                && console.Map.gameConditionManager.ElectricityDisabled(console.Map))
            {
                yield return new FloatMenuOption("CannotUseSolarFlare".Translate(), null);
                yield break;
            }

            if (!console.CanUseCommsNow)
            {
                yield return new FloatMenuOption("CannotUseNoPower".Translate(), null);
                yield break;
            }

            FloatMenuOption option = new FloatMenuOption(
                SymbiosisCovenantCommunicationUtility.AccessLabel,
                () => SymbiosisCovenantCommunicationUtility.TryOrderAccessJob(
                    pawn,
                    console));
            yield return FloatMenuUtility.DecoratePrioritizedTask(option, pawn, console);
        }
    }

    public sealed class FloatMenuOptionProvider_BroadcastSymbiosisDeclaration
        : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;

        protected override bool Undrafted => true;

        protected override bool Multiselect => false;

        protected override bool MechanoidCanDo => true;

        protected override bool AppliesInt(FloatMenuContext context)
        {
            return SymbiosisCovenantCommunicationUtility.IsValidContactPawn(
                context.FirstSelectedPawn);
        }

        public override IEnumerable<FloatMenuOption> GetOptionsFor(
            Thing clickedThing,
            FloatMenuContext context)
        {
            if (clickedThing is not Building_CommsConsole console)
            {
                yield break;
            }

            Pawn? selectedPawn = context.FirstSelectedPawn;
            if (!SymbiosisCovenantCommunicationUtility.IsValidContactPawn(selectedPawn)
                || !SymbiosisCovenantCommunicationUtility.CanBroadcastDeclaration())
            {
                yield break;
            }

            Pawn pawn = selectedPawn!;
            if (!pawn.CanReach(console, PathEndMode.InteractionCell, Danger.Some))
            {
                yield return new FloatMenuOption("CannotUseNoPath".Translate(), null);
                yield break;
            }

            if (console.Spawned
                && console.Map != null
                && console.Map.gameConditionManager.ElectricityDisabled(console.Map))
            {
                yield return new FloatMenuOption("CannotUseSolarFlare".Translate(), null);
                yield break;
            }

            if (!console.CanUseCommsNow)
            {
                yield return new FloatMenuOption("CannotUseNoPower".Translate(), null);
                yield break;
            }

            FloatMenuOption option = new FloatMenuOption(
                SymbiosisCovenantCommunicationUtility.DeclarationLabel,
                () => SymbiosisCovenantCommunicationUtility.ShowDeclarationConfirmation(
                    pawn,
                    console));
            yield return FloatMenuUtility.DecoratePrioritizedTask(option, pawn, console);
        }
    }

    public sealed class JobDriver_AccessSymbiosisCovenant : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.InteractionCell)
                .FailOn(
                    (Toil toil) =>
                    {
                        Thing? thing =
                            toil.actor.jobs.curJob.GetTarget(TargetIndex.A).Thing;
                        return thing is not Building_CommsConsole console
                            || !console.CanUseCommsNow;
                    });

            Toil openComms = ToilMaker.MakeToil("OpenSymbiosisCovenant");
            openComms.initAction = delegate
            {
                Pawn actor = openComms.actor;
                Thing? thing = actor.jobs.curJob.GetTarget(TargetIndex.A).Thing;
                if (thing is Building_CommsConsole console
                    && console.Spawned
                    && console.CanUseCommsNow)
                {
                    SymbiosisCovenantCommunicationUtility.TryOpenDialog(actor);
                }
            };
            yield return openComms;
        }
    }

    public sealed class CompProperties_SymbiosisCovenantContactUnlockedLetter
        : CompProperties
    {
        public CompProperties_SymbiosisCovenantContactUnlockedLetter()
        {
            compClass = typeof(CompSymbiosisCovenantContactUnlockedLetter);
        }
    }

    public sealed class CompSymbiosisCovenantContactUnlockedLetter : ThingComp
    {
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (respawningAfterLoad
                || parent.Faction != Faction.OfPlayerSilentFail
                || !SymbiosisCovenantCommunicationUtility.CanAccessCovenant())
            {
                return;
            }

            GameComponent_SymbiosisCovenantState? state =
                GameComponent_SymbiosisCovenantState.CurrentComponent;
            if (state == null || !state.TryMarkContactUnlockedLetterSent())
            {
                return;
            }

            Find.LetterStack.ReceiveLetter(
                "MAP_MechanoidMechanitor.Symbiosis.ContactUnlocked.Label".Translate(),
                "MAP_MechanoidMechanitor.Symbiosis.ContactUnlocked.Text".Translate(),
                LetterDefOf.NeutralEvent,
                parent);
        }
    }
}
