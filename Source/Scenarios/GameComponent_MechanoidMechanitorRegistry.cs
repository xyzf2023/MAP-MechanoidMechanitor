using System.Collections.Generic;
using MAP_MechanoidMechanitor;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class GameComponent_MechanoidMechanitorRegistry : GameComponent
    {
        private Pawn? mechanicalConsciousnessHost;
        private Pawn? scenarioProtagonist;
        private List<MechanoidMechanitorRecord> mechanitorRecords = new List<MechanoidMechanitorRecord>();
        private Dictionary<Pawn, MechanoidMechanitorRecord> recordByPawn =
            new Dictionary<Pawn, MechanoidMechanitorRecord>();
        private List<Pawn>? registeredMechanitorsCache;
        private List<Pawn>? pendingAcquiredHediffSync;

        public Pawn? MechanicalConsciousnessHost => mechanicalConsciousnessHost;
        public Pawn? ScenarioProtagonist => scenarioProtagonist;

        public IReadOnlyList<Pawn> RegisteredMechanitors
        {
            get
            {
                EnsureRegisteredMechanitorsCache();
                return registeredMechanitorsCache!;
            }
        }

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

        public static bool TryGetMechanitorRecord(
            Pawn? pawn,
            out MechanoidMechanitorRecord? record)
        {
            record = null;
            GameComponent_MechanoidMechanitorRegistry? registry = CurrentRegistry;
            if (registry == null || pawn == null || pawn.Destroyed)
            {
                return false;
            }

            record = registry.FindRecordForPawn(pawn);
            return record != null;
        }

        public static bool TryGetNativeMechanitorRecord(
            Pawn? pawn,
            out MechanoidMechanitorRecord? record)
        {
            record = null;
            if (!TryGetMechanitorRecord(pawn, out MechanoidMechanitorRecord? candidate)
                || candidate == null
                || candidate.Origin != MechanoidMechanitorOrigin.Native)
            {
                return false;
            }

            record = candidate;
            return true;
        }

        public static bool TryGetAcquiredMechanitorRecord(
            Pawn? pawn,
            out MechanoidMechanitorRecord? record)
        {
            record = null;
            if (!TryGetMechanitorRecord(pawn, out MechanoidMechanitorRecord? candidate)
                || candidate == null
                || candidate.Origin != MechanoidMechanitorOrigin.Acquired)
            {
                return false;
            }

            record = candidate;
            return true;
        }

        public static bool EnsureNativeMechanitorRecord(
            Pawn? pawn,
            bool pendingLegacyNativeStateImport = false)
        {
            GameComponent_MechanoidMechanitorRegistry? registry = CurrentRegistry;
            if (registry == null
                || pawn == null
                || pawn.Destroyed
                || !MechanoidMechanitorRoleUtility.HasNativeMechanitorMarker(pawn))
            {
                return false;
            }

            MechanoidMechanitorRecord? existing = registry.FindRecordForPawn(pawn);
            if (existing != null)
            {
                if (existing.Origin != MechanoidMechanitorOrigin.Native)
                {
                    existing.Origin = MechanoidMechanitorOrigin.Native;
                    registry.InvalidateDerivedCaches();
                }

                return true;
            }

            registry.AddRecord(new MechanoidMechanitorRecord(
                pawn,
                MechanoidMechanitorOrigin.Native,
                pendingLegacyNativeStateImport));
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

            MechanoidMechanitorRecord? existing = registry.FindRecordForPawn(pawn);
            if (existing != null && existing.Origin == MechanoidMechanitorOrigin.Acquired)
            {
                registry.EnsureAcquiredMechanitorHediffInternal(pawn);
                MechanoidMechanitorWorkAuthorizationUtility.GrantAndEnsureInfrastructure(pawn);
                MechanoidMechanitorRoleUtility.EnsureRoleState(pawn);
                return true;
            }

            if (!MechanoidMechanitorRoleUtility.CanBecomeAcquiredMechanoidMechanitor(pawn))
            {
                return false;
            }

            registry.AddRecord(new MechanoidMechanitorRecord(
                pawn,
                MechanoidMechanitorOrigin.Acquired));
            registry.EnsureAcquiredMechanitorHediffInternal(pawn);
            MechanoidMechanitorWorkAuthorizationUtility.GrantAndEnsureInfrastructure(pawn);
            MechanoidMechanitorRoleUtility.EnsureRoleState(pawn);
            return true;
        }

        public static bool EnsureAcquiredMechanitorHediff(Pawn? pawn)
        {
            GameComponent_MechanoidMechanitorRegistry? registry = CurrentRegistry;
            if (registry == null || pawn == null || pawn.Destroyed)
            {
                return false;
            }

            if (!TryGetAcquiredMechanitorRecord(pawn, out _))
            {
                return false;
            }

            return registry.EnsureAcquiredMechanitorHediffInternal(pawn);
        }

        internal static void NotifyAcquiredMechanitorHediffRemoved(Pawn? pawn)
        {
            GameComponent_MechanoidMechanitorRegistry? registry = CurrentRegistry;
            if (registry == null
                || pawn == null
                || pawn.Destroyed
                || !TryGetAcquiredMechanitorRecord(pawn, out _))
            {
                return;
            }

            registry.QueueAcquiredHediffSync(pawn);
        }

        internal static void RequestAcquiredHediffMirror(Pawn? pawn)
        {
            GameComponent_MechanoidMechanitorRegistry? registry = CurrentRegistry;
            if (registry == null
                || pawn == null
                || pawn.Destroyed
                || !TryGetAcquiredMechanitorRecord(pawn, out _))
            {
                return;
            }

            if (MechanoidMechanitorRoleUtility.HasAcquiredMechanitorHediff(pawn))
            {
                return;
            }

            if (registry.EnsureAcquiredMechanitorHediffInternal(pawn))
            {
                return;
            }

            registry.QueueAcquiredHediffSync(pawn);
        }

        public static IEnumerable<Pawn> GetMechanicalConsciousnessCandidates()
        {
            GameComponent_MechanoidMechanitorRegistry? registry = CurrentRegistry;
            if (registry == null)
            {
                yield break;
            }

            for (int i = 0; i < registry.mechanitorRecords.Count; i++)
            {
                MechanoidMechanitorRecord record = registry.mechanitorRecords[i];
                Pawn? pawn = record.Pawn;
                if (pawn == null
                    || pawn.Dead
                    || pawn.Destroyed
                    || !pawn.RaceProps.IsMechanoid
                    || pawn.Faction == null
                    || !pawn.Faction.IsPlayerSafe())
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
            return true;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(
                ref mechanitorRecords,
                "mechanitorRecords",
                LookMode.Deep);
            Scribe_References.Look(
                ref mechanicalConsciousnessHost,
                "mechanicalConsciousnessHost");
            Scribe_References.Look(
                ref scenarioProtagonist,
                "mechanoidMechanitorScenarioProtagonist");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                mechanitorRecords ??= new List<MechanoidMechanitorRecord>();
                CleanupRecords();
                RebuildRecordIndex();
            }
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            ProcessPendingAcquiredHediffSyncOnTick();
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            mechanitorRecords ??= new List<MechanoidMechanitorRecord>();
            pendingAcquiredHediffSync = null;
            CleanupRecords();
            RebuildRecordIndex();
            TryMigrateLegacyScenarioIdentity();
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            mechanitorRecords ??= new List<MechanoidMechanitorRecord>();
            CleanupRecords();
            RebuildRecordIndex();
            ProcessPendingLegacyNativeStateImports();
            RestoreAcquiredRecordsAfterLoad();
            TryMigrateLegacyScenarioIdentity();

            if (mechanicalConsciousnessHost != null
                && MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(
                    mechanicalConsciousnessHost))
            {
                FinalizeHostAssignment(mechanicalConsciousnessHost);
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

        private void AddRecord(MechanoidMechanitorRecord record)
        {
            if (record.Pawn == null)
            {
                return;
            }

            mechanitorRecords.Add(record);
            recordByPawn[record.Pawn] = record;
            InvalidateDerivedCaches();
        }

        private void CleanupRecords()
        {
            mechanitorRecords ??= new List<MechanoidMechanitorRecord>();

            for (int i = mechanitorRecords.Count - 1; i >= 0; i--)
            {
                MechanoidMechanitorRecord record = mechanitorRecords[i];
                if (record == null || record.Pawn == null || record.Pawn.Destroyed)
                {
                    mechanitorRecords.RemoveAt(i);
                }
            }

            for (int i = mechanitorRecords.Count - 1; i >= 0; i--)
            {
                MechanoidMechanitorRecord record = mechanitorRecords[i];
                Pawn? pawn = record.Pawn;
                if (pawn == null)
                {
                    continue;
                }

                for (int j = i - 1; j >= 0; j--)
                {
                    if (ReferenceEquals(pawn, mechanitorRecords[j].Pawn))
                    {
                        mechanitorRecords.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        private void RebuildRecordIndex()
        {
            recordByPawn = new Dictionary<Pawn, MechanoidMechanitorRecord>();
            for (int i = 0; i < mechanitorRecords.Count; i++)
            {
                MechanoidMechanitorRecord record = mechanitorRecords[i];
                Pawn? pawn = record.Pawn;
                if (pawn != null && !pawn.Destroyed)
                {
                    recordByPawn[pawn] = record;
                }
            }

            InvalidateDerivedCaches();
        }

        private void InvalidateDerivedCaches()
        {
            registeredMechanitorsCache = null;
        }

        private void EnsureRegisteredMechanitorsCache()
        {
            if (registeredMechanitorsCache != null)
            {
                return;
            }

            registeredMechanitorsCache = new List<Pawn>();
            for (int i = 0; i < mechanitorRecords.Count; i++)
            {
                Pawn? pawn = mechanitorRecords[i].Pawn;
                if (pawn != null && !pawn.Destroyed)
                {
                    registeredMechanitorsCache.Add(pawn);
                }
            }
        }

        private void ProcessPendingLegacyNativeStateImports()
        {
            for (int i = 0; i < mechanitorRecords.Count; i++)
            {
                MechanoidMechanitorRecord record = mechanitorRecords[i];
                if (!record.PendingLegacyNativeStateImport
                    || record.Origin != MechanoidMechanitorOrigin.Native
                    || record.Pawn == null
                    || record.Pawn.Destroyed)
                {
                    continue;
                }

                ImportLegacyNativeState(record);
                record.PendingLegacyNativeStateImport = false;

                CompJusticeSelfWorkMode? workModeComp =
                    CompJusticeSelfWorkMode.GetFor(record.Pawn);
                workModeComp?.SyncSelfWorkModeEffectsFromAuthoritativeState();
            }
        }

        private static void ImportLegacyNativeState(MechanoidMechanitorRecord record)
        {
            Pawn pawn = record.Pawn!;
            if (CompMAPMechanitorNode.TryGetNodeComp(pawn, out CompMAPMechanitorNode? nodeComp)
                && nodeComp != null)
            {
                int legacyBonus = nodeComp.GetLegacyChipBandwidthBonusForMigration();
                int maxBonus = MechanoidMechanitorRecord.GetMaxChipBandwidthBonus(
                    pawn,
                    MechanoidMechanitorOrigin.Native);
                record.ChipBandwidthBonus = UnityEngine.Mathf.Clamp(legacyBonus, 0, maxBonus);
            }

            CompJusticeSelfWorkMode? workModeComp = CompJusticeSelfWorkMode.GetFor(pawn);
            if (workModeComp != null)
            {
                record.SelfWorkMode = MechanoidMechanitorSelfWorkModeUtility.SanitizeWorkMode(
                    workModeComp.GetLegacySelfWorkModeForMigration());
            }

            pawn.mechanitor?.Notify_BandwidthChanged();
        }

        private MechanoidMechanitorRecord? FindRecordForPawn(Pawn pawn)
        {
            if (recordByPawn.TryGetValue(pawn, out MechanoidMechanitorRecord? indexed))
            {
                return indexed;
            }

            for (int i = 0; i < mechanitorRecords.Count; i++)
            {
                MechanoidMechanitorRecord candidate = mechanitorRecords[i];
                if (ReferenceEquals(candidate.Pawn, pawn))
                {
                    recordByPawn[pawn] = candidate;
                    return candidate;
                }
            }

            return null;
        }

        private void RestoreAcquiredRecordsAfterLoad()
        {
            for (int i = 0; i < mechanitorRecords.Count; i++)
            {
                MechanoidMechanitorRecord record = mechanitorRecords[i];
                if (record.Origin != MechanoidMechanitorOrigin.Acquired
                    || record.Pawn == null
                    || record.Pawn.Destroyed)
                {
                    continue;
                }

                Pawn pawn = record.Pawn;
                if (!EnsureAcquiredMechanitorHediffInternal(pawn))
                {
                    QueueAcquiredHediffSync(pawn);
                }

                MechanoidMechanitorWorkAuthorizationUtility.GrantAndEnsureInfrastructure(pawn);
                MechanoidMechanitorRoleUtility.EnsureRoleState(pawn);
                MechanoidMechanitorSelfWorkModeUtility.ApplyAcquiredSelfWorkMode(
                    pawn,
                    record.SelfWorkMode
                        ?? MechanoidMechanitorSelfWorkModeUtility.SanitizeWorkMode(null));
                pawn.mechanitor?.Notify_BandwidthChanged();
            }
        }

        private void ProcessPendingAcquiredHediffSyncOnTick()
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

        private void ProcessPendingAcquiredHediffSyncFor(Pawn pawn)
        {
            if (pawn == null
                || pawn.Destroyed
                || !TryGetAcquiredMechanitorRecord(pawn, out _))
            {
                return;
            }

            if (EnsureAcquiredMechanitorHediffInternal(pawn))
            {
                MechanoidMechanitorWorkAuthorizationUtility.GrantAndEnsureInfrastructure(pawn);
                MechanoidMechanitorRoleUtility.EnsureRoleState(pawn);
            }
            else
            {
                QueueAcquiredHediffSync(pawn);
            }
        }

        private bool EnsureAcquiredMechanitorHediffInternal(Pawn pawn)
        {
            if (pawn.Destroyed || !TryGetAcquiredMechanitorRecord(pawn, out _))
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
            mechanitorRecords ??= new List<MechanoidMechanitorRecord>();
            CleanupRecords();
            RebuildRecordIndex();
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
            for (int i = 0; i < mechanitorRecords.Count; i++)
            {
                MechanoidMechanitorRecord record = mechanitorRecords[i];
                Pawn? pawn = record.Pawn;
                if (pawn == null
                    || pawn.Dead
                    || pawn.Destroyed
                    || !pawn.RaceProps.IsMechanoid
                    || pawn.Faction == null
                    || !pawn.Faction.IsPlayerSafe())
                {
                    continue;
                }

                if (record.Origin == MechanoidMechanitorOrigin.Native)
                {
                    fallback = pawn;
                    break;
                }

                if (fallback == null && record.Origin == MechanoidMechanitorOrigin.Acquired)
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
