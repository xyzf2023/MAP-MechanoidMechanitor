using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class WardenWorkUtility
    {
        private static WorkTypeDef? cachedWardenWorkType;

        public static WorkTypeDef WardenWorkType =>
            cachedWardenWorkType ??=
                DefDatabase<WorkTypeDef>.GetNamedSilentFail("Warden");

        public static bool IsAuthorized(Pawn? pawn)
        {
            if (pawn == null || pawn.Destroyed)
            {
                return false;
            }

            if (pawn.GetComp<CompWardenWorkUser>() != null)
            {
                return true;
            }

            return GameComponent_WardenWorkRegistry.IsAuthorized(pawn);
        }

        public static void GrantAndEnsureInfrastructure(Pawn? pawn)
        {
            if (pawn == null || pawn.Destroyed)
            {
                return;
            }

            if (pawn.GetComp<CompWardenWorkUser>() == null)
            {
                GameComponent_WardenWorkRegistry.Grant(pawn);
            }

            EnsureInfrastructure(pawn);
        }

        public static void EnsureInfrastructure(Pawn? pawn)
        {
            if (pawn == null || pawn.Destroyed || !IsAuthorized(pawn))
            {
                return;
            }

            pawn.story ??= new Pawn_StoryTracker(pawn);
            pawn.story.traits ??= new TraitSet(pawn);
            pawn.skills ??= new Pawn_SkillTracker(pawn);
            pawn.interactions ??= new Pawn_InteractionsTracker(pawn);

            if (!MechWorkSettingsUtility.TryEnsureWorkSettingsInitialized(pawn))
            {
                return;
            }

            pawn.Notify_DisabledWorkTypesChanged();
            TryInitializeDefaultWardenPriority(pawn);
            pawn.Notify_DisabledWorkTypesChanged();
        }

        public static bool IsDisabledOutsideMechRaceProfile(
            Pawn pawn,
            WorkTypeDef workType,
            bool permanentOnly)
        {
            return MechWorkTypeAuthorizationUtility.IsDisabledOutsideMechRaceProfile(
                pawn,
                workType,
                permanentOnly);
        }

        private static void TryInitializeDefaultWardenPriority(Pawn pawn)
        {
            WorkTypeDef? warden = WardenWorkType;
            if (warden == null || pawn.workSettings == null)
            {
                return;
            }

            if (IsDefaultPriorityInitialized(pawn))
            {
                return;
            }

            if (pawn.workSettings.GetPriority(warden) > 0)
            {
                MarkDefaultPriorityInitialized(pawn);
                return;
            }

            if (pawn.WorkTypeIsDisabled(warden))
            {
                return;
            }

            pawn.workSettings.SetPriority(warden, GetDefaultPriority(pawn));

            if (pawn.workSettings.GetPriority(warden) > 0)
            {
                MarkDefaultPriorityInitialized(pawn);
            }
        }

        private static int GetDefaultPriority(Pawn pawn)
        {
            CompWardenWorkUser? comp = pawn.GetComp<CompWardenWorkUser>();
            return ClampDefaultPriority(comp?.DefaultPriority ?? 3);
        }

        private static int ClampDefaultPriority(int priority)
        {
            if (priority < 1)
            {
                return 1;
            }

            if (priority > 4)
            {
                return 4;
            }

            return priority;
        }

        private static bool IsDefaultPriorityInitialized(Pawn pawn)
        {
            CompWardenWorkUser? comp = pawn.GetComp<CompWardenWorkUser>();
            if (comp != null)
            {
                return comp.DefaultPriorityInitialized;
            }

            return GameComponent_WardenWorkRegistry.IsDefaultPriorityInitialized(pawn);
        }

        private static void MarkDefaultPriorityInitialized(Pawn pawn)
        {
            CompWardenWorkUser? comp = pawn.GetComp<CompWardenWorkUser>();
            if (comp != null)
            {
                comp.MarkDefaultPriorityInitialized();
                return;
            }

            GameComponent_WardenWorkRegistry.MarkDefaultPriorityInitialized(pawn);
        }
    }
}
