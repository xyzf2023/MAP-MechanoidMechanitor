using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class GameComponent_MechanoidMechanitorRegistry : GameComponent
    {
        private Pawn? mechanicalConsciousnessHost;
        private Pawn? scenarioProtagonist;

        public Pawn? MechanicalConsciousnessHost => mechanicalConsciousnessHost;
        public Pawn? ScenarioProtagonist => scenarioProtagonist;

        private static GameComponent_MechanoidMechanitorRegistry? CurrentRegistry
        {
            get
            {
                if (Current.Game == null)
                {
                    return null;
                }

                return Current.Game.GetComponent<GameComponent_MechanoidMechanitorRegistry>();
            }
        }

        public GameComponent_MechanoidMechanitorRegistry(Game game)
        {
        }

        public static Pawn? CurrentMechanicalConsciousnessHost =>
            CurrentRegistry?.mechanicalConsciousnessHost;

        public static Pawn? CurrentScenarioProtagonist =>
            CurrentRegistry?.scenarioProtagonist;

        public static bool IsMechanicalConsciousnessHost(Pawn? pawn)
        {
            return pawn != null && ReferenceEquals(CurrentMechanicalConsciousnessHost, pawn);
        }

        public static bool IsScenarioProtagonist(Pawn? pawn)
        {
            return pawn != null && ReferenceEquals(CurrentScenarioProtagonist, pawn);
        }

        public static bool RegisterScenarioPawn(Pawn? pawn, bool promoteIfNeeded = true)
        {
            GameComponent_MechanoidMechanitorRegistry? registry = CurrentRegistry;
            if (registry == null || pawn == null)
            {
                return false;
            }

            if (!MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
            {
                if (!promoteIfNeeded
                    || !MechanoidMechanitorRoleUtility.PromoteToAcquiredMechanoidMechanitor(pawn))
                {
                    return false;
                }
            }

            MechanoidMechanitorRoleUtility.EnsureRoleState(pawn);
            registry.mechanicalConsciousnessHost = pawn;
            registry.scenarioProtagonist = pawn;
            return true;
        }

        public static bool SetMechanicalConsciousnessHost(
            Pawn? pawn,
            bool promoteIfNeeded = false)
        {
            GameComponent_MechanoidMechanitorRegistry? registry = CurrentRegistry;
            if (registry == null || pawn == null)
            {
                return false;
            }

            if (!MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
            {
                if (!promoteIfNeeded
                    || !MechanoidMechanitorRoleUtility.PromoteToAcquiredMechanoidMechanitor(pawn))
                {
                    return false;
                }
            }

            registry.mechanicalConsciousnessHost = pawn;
            MechanoidMechanitorRoleUtility.EnsureRoleState(pawn);
            return true;
        }

        public static bool SetScenarioProtagonist(Pawn? pawn)
        {
            GameComponent_MechanoidMechanitorRegistry? registry = CurrentRegistry;
            if (registry == null
                || pawn == null
                || !MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
            {
                return false;
            }

            registry.scenarioProtagonist = pawn;
            return true;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(
                ref mechanicalConsciousnessHost,
                "mechanicalConsciousnessHost");
            Scribe_References.Look(
                ref scenarioProtagonist,
                "mechanoidMechanitorScenarioProtagonist");
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            TryMigrateLegacyScenarioIdentity();
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            TryMigrateLegacyScenarioIdentity();

            if (mechanicalConsciousnessHost != null)
            {
                MechanoidMechanitorRoleUtility.EnsureRoleState(mechanicalConsciousnessHost);
            }
        }

        private void TryMigrateLegacyScenarioIdentity()
        {
            if (!JusticeScenarioUtility.IsJusticeScenarioActive)
            {
                return;
            }

            if (mechanicalConsciousnessHost != null && scenarioProtagonist != null)
            {
                return;
            }

            Pawn? fallback = null;
            foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_OfPlayerFaction)
            {
                if (pawn == null || pawn.Dead)
                {
                    continue;
                }

                if (MechanoidMechanitorRoleUtility.IsNativeMechanoidMechanitor(pawn))
                {
                    fallback = pawn;
                    break;
                }

                if (fallback == null
                    && MechanoidMechanitorRoleUtility.IsAcquiredMechanoidMechanitor(pawn))
                {
                    fallback = pawn;
                }
            }

            if (fallback == null)
            {
                return;
            }

            mechanicalConsciousnessHost ??= fallback;
            scenarioProtagonist ??= fallback;
        }
    }
}
