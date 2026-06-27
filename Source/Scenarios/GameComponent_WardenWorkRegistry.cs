using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class GameComponent_WardenWorkRegistry : GameComponent
    {
        private List<WardenWorkAuthorizationRecord> authorizationRecords = new List<WardenWorkAuthorizationRecord>();
        private Dictionary<Pawn, WardenWorkAuthorizationRecord> recordByPawn =
            new Dictionary<Pawn, WardenWorkAuthorizationRecord>();

        private static GameComponent_WardenWorkRegistry? CurrentRegistry
        {
            get
            {
                if (Current.Game == null)
                {
                    return null;
                }

                return Current.Game.GetComponent<GameComponent_WardenWorkRegistry>();
            }
        }

        public GameComponent_WardenWorkRegistry(Game game)
        {
        }

        public static void Grant(Pawn? pawn)
        {
            GameComponent_WardenWorkRegistry? registry = CurrentRegistry;
            if (registry == null || pawn == null || pawn.Destroyed)
            {
                return;
            }

            if (registry.FindRecordForPawn(pawn) != null)
            {
                return;
            }

            registry.AddRecord(new WardenWorkAuthorizationRecord(pawn));
        }

        public static void Revoke(Pawn? pawn)
        {
            GameComponent_WardenWorkRegistry? registry = CurrentRegistry;
            if (registry == null || pawn == null)
            {
                return;
            }

            registry.RemoveRecordForPawn(pawn);
        }

        public static bool IsAuthorized(Pawn? pawn)
        {
            GameComponent_WardenWorkRegistry? registry = CurrentRegistry;
            if (registry == null || pawn == null || pawn.Destroyed)
            {
                return false;
            }

            return registry.FindRecordForPawn(pawn) != null;
        }

        public static void EnsureState(Pawn? pawn)
        {
            if (pawn == null || pawn.Destroyed)
            {
                return;
            }

            Grant(pawn);
            WardenWorkUtility.EnsureInfrastructure(pawn);
        }

        internal static bool TryGetRecord(
            Pawn? pawn,
            out WardenWorkAuthorizationRecord? record)
        {
            record = null;
            GameComponent_WardenWorkRegistry? registry = CurrentRegistry;
            if (registry == null || pawn == null || pawn.Destroyed)
            {
                return false;
            }

            record = registry.FindRecordForPawn(pawn);
            return record != null;
        }

        internal static void MarkDefaultPriorityInitialized(Pawn? pawn)
        {
            if (!TryGetRecord(pawn, out WardenWorkAuthorizationRecord? record) || record == null)
            {
                return;
            }

            record.DefaultPriorityInitialized = true;
        }

        internal static bool IsDefaultPriorityInitialized(Pawn? pawn)
        {
            return TryGetRecord(pawn, out WardenWorkAuthorizationRecord? record)
                && record != null
                && record.DefaultPriorityInitialized;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(
                ref authorizationRecords,
                "authorizationRecords",
                LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                authorizationRecords ??= new List<WardenWorkAuthorizationRecord>();
                RebuildRecordIndex();
                CleanupInvalidRecords();
            }
        }

        private void AddRecord(WardenWorkAuthorizationRecord record)
        {
            if (record.Pawn == null)
            {
                return;
            }

            authorizationRecords.Add(record);
            recordByPawn[record.Pawn] = record;
        }

        private void RemoveRecordForPawn(Pawn pawn)
        {
            for (int i = authorizationRecords.Count - 1; i >= 0; i--)
            {
                WardenWorkAuthorizationRecord record = authorizationRecords[i];
                if (ReferenceEquals(record.Pawn, pawn))
                {
                    authorizationRecords.RemoveAt(i);
                    break;
                }
            }

            recordByPawn.Remove(pawn);
        }

        private void RebuildRecordIndex()
        {
            recordByPawn = new Dictionary<Pawn, WardenWorkAuthorizationRecord>();
            for (int i = 0; i < authorizationRecords.Count; i++)
            {
                WardenWorkAuthorizationRecord record = authorizationRecords[i];
                Pawn? pawn = record.Pawn;
                if (pawn != null && !pawn.Destroyed)
                {
                    recordByPawn[pawn] = record;
                }
            }
        }

        private void CleanupInvalidRecords()
        {
            for (int i = authorizationRecords.Count - 1; i >= 0; i--)
            {
                Pawn? pawn = authorizationRecords[i].Pawn;
                if (pawn == null || pawn.Destroyed)
                {
                    authorizationRecords.RemoveAt(i);
                }
            }

            RebuildRecordIndex();
        }

        private WardenWorkAuthorizationRecord? FindRecordForPawn(Pawn pawn)
        {
            if (recordByPawn.TryGetValue(pawn, out WardenWorkAuthorizationRecord? indexed))
            {
                return indexed;
            }

            for (int i = 0; i < authorizationRecords.Count; i++)
            {
                WardenWorkAuthorizationRecord candidate = authorizationRecords[i];
                if (ReferenceEquals(candidate.Pawn, pawn))
                {
                    recordByPawn[pawn] = candidate;
                    return candidate;
                }
            }

            return null;
        }
    }
}
