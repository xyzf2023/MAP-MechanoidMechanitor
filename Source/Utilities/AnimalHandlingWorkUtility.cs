using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class AnimalHandlingWorkUtility
    {
        private static WorkTypeDef? cachedHandlingWorkType;
        private static WorkGiverDef? cachedTameWorkGiver;
        private static WorkGiverDef? cachedTrainWorkGiver;

        public static WorkTypeDef HandlingWorkType =>
            cachedHandlingWorkType ??=
                DefDatabase<WorkTypeDef>.GetNamedSilentFail("Handling");

        public static bool IsAuthorized(Pawn? pawn)
        {
            return GameComponent_AnimalHandlingWorkRegistry.IsAuthorized(pawn);
        }

        public static AnimalHandlingWorkScope GetScope(Pawn? pawn)
        {
            return GameComponent_AnimalHandlingWorkRegistry.GetScope(pawn);
        }

        public static bool AllowsWorkGiver(Pawn? pawn, WorkGiverDef? workGiverDef)
        {
            if (pawn == null || workGiverDef == null || !IsAuthorized(pawn))
            {
                return false;
            }

            if (GetScope(pawn) == AnimalHandlingWorkScope.FullHandling)
            {
                return true;
            }

            WorkGiverDef? tame = GetTameWorkGiver();
            WorkGiverDef? train = GetTrainWorkGiver();
            return workGiverDef == tame || workGiverDef == train;
        }

        public static void GrantAndEnsureInfrastructure(
            Pawn? pawn,
            AnimalHandlingWorkScope scope,
            int defaultPriority)
        {
            if (pawn == null || pawn.Destroyed)
            {
                return;
            }

            if (pawn.GetComp<CompAnimalHandlingWorkUser>() == null)
            {
                GameComponent_AnimalHandlingWorkRegistry.Grant(pawn, scope, defaultPriority);
            }

            EnsureInfrastructure(pawn);
        }

        public static void EnsureInfrastructure(Pawn? pawn)
        {
            if (pawn == null || pawn.Destroyed || !IsAuthorized(pawn))
            {
                return;
            }

            EnsureTrackerContainers(pawn);

            MechanoidBackstoryUtility.EnsureGenericBackstories(pawn);

            if (!MechWorkSettingsUtility.TryEnsureWorkSettingsInitialized(pawn))
            {
                return;
            }

            pawn.Notify_DisabledWorkTypesChanged();
            TryInitializeDefaultHandlingPriority(pawn);
            MechWorkSettingsUtility.RestrictToMechEnabledWorkTypes(pawn);
            pawn.Notify_DisabledWorkTypesChanged();
        }

        internal static int ClampDefaultPriority(int priority)
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

        private static void EnsureTrackerContainers(Pawn pawn)
        {
            pawn.story ??= new Pawn_StoryTracker(pawn);
            pawn.story.traits ??= new TraitSet(pawn);
            pawn.skills ??= new Pawn_SkillTracker(pawn);
            pawn.interactions ??= new Pawn_InteractionsTracker(pawn);
            pawn.relations ??= new Pawn_RelationsTracker(pawn);
            pawn.workSettings ??= new Pawn_WorkSettings(pawn);
        }

        private static void TryInitializeDefaultHandlingPriority(Pawn pawn)
        {
            WorkTypeDef? handling = HandlingWorkType;
            if (handling == null || pawn.workSettings == null)
            {
                return;
            }

            if (GameComponent_AnimalHandlingWorkRegistry.IsDefaultPriorityInitialized(pawn))
            {
                return;
            }

            if (pawn.workSettings.GetPriority(handling) > 0)
            {
                GameComponent_AnimalHandlingWorkRegistry.MarkDefaultPriorityInitialized(pawn);
                return;
            }

            if (pawn.WorkTypeIsDisabled(handling))
            {
                return;
            }

            pawn.workSettings.SetPriority(
                handling,
                GameComponent_AnimalHandlingWorkRegistry.GetDefaultPriority(pawn));

            if (pawn.workSettings.GetPriority(handling) > 0)
            {
                GameComponent_AnimalHandlingWorkRegistry.MarkDefaultPriorityInitialized(pawn);
            }
        }

        private static WorkGiverDef? GetTameWorkGiver()
        {
            return cachedTameWorkGiver ??=
                DefDatabase<WorkGiverDef>.GetNamedSilentFail("Tame");
        }

        private static WorkGiverDef? GetTrainWorkGiver()
        {
            return cachedTrainWorkGiver ??=
                DefDatabase<WorkGiverDef>.GetNamedSilentFail("Train");
        }
    }
}
