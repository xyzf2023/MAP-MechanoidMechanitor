using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class GameComponent_MechanoidMechanitorRegistry : GameComponent
    {
        private Pawn? mechanicalConsciousnessHost;
        private Pawn? scenarioProtagonist;
        private List<Pawn> registeredMechanitors = new List<Pawn>();
        private List<Pawn> acquiredMechanitors = new List<Pawn>();
        private List<Pawn>? pendingAcquiredHediffSync;

        public Pawn? MechanicalConsciousnessHost => mechanicalConsciousnessHost;
        public Pawn? ScenarioProtagonist => scenarioProtagonist;
        public IReadOnlyList<Pawn> RegisteredMechanitors => registeredMechanitors;

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

        public static bool CanHostMechanicalConsciousness(Pawn? pawn)
        {
            return JusticeScenarioUtility.IsJusticeScenarioActive
                && pawn != null
                && !pawn.Dead
                && !pawn.Destroyed
                && pawn.RaceProps.IsMechanoid
                && pawn.Faction != null
                && pawn.Faction.IsPlayerSafe()
                && MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn);
        }

        public static bool IsRegisteredMechanitor(Pawn? pawn)
        {
            GameComponent_MechanoidMechanitorRegistry? registry = CurrentRegistry;
            if (registry == null || pawn == null)
            {
                return false;
            }

            if (pawn.Destroyed)
            {
                registry.RemoveFromRegisteredMechanitors(pawn);
                registry.RemoveFromAcquiredMechanitors(pawn);
                return false;
            }

            if (registry.HasAuthoritativeIdentity(pawn)
                && !registry.registeredMechanitors.Contains(pawn))
            {
                registry.AddToRegisteredMechanitorsIdempotent(pawn);
            }

            if (!registry.registeredMechanitors.Contains(pawn))
            {
                return false;
            }

            if (!registry.HasAuthoritativeIdentity(pawn))
            {
                registry.registeredMechanitors.Remove(pawn);
                return false;
            }

            return true;
        }

        public static bool IsRegisteredAcquiredMechanitor(Pawn? pawn)
        {
            GameComponent_MechanoidMechanitorRegistry? registry = CurrentRegistry;
            return pawn != null
                && registry != null
                && registry.acquiredMechanitors.Contains(pawn);
        }

        public static bool RegisterNativeMechanitor(Pawn? pawn)
        {
            GameComponent_MechanoidMechanitorRegistry? registry = CurrentRegistry;
            if (registry == null
                || pawn == null
                || pawn.Destroyed
                || !MechanoidMechanitorRoleUtility.HasNativeMechanitorMarker(pawn))
            {
                return false;
            }

            registry.AddToRegisteredMechanitorsIdempotent(pawn);
            return true;
        }

        public static bool GrantAcquiredMechanitorIdentity(Pawn? pawn)
        {
            GameComponent_MechanoidMechanitorRegistry? registry = CurrentRegistry;
            if (registry == null || pawn == null || pawn.Destroyed)
            {
                return false;
            }

            if (!pawn.RaceProps.IsMechanoid
                || MechanoidMechanitorRoleUtility.HasNativeMechanitorMarker(pawn))
            {
                return false;
            }

            if (registry.acquiredMechanitors.Contains(pawn))
            {
                registry.AddToRegisteredMechanitorsIdempotent(pawn);
                registry.EnsureAcquiredMechanitorHediffInternal(pawn);
                MechanoidMechanitorRoleUtility.EnsureRoleState(pawn);
                return true;
            }

            if (!MechanoidMechanitorRoleUtility.CanBecomeAcquiredMechanoidMechanitor(pawn))
            {
                return false;
            }

            registry.acquiredMechanitors.Add(pawn);
            registry.AddToRegisteredMechanitorsIdempotent(pawn);
            registry.EnsureAcquiredMechanitorHediffInternal(pawn);
            MechanoidMechanitorRoleUtility.EnsureRoleState(pawn);
            return true;
        }

        public static bool RegisterLegacyAcquiredMechanitor(Pawn? pawn)
        {
            GameComponent_MechanoidMechanitorRegistry? registry = CurrentRegistry;
            if (registry == null
                || pawn == null
                || pawn.Destroyed
                || MechanoidMechanitorRoleUtility.HasNativeMechanitorMarker(pawn)
                || !MechanoidMechanitorRoleUtility.HasAcquiredMechanitorHediff(pawn))
            {
                return false;
            }

            if (registry.acquiredMechanitors.Contains(pawn))
            {
                registry.AddToRegisteredMechanitorsIdempotent(pawn);
                return true;
            }

            registry.acquiredMechanitors.Add(pawn);
            registry.AddToRegisteredMechanitorsIdempotent(pawn);
            return true;
        }

        public static void RefreshMechanitorRegistration(Pawn? pawn)
        {
            CurrentRegistry?.RefreshMechanitorRegistrationInternal(pawn);
        }

        public static bool EnsureAcquiredMechanitorHediff(Pawn? pawn)
        {
            GameComponent_MechanoidMechanitorRegistry? registry = CurrentRegistry;
            if (registry == null || pawn == null || pawn.Destroyed)
            {
                return false;
            }

            if (!registry.acquiredMechanitors.Contains(pawn))
            {
                return false;
            }

            return registry.EnsureAcquiredMechanitorHediffInternal(pawn);
        }

        public static void NotifyAcquiredMechanitorHediffRemoved(Pawn? pawn)
        {
            GameComponent_MechanoidMechanitorRegistry? registry = CurrentRegistry;
            if (registry == null
                || pawn == null
                || pawn.Destroyed
                || !registry.acquiredMechanitors.Contains(pawn))
            {
                return;
            }

            registry.QueueAcquiredHediffSync(pawn);
            LongEventHandler.ExecuteWhenFinished(() => ProcessPendingAcquiredHediffSyncFor(pawn));
        }

        public static IEnumerable<Pawn> GetMechanicalConsciousnessCandidates()
        {
            GameComponent_MechanoidMechanitorRegistry? registry = CurrentRegistry;
            if (registry == null)
            {
                yield break;
            }

            for (int i = 0; i < registry.registeredMechanitors.Count; i++)
            {
                Pawn pawn = registry.registeredMechanitors[i];
                if (pawn == null
                    || pawn.Dead
                    || pawn.Destroyed
                    || !pawn.RaceProps.IsMechanoid
                    || pawn.Faction == null
                    || !pawn.Faction.IsPlayerSafe())
                {
                    continue;
                }

                if (!MechanoidMechanitorRoleUtility.HasNativeMechanitorMarker(pawn)
                    && !registry.acquiredMechanitors.Contains(pawn))
                {
                    continue;
                }

                yield return pawn;
            }
        }

        public static bool RegisterScenarioPawn(Pawn? pawn, bool promoteIfNeeded = true)
        {
            GameComponent_MechanoidMechanitorRegistry? registry = CurrentRegistry;
            if (registry == null
                || pawn == null
                || !JusticeScenarioUtility.IsJusticeScenarioActive)
            {
                return false;
            }

            if (!PrepareHost(pawn, promoteIfNeeded))
            {
                return false;
            }

            registry.mechanicalConsciousnessHost = pawn;
            registry.scenarioProtagonist = pawn;
            registry.AddToRegisteredMechanitorsIdempotent(pawn);
            FinalizeHostAssignment(pawn);
            return true;
        }

        public static bool SetMechanicalConsciousnessHost(
            Pawn? pawn,
            bool promoteIfNeeded = false)
        {
            GameComponent_MechanoidMechanitorRegistry? registry = CurrentRegistry;
            if (registry == null
                || pawn == null
                || !JusticeScenarioUtility.IsJusticeScenarioActive)
            {
                return false;
            }

            if (!PrepareHost(pawn, promoteIfNeeded))
            {
                return false;
            }

            registry.mechanicalConsciousnessHost = pawn;
            registry.AddToRegisteredMechanitorsIdempotent(pawn);
            FinalizeHostAssignment(pawn);
            return true;
        }

        public static bool SetScenarioProtagonist(Pawn? pawn)
        {
            GameComponent_MechanoidMechanitorRegistry? registry = CurrentRegistry;
            if (registry == null
                || pawn == null
                || !JusticeScenarioUtility.IsJusticeScenarioActive
                || !MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
            {
                return false;
            }

            registry.scenarioProtagonist = pawn;
            registry.AddToRegisteredMechanitorsIdempotent(pawn);
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
            Scribe_Collections.Look(
                ref registeredMechanitors,
                "registeredMechanoidMechanitors",
                LookMode.Reference);
            Scribe_Collections.Look(
                ref acquiredMechanitors,
                "acquiredMechanoidMechanitors",
                LookMode.Reference);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                registeredMechanitors ??= new List<Pawn>();
                acquiredMechanitors ??= new List<Pawn>();
            }
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            registeredMechanitors ??= new List<Pawn>();
            acquiredMechanitors ??= new List<Pawn>();
            CleanupAfterLoad();
            TryMigrateLegacyScenarioIdentity();
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            registeredMechanitors ??= new List<Pawn>();
            acquiredMechanitors ??= new List<Pawn>();
            CleanupAfterLoad();
            TryMigrateLegacyScenarioIdentity();

            if (mechanicalConsciousnessHost != null)
            {
                if (!MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(
                        mechanicalConsciousnessHost))
                {
                    MechanoidMechanitorRoleUtility
                        .PromoteToAcquiredMechanoidMechanitor(mechanicalConsciousnessHost);
                }

                AddToRegisteredMechanitorsIdempotent(mechanicalConsciousnessHost);
                FinalizeHostAssignment(mechanicalConsciousnessHost);
            }

            if (scenarioProtagonist != null && !scenarioProtagonist.Destroyed)
            {
                AddToRegisteredMechanitorsIdempotent(scenarioProtagonist);
            }
        }

        private static bool PrepareHost(Pawn pawn, bool promoteIfNeeded)
        {
            if (pawn.Dead
                || pawn.Destroyed
                || !pawn.RaceProps.IsMechanoid
                || pawn.Faction == null
                || !pawn.Faction.IsPlayerSafe())
            {
                return false;
            }

            if (!MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
            {
                if (!promoteIfNeeded
                    || !MechanoidMechanitorRoleUtility
                        .PromoteToAcquiredMechanoidMechanitor(pawn))
                {
                    return false;
                }
            }

            MechanoidMechanitorRoleUtility.EnsureRoleState(pawn);
            return CanHostMechanicalConsciousness(pawn);
        }

        private static void FinalizeHostAssignment(Pawn pawn)
        {
            MechanoidMechanitorRoleUtility.EnsureRoleState(pawn);
            MAPOverseerlessNodeUtility.ClearExternalOverseerIfNode(pawn);
        }

        private static void ProcessPendingAcquiredHediffSyncFor(Pawn pawn)
        {
            GameComponent_MechanoidMechanitorRegistry? registry = CurrentRegistry;
            if (registry == null
                || pawn == null
                || pawn.Destroyed
                || !registry.acquiredMechanitors.Contains(pawn))
            {
                return;
            }

            registry.pendingAcquiredHediffSync?.Remove(pawn);
            registry.EnsureAcquiredMechanitorHediffInternal(pawn);
        }

        private void RefreshMechanitorRegistrationInternal(Pawn? pawn)
        {
            if (pawn == null || pawn.Destroyed)
            {
                if (pawn != null)
                {
                    RemoveFromRegisteredMechanitors(pawn);
                    RemoveFromAcquiredMechanitors(pawn);
                }

                return;
            }

            bool hasNative = MechanoidMechanitorRoleUtility.HasNativeMechanitorMarker(pawn);
            bool inAcquired = acquiredMechanitors.Contains(pawn);
            bool hasLegacyHediff =
                MechanoidMechanitorRoleUtility.HasAcquiredMechanitorHediff(pawn);

            if (hasNative)
            {
                AddToRegisteredMechanitorsIdempotent(pawn);
            }

            if (inAcquired)
            {
                AddToRegisteredMechanitorsIdempotent(pawn);
                EnsureAcquiredMechanitorHediffInternal(pawn);
            }
            else if (hasLegacyHediff && !hasNative)
            {
                RegisterLegacyAcquiredMechanitor(pawn);
            }
            else if (!hasNative)
            {
                RemoveFromRegisteredMechanitors(pawn);
            }
        }

        private bool EnsureAcquiredMechanitorHediffInternal(Pawn pawn)
        {
            if (pawn.Destroyed || !acquiredMechanitors.Contains(pawn))
            {
                return false;
            }

            if (MechanoidMechanitorRoleUtility.HasAcquiredMechanitorHediff(pawn))
            {
                return true;
            }

            HediffDef? def = MechanoidMechanitorRoleUtility.GetAcquiredIdentityDef();
            if (def == null || pawn.health?.hediffSet == null)
            {
                return false;
            }

            pawn.health.AddHediff(def);
            return MechanoidMechanitorRoleUtility.HasAcquiredMechanitorHediff(pawn);
        }

        private bool HasAuthoritativeIdentity(Pawn pawn)
        {
            if (pawn.Destroyed)
            {
                return false;
            }

            return MechanoidMechanitorRoleUtility.HasNativeMechanitorMarker(pawn)
                || acquiredMechanitors.Contains(pawn);
        }

        private void AddToRegisteredMechanitorsIdempotent(Pawn pawn)
        {
            if (!registeredMechanitors.Contains(pawn))
            {
                registeredMechanitors.Add(pawn);
            }
        }

        private void RemoveFromRegisteredMechanitors(Pawn pawn)
        {
            registeredMechanitors.Remove(pawn);
            pendingAcquiredHediffSync?.Remove(pawn);
        }

        private void RemoveFromAcquiredMechanitors(Pawn pawn)
        {
            acquiredMechanitors.Remove(pawn);
            pendingAcquiredHediffSync?.Remove(pawn);
        }

        private void QueueAcquiredHediffSync(Pawn pawn)
        {
            pendingAcquiredHediffSync ??= new List<Pawn>();
            if (!pendingAcquiredHediffSync.Contains(pawn))
            {
                pendingAcquiredHediffSync.Add(pawn);
            }
        }

        private void CleanupAfterLoad()
        {
            registeredMechanitors ??= new List<Pawn>();
            acquiredMechanitors ??= new List<Pawn>();

            SanitizePawnList(registeredMechanitors);
            SanitizePawnList(acquiredMechanitors);

            for (int i = 0; i < acquiredMechanitors.Count; i++)
            {
                EnsureAcquiredMechanitorHediffInternal(acquiredMechanitors[i]);
            }

            for (int i = registeredMechanitors.Count - 1; i >= 0; i--)
            {
                Pawn pawn = registeredMechanitors[i];
                if (pawn == null || pawn.Destroyed)
                {
                    registeredMechanitors.RemoveAt(i);
                    continue;
                }

                RefreshMechanitorRegistrationInternal(pawn);
            }

            for (int i = registeredMechanitors.Count - 1; i >= 0; i--)
            {
                Pawn pawn = registeredMechanitors[i];
                if (pawn == null || pawn.Destroyed || !HasAuthoritativeIdentity(pawn))
                {
                    registeredMechanitors.RemoveAt(i);
                }
            }

            if (mechanicalConsciousnessHost != null
                && !mechanicalConsciousnessHost.Destroyed
                && HasAuthoritativeIdentity(mechanicalConsciousnessHost))
            {
                AddToRegisteredMechanitorsIdempotent(mechanicalConsciousnessHost);
            }

            if (scenarioProtagonist != null
                && !scenarioProtagonist.Destroyed
                && HasAuthoritativeIdentity(scenarioProtagonist))
            {
                AddToRegisteredMechanitorsIdempotent(scenarioProtagonist);
            }

            ProcessAllPendingAcquiredHediffSync();
        }

        private void ProcessAllPendingAcquiredHediffSync()
        {
            if (pendingAcquiredHediffSync == null || pendingAcquiredHediffSync.Count == 0)
            {
                return;
            }

            List<Pawn> pending = new List<Pawn>(pendingAcquiredHediffSync);
            pendingAcquiredHediffSync.Clear();
            for (int i = 0; i < pending.Count; i++)
            {
                ProcessPendingAcquiredHediffSyncFor(pending[i]);
            }
        }

        private static void SanitizePawnList(List<Pawn> pawns)
        {
            for (int i = pawns.Count - 1; i >= 0; i--)
            {
                Pawn pawn = pawns[i];
                if (pawn == null || pawn.Destroyed)
                {
                    pawns.RemoveAt(i);
                }
            }

            for (int i = pawns.Count - 1; i >= 0; i--)
            {
                Pawn pawn = pawns[i];
                for (int j = i - 1; j >= 0; j--)
                {
                    if (ReferenceEquals(pawn, pawns[j]))
                    {
                        pawns.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        private void TryMigrateLegacyScenarioIdentity()
        {
            if (!JusticeScenarioUtility.IsJusticeScenarioActive)
            {
                mechanicalConsciousnessHost = null;
                scenarioProtagonist = null;
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
