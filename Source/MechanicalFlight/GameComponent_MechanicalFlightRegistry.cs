using System;
using System.Collections.Generic;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class GameComponent_MechanicalFlightRegistry : GameComponent
    {
        private const string DefaultProfileDefName = "MAP_DefaultMechanicalFlightProfile";
        private static readonly IReadOnlyList<MechanicalFlightAuthorizationRecord> EmptySnapshot =
            Array.Empty<MechanicalFlightAuthorizationRecord>();

        private List<MechanicalFlightAuthorizationRecord> authorizationRecords = new();
        private Dictionary<Pawn, MechanicalFlightAuthorizationRecord> recordByPawn = new();
        private List<MechanicalFlightAuthorizationRecord> activeRecords = new();

        private static GameComponent_MechanicalFlightRegistry? CurrentRegistry =>
            Current.Game?.GetComponent<GameComponent_MechanicalFlightRegistry>();

        public GameComponent_MechanicalFlightRegistry(Game game)
        {
        }

        public static bool IsAuthorized(Pawn? pawn)
        {
            return TryGetRecord(pawn, out _);
        }

        public static bool HasAuthorizationRecord(Pawn? pawn)
        {
            GameComponent_MechanicalFlightRegistry? registry = CurrentRegistry;
            return registry != null && pawn != null && !pawn.Discarded
                && registry.FindRecord(pawn) != null;
        }

        public static bool TryGetRecord(
            Pawn? pawn,
            out MechanicalFlightAuthorizationRecord? record)
        {
            record = null;
            GameComponent_MechanicalFlightRegistry? registry = CurrentRegistry;
            if (registry == null || pawn == null || pawn.Discarded)
            {
                return false;
            }

            record = registry.FindRecord(pawn);
            return record != null;
        }

        public static IReadOnlyList<MechanicalFlightAuthorizationRecord>
            GetAuthorizationRecordSnapshot()
        {
            GameComponent_MechanicalFlightRegistry? registry = CurrentRegistry;
            if (registry == null)
            {
                return EmptySnapshot;
            }

            List<MechanicalFlightAuthorizationRecord> snapshot = new();
            for (int i = 0; i < registry.authorizationRecords.Count; i++)
            {
                MechanicalFlightAuthorizationRecord? record =
                    registry.authorizationRecords[i];
                if (record?.Pawn != null && !record.Pawn.Discarded)
                {
                    snapshot.Add(record);
                }
            }
            return snapshot;
        }

        public static bool TryAuthorize(
            Pawn? pawn,
            MechanicalFlightProfileDef? profile = null)
        {
            GameComponent_MechanicalFlightRegistry? registry = CurrentRegistry;
            if (registry == null || pawn == null || pawn.Destroyed || pawn.Discarded
                || pawn.RaceProps?.IsMechanoid != true || registry.FindRecord(pawn) != null)
            {
                return false;
            }

            profile ??= DefDatabase<MechanicalFlightProfileDef>.GetNamedSilentFail(
                DefaultProfileDefName);
            if (profile == null)
            {
                Log.Error("[MAP-机械族机械师] 无法授予飞行能力：默认飞行配置不存在。");
                return false;
            }

            MechanicalFlightAuthorizationRecord record = new(pawn, profile);
            registry.authorizationRecords.Add(record);
            registry.recordByPawn[pawn] = record;
            return true;
        }

        public static bool TryRevokeAuthorization(Pawn? pawn)
        {
            GameComponent_MechanicalFlightRegistry? registry = CurrentRegistry;
            if (registry == null || pawn == null)
            {
                return false;
            }

            MechanicalFlightAuthorizationRecord? record = registry.FindRecord(pawn);
            if (record == null)
            {
                return false;
            }

            MechanicalFlightUtility.ClearRuntimeState(record, forceLand: true);
            registry.authorizationRecords.RemoveAll(candidate =>
                candidate != null && ReferenceEquals(candidate.Pawn, pawn));
            registry.recordByPawn.Remove(pawn);
            registry.activeRecords.Remove(record);
            return true;
        }

        internal static void NotifyRuntimeStateChanged(
            MechanicalFlightAuthorizationRecord record)
        {
            GameComponent_MechanicalFlightRegistry? registry = CurrentRegistry;
            if (registry == null)
            {
                return;
            }

            if (record.IsRuntimeActive)
            {
                if (!registry.activeRecords.Contains(record))
                {
                    registry.activeRecords.Add(record);
                }
            }
            else
            {
                registry.activeRecords.Remove(record);
            }
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            for (int i = activeRecords.Count - 1; i >= 0; i--)
            {
                MechanicalFlightAuthorizationRecord? record = activeRecords[i];
                if (record == null || record.Pawn == null || record.Pawn.Discarded)
                {
                    activeRecords.RemoveAt(i);
                    continue;
                }

                MechanicalFlightUtility.Tick(record);
                if (!record.IsRuntimeActive)
                {
                    activeRecords.Remove(record);
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                CleanupInvalidRecords();
            }

            Scribe_Collections.Look(ref authorizationRecords,
                "mechanicalFlightAuthorizationRecords", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                RebuildCaches();
                MechanicalFlightUtility.ReconcileAfterLoad(activeRecords);
            }
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            RebuildCaches();
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            RebuildCaches();
            MechanicalFlightUtility.ReconcileAfterLoad(activeRecords);
        }

        private MechanicalFlightAuthorizationRecord? FindRecord(Pawn pawn)
        {
            if (recordByPawn.TryGetValue(pawn, out MechanicalFlightAuthorizationRecord? record))
            {
                return record;
            }

            for (int i = 0; i < authorizationRecords.Count; i++)
            {
                MechanicalFlightAuthorizationRecord? candidate = authorizationRecords[i];
                if (candidate != null && ReferenceEquals(candidate.Pawn, pawn))
                {
                    recordByPawn[pawn] = candidate;
                    return candidate;
                }
            }
            return null;
        }

        private void CleanupInvalidRecords()
        {
            authorizationRecords ??= new List<MechanicalFlightAuthorizationRecord>();
            HashSet<Pawn> seen = new();
            for (int i = authorizationRecords.Count - 1; i >= 0; i--)
            {
                MechanicalFlightAuthorizationRecord? record = authorizationRecords[i];
                Pawn? pawn = record?.Pawn;
                if (record == null || pawn == null || pawn.Discarded || !seen.Add(pawn))
                {
                    authorizationRecords.RemoveAt(i);
                }
            }
        }

        private void RebuildCaches()
        {
            CleanupInvalidRecords();
            recordByPawn = new Dictionary<Pawn, MechanicalFlightAuthorizationRecord>();
            activeRecords = new List<MechanicalFlightAuthorizationRecord>();
            for (int i = 0; i < authorizationRecords.Count; i++)
            {
                MechanicalFlightAuthorizationRecord record = authorizationRecords[i];
                Pawn pawn = record.Pawn!;
                recordByPawn[pawn] = record;
                if (record.IsRuntimeActive)
                {
                    activeRecords.Add(record);
                }
            }
        }
    }
}
