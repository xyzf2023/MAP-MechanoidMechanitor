using MAP_MechanoidMechanitor;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
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

            if (pawn.GetComp<CompWardenWorkUser>() != null)
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
            if (registry == null || pawn == null || pawn.Destroyed)
            {
                return;
            }

            if (registry.FindRecordForPawn(pawn) == null)
            {
                return;
            }

            registry.RemoveRecordForPawn(pawn);

            if (WardenWorkUtility.IsAuthorized(pawn))
            {
                WardenWorkUtility.EnsureInfrastructure(pawn);
                return;
            }

            if (MechWorkSettingsUtility.TryEnsureWorkSettingsInitialized(pawn))
            {
                pawn.Notify_DisabledWorkTypesChanged();

                WorkTypeDef? warden = WardenWorkUtility.WardenWorkType;
                if (warden != null)
                {
                    pawn.workSettings!.SetPriority(warden, 0);
                }

                MechWorkSettingsUtility.RestrictToMechEnabledWorkTypes(pawn);
            }
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

            if (pawn.GetComp<CompWardenWorkUser>() != null)
            {
                WardenWorkUtility.EnsureInfrastructure(pawn);
                return;
            }

            if (!TryGetRecord(pawn, out _))
            {
                Grant(pawn);
            }

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
                NormalizeAuthorizationRecords();
                RebuildRecordIndex();
                EnsureInfrastructureForAllAuthorizedPawns();
            }
        }

        private void EnsureInfrastructureForAllAuthorizedPawns()
        {
            List<Pawn> authorizedPawns = new List<Pawn>();
            for (int i = 0; i < authorizationRecords.Count; i++)
            {
                Pawn? pawn = authorizationRecords[i].Pawn;
                if (pawn != null && !pawn.Destroyed)
                {
                    authorizedPawns.Add(pawn);
                }
            }

            for (int i = 0; i < authorizedPawns.Count; i++)
            {
                WardenWorkUtility.EnsureInfrastructure(authorizedPawns[i]);
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
                WardenWorkAuthorizationRecord? record = authorizationRecords[i];
                if (record == null)
                {
                    authorizationRecords.RemoveAt(i);
                    continue;
                }

                if (ReferenceEquals(record.Pawn, pawn))
                {
                    authorizationRecords.RemoveAt(i);
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

        /// <summary>
        /// 读档后清理失效记录、迁移静态 Comp 状态，并按 Pawn 合并重复动态授权。
        /// </summary>
        private void NormalizeAuthorizationRecords()
        {
            List<WardenWorkAuthorizationRecord> source = authorizationRecords;
            List<WardenWorkAuthorizationRecord> cleaned =
                new List<WardenWorkAuthorizationRecord>();
            Dictionary<Pawn, WardenWorkAuthorizationRecord> keptByPawn =
                new Dictionary<Pawn, WardenWorkAuthorizationRecord>();

            for (int i = 0; i < source.Count; i++)
            {
                WardenWorkAuthorizationRecord? record = source[i];
                if (record == null)
                {
                    continue;
                }

                Pawn? pawn = record.Pawn;
                if (pawn == null || pawn.Destroyed)
                {
                    continue;
                }

                // 静态 Comp 已授权：迁移初始化状态后丢弃动态记录。
                if (pawn.GetComp<CompWardenWorkUser>() is CompWardenWorkUser comp)
                {
                    if (record.DefaultPriorityInitialized)
                    {
                        comp.MarkDefaultPriorityInitialized();
                    }

                    continue;
                }

                if (keptByPawn.TryGetValue(pawn, out WardenWorkAuthorizationRecord? existing)
                    && existing != null)
                {
                    MergeDuplicateRecordState(existing, record);
                    continue;
                }

                keptByPawn[pawn] = record;
                cleaned.Add(record);
            }

            authorizationRecords = cleaned;
        }

        private static void MergeDuplicateRecordState(
            WardenWorkAuthorizationRecord keep,
            WardenWorkAuthorizationRecord duplicate)
        {
            if (duplicate.DefaultPriorityInitialized)
            {
                keep.DefaultPriorityInitialized = true;
            }
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
