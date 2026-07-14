using MAP_MechanoidMechanitor;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class GameComponent_AnimalHandlingWorkRegistry : GameComponent
    {
        private List<AnimalHandlingWorkAuthorizationRecord> authorizationRecords =
            new List<AnimalHandlingWorkAuthorizationRecord>();
        private Dictionary<Pawn, AnimalHandlingWorkAuthorizationRecord> recordByPawn =
            new Dictionary<Pawn, AnimalHandlingWorkAuthorizationRecord>();

        private static GameComponent_AnimalHandlingWorkRegistry? CurrentRegistry
        {
            get
            {
                if (Current.Game == null)
                {
                    return null;
                }

                return Current.Game.GetComponent<GameComponent_AnimalHandlingWorkRegistry>();
            }
        }

        public GameComponent_AnimalHandlingWorkRegistry(Game game)
        {
        }

        public static void Grant(Pawn? pawn, int defaultPriority)
        {
            GameComponent_AnimalHandlingWorkRegistry? registry = CurrentRegistry;
            if (registry == null || pawn == null || pawn.Destroyed)
            {
                return;
            }

            if (pawn.GetComp<CompAnimalHandlingWorkUser>() != null)
            {
                return;
            }

            AnimalHandlingWorkAuthorizationRecord? existing =
                registry.FindRecordForPawn(pawn);
            if (existing != null)
            {
                existing.DefaultPriority = AnimalHandlingWorkUtility.ClampDefaultPriority(
                    defaultPriority);
                return;
            }

            registry.AddRecord(new AnimalHandlingWorkAuthorizationRecord(
                pawn,
                AnimalHandlingWorkUtility.ClampDefaultPriority(defaultPriority)));
        }

        public static void Revoke(Pawn? pawn)
        {
            GameComponent_AnimalHandlingWorkRegistry? registry = CurrentRegistry;
            if (registry == null || pawn == null || pawn.Destroyed)
            {
                return;
            }

            if (registry.FindRecordForPawn(pawn) == null)
            {
                return;
            }

            registry.RemoveRecordForPawn(pawn);

            if (AnimalHandlingWorkUtility.IsAuthorized(pawn))
            {
                return;
            }

            if (pawn.workSettings != null
                && MechWorkSettingsUtility.TryEnsureWorkSettingsInitialized(pawn))
            {
                pawn.Notify_DisabledWorkTypesChanged();

                WorkTypeDef? handling = AnimalHandlingWorkUtility.HandlingWorkType;
                if (handling != null)
                {
                    pawn.workSettings.SetPriority(handling, 0);
                }

                MechWorkSettingsUtility.RestrictToMechEnabledWorkTypes(pawn);
            }
        }

        public static bool IsAuthorized(Pawn? pawn)
        {
            if (pawn == null || pawn.Destroyed)
            {
                return false;
            }

            if (pawn.GetComp<CompAnimalHandlingWorkUser>() != null)
            {
                return true;
            }

            GameComponent_AnimalHandlingWorkRegistry? registry = CurrentRegistry;
            if (registry == null)
            {
                return false;
            }

            return registry.FindRecordForPawn(pawn) != null;
        }

        public static int GetDefaultPriority(Pawn? pawn)
        {
            CompAnimalHandlingWorkUser? comp = pawn?.GetComp<CompAnimalHandlingWorkUser>();
            if (comp != null)
            {
                return AnimalHandlingWorkUtility.ClampDefaultPriority(comp.DefaultPriority);
            }

            if (!TryGetRecord(pawn, out AnimalHandlingWorkAuthorizationRecord? record)
                || record == null)
            {
                return 3;
            }

            return AnimalHandlingWorkUtility.ClampDefaultPriority(record.DefaultPriority);
        }

        public static bool IsDefaultPriorityInitialized(Pawn? pawn)
        {
            CompAnimalHandlingWorkUser? comp = pawn?.GetComp<CompAnimalHandlingWorkUser>();
            if (comp != null)
            {
                return comp.DefaultPriorityInitialized;
            }

            return TryGetRecord(pawn, out AnimalHandlingWorkAuthorizationRecord? record)
                && record != null
                && record.DefaultPriorityInitialized;
        }

        public static void MarkDefaultPriorityInitialized(Pawn? pawn)
        {
            CompAnimalHandlingWorkUser? comp = pawn?.GetComp<CompAnimalHandlingWorkUser>();
            if (comp != null)
            {
                comp.MarkDefaultPriorityInitialized();
                return;
            }

            if (TryGetRecord(pawn, out AnimalHandlingWorkAuthorizationRecord? record)
                && record != null)
            {
                record.DefaultPriorityInitialized = true;
            }
        }

        public static void EnsureState(Pawn? pawn)
        {
            if (pawn == null || pawn.Destroyed)
            {
                return;
            }

            if (pawn.GetComp<CompAnimalHandlingWorkUser>() != null)
            {
                AnimalHandlingWorkUtility.EnsureInfrastructure(pawn);
                return;
            }

            GameComponent_AnimalHandlingWorkRegistry? registry = CurrentRegistry;
            if (registry == null)
            {
                return;
            }

            if (registry.FindRecordForPawn(pawn) == null)
            {
                Grant(pawn, 3);
            }

            AnimalHandlingWorkUtility.EnsureInfrastructure(pawn);
        }

        internal static bool TryGetRecord(
            Pawn? pawn,
            out AnimalHandlingWorkAuthorizationRecord? record)
        {
            record = null;
            GameComponent_AnimalHandlingWorkRegistry? registry = CurrentRegistry;
            if (registry == null || pawn == null || pawn.Destroyed)
            {
                return false;
            }

            record = registry.FindRecordForPawn(pawn);
            return record != null;
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
                authorizationRecords ??= new List<AnimalHandlingWorkAuthorizationRecord>();
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
                AnimalHandlingWorkUtility.EnsureInfrastructure(authorizedPawns[i]);
            }
        }

        private void AddRecord(AnimalHandlingWorkAuthorizationRecord record)
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
                AnimalHandlingWorkAuthorizationRecord? record = authorizationRecords[i];
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
            recordByPawn = new Dictionary<Pawn, AnimalHandlingWorkAuthorizationRecord>();
            for (int i = 0; i < authorizationRecords.Count; i++)
            {
                AnimalHandlingWorkAuthorizationRecord record = authorizationRecords[i];
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
            List<AnimalHandlingWorkAuthorizationRecord> source = authorizationRecords;
            List<AnimalHandlingWorkAuthorizationRecord> cleaned =
                new List<AnimalHandlingWorkAuthorizationRecord>();
            Dictionary<Pawn, AnimalHandlingWorkAuthorizationRecord> keptByPawn =
                new Dictionary<Pawn, AnimalHandlingWorkAuthorizationRecord>();

            for (int i = 0; i < source.Count; i++)
            {
                AnimalHandlingWorkAuthorizationRecord? record = source[i];
                if (record == null)
                {
                    continue;
                }

                Pawn? pawn = record.Pawn;
                if (pawn == null || pawn.Destroyed)
                {
                    continue;
                }

                // 静态 Comp 已授权：迁移初始化状态后丢弃动态记录，不覆盖 Comp 默认优先级。
                if (pawn.GetComp<CompAnimalHandlingWorkUser>() is CompAnimalHandlingWorkUser comp)
                {
                    if (record.DefaultPriorityInitialized)
                    {
                        comp.MarkDefaultPriorityInitialized();
                    }

                    continue;
                }

                if (keptByPawn.TryGetValue(
                        pawn, out AnimalHandlingWorkAuthorizationRecord? existing)
                    && existing != null)
                {
                    MergeDuplicateRecordState(existing, record);
                    continue;
                }

                record.DefaultPriority =
                    AnimalHandlingWorkUtility.ClampDefaultPriority(record.DefaultPriority);
                keptByPawn[pawn] = record;
                cleaned.Add(record);
            }

            authorizationRecords = cleaned;
        }

        private static void MergeDuplicateRecordState(
            AnimalHandlingWorkAuthorizationRecord keep,
            AnimalHandlingWorkAuthorizationRecord duplicate)
        {
            if (!keep.DefaultPriorityInitialized && duplicate.DefaultPriorityInitialized)
            {
                keep.DefaultPriority = AnimalHandlingWorkUtility.ClampDefaultPriority(
                    duplicate.DefaultPriority);
                keep.DefaultPriorityInitialized = true;
            }
        }

        private AnimalHandlingWorkAuthorizationRecord? FindRecordForPawn(Pawn pawn)
        {
            if (recordByPawn.TryGetValue(pawn, out AnimalHandlingWorkAuthorizationRecord? indexed))
            {
                return indexed;
            }

            for (int i = 0; i < authorizationRecords.Count; i++)
            {
                AnimalHandlingWorkAuthorizationRecord candidate = authorizationRecords[i];
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
