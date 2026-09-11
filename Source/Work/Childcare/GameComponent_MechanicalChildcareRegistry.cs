using MAP_MechanoidMechanitor;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class GameComponent_MechanicalChildcareRegistry : GameComponent
    {
        private List<MechanicalChildcareAuthorizationRecord> authorizationRecords =
            new List<MechanicalChildcareAuthorizationRecord>();
        private Dictionary<Pawn, MechanicalChildcareAuthorizationRecord> recordByPawn =
            new Dictionary<Pawn, MechanicalChildcareAuthorizationRecord>();

        private static GameComponent_MechanicalChildcareRegistry? CurrentRegistry =>
            CurrentGameComponentCache<GameComponent_MechanicalChildcareRegistry>.Get();

        public GameComponent_MechanicalChildcareRegistry(Game game)
        {
        }

        public static void Grant(Pawn? pawn, int defaultPriority)
        {
            GameComponent_MechanicalChildcareRegistry? registry = CurrentRegistry;
            if (registry == null || pawn == null || pawn.Destroyed)
            {
                return;
            }

            if (pawn.GetComp<CompMechanicalChildcareUser>() != null)
            {
                return;
            }

            MechanicalChildcareAuthorizationRecord? existing =
                registry.FindRecordForPawn(pawn);
            if (existing != null)
            {
                existing.DefaultPriority = MechanicalChildcareUtility.ClampDefaultPriority(
                    defaultPriority);
                return;
            }

            registry.AddRecord(new MechanicalChildcareAuthorizationRecord(
                pawn,
                MechanicalChildcareUtility.ClampDefaultPriority(defaultPriority)));
        }

        public static void Revoke(Pawn? pawn)
        {
            GameComponent_MechanicalChildcareRegistry? registry = CurrentRegistry;
            if (registry == null || pawn == null || pawn.Discarded)
            {
                return;
            }

            if (registry.FindRecordForPawn(pawn) == null)
            {
                return;
            }

            registry.RemoveRecordForPawn(pawn);

            // 死亡/尸体中等 Destroyed 但未 Discarded：只清理持久记录，不碰 workSettings。
            if (pawn.Dead || pawn.Destroyed)
            {
                return;
            }

            if (MechanicalChildcareUtility.IsAuthorized(pawn))
            {
                return;
            }

            if (pawn.workSettings != null
                && MechWorkSettingsUtility.TryEnsureWorkSettingsInitialized(pawn))
            {
                pawn.Notify_DisabledWorkTypesChanged();

                WorkTypeDef? childcare = MechanicalChildcareUtility.ChildcareWorkType;
                if (childcare != null)
                {
                    pawn.workSettings.SetPriority(childcare, 0);
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

            if (pawn.GetComp<CompMechanicalChildcareUser>() != null)
            {
                return true;
            }

            GameComponent_MechanicalChildcareRegistry? registry = CurrentRegistry;
            if (registry == null)
            {
                return false;
            }

            return registry.FindRecordForPawn(pawn) != null;
        }

        public static int GetDefaultPriority(Pawn? pawn)
        {
            CompMechanicalChildcareUser? comp = pawn?.GetComp<CompMechanicalChildcareUser>();
            if (comp != null)
            {
                return MechanicalChildcareUtility.ClampDefaultPriority(comp.DefaultPriority);
            }

            if (!TryGetRecord(pawn, out MechanicalChildcareAuthorizationRecord? record)
                || record == null)
            {
                return 3;
            }

            return MechanicalChildcareUtility.ClampDefaultPriority(record.DefaultPriority);
        }

        public static bool IsDefaultPriorityInitialized(Pawn? pawn)
        {
            CompMechanicalChildcareUser? comp = pawn?.GetComp<CompMechanicalChildcareUser>();
            if (comp != null && comp.DefaultPriorityInitialized)
            {
                return true;
            }

            return TryGetRecord(pawn, out MechanicalChildcareAuthorizationRecord? record)
                && record != null
                && record.DefaultPriorityInitialized;
        }

        public static void MarkDefaultPriorityInitialized(Pawn? pawn)
        {
            CompMechanicalChildcareUser? comp = pawn?.GetComp<CompMechanicalChildcareUser>();
            if (comp != null)
            {
                comp.MarkDefaultPriorityInitialized();
                return;
            }

            if (TryGetRecord(pawn, out MechanicalChildcareAuthorizationRecord? record)
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

            if (pawn.GetComp<CompMechanicalChildcareUser>() != null)
            {
                MechanicalChildcareUtility.EnsureInfrastructure(pawn);
                return;
            }

            GameComponent_MechanicalChildcareRegistry? registry = CurrentRegistry;
            if (registry == null)
            {
                return;
            }

            if (registry.FindRecordForPawn(pawn) == null)
            {
                Grant(pawn, 3);
            }

            MechanicalChildcareUtility.EnsureInfrastructure(pawn);
        }

        internal static bool TryGetRecord(
            Pawn? pawn,
            out MechanicalChildcareAuthorizationRecord? record)
        {
            record = null;
            GameComponent_MechanicalChildcareRegistry? registry = CurrentRegistry;
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
                authorizationRecords ??= new List<MechanicalChildcareAuthorizationRecord>();
                RebuildRecordIndex();
                CleanupInvalidRecords();
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
                MechanicalChildcareUtility.EnsureInfrastructure(authorizedPawns[i]);
            }
        }

        private void AddRecord(MechanicalChildcareAuthorizationRecord record)
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
                MechanicalChildcareAuthorizationRecord record = authorizationRecords[i];
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
            recordByPawn = new Dictionary<Pawn, MechanicalChildcareAuthorizationRecord>();
            for (int i = 0; i < authorizationRecords.Count; i++)
            {
                MechanicalChildcareAuthorizationRecord record = authorizationRecords[i];
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

            Dictionary<Pawn, MechanicalChildcareAuthorizationRecord> keptRecords =
                new Dictionary<Pawn, MechanicalChildcareAuthorizationRecord>();
            List<MechanicalChildcareAuthorizationRecord> cleanedRecords =
                new List<MechanicalChildcareAuthorizationRecord>();

            for (int i = 0; i < authorizationRecords.Count; i++)
            {
                MechanicalChildcareAuthorizationRecord record = authorizationRecords[i];
                Pawn? pawn = record.Pawn;
                if (pawn == null)
                {
                    continue;
                }

                if (pawn.GetComp<CompMechanicalChildcareUser>() is CompMechanicalChildcareUser comp)
                {
                    if (record.DefaultPriorityInitialized)
                    {
                        comp.MarkDefaultPriorityInitialized();
                    }

                    continue;
                }

                if (keptRecords.TryGetValue(pawn, out MechanicalChildcareAuthorizationRecord? existing)
                    && existing != null)
                {
                    MergeDuplicateRecordState(existing, record);
                    continue;
                }

                keptRecords[pawn] = record;
                cleanedRecords.Add(record);
            }

            authorizationRecords = cleanedRecords;
            RebuildRecordIndex();
        }

        private static void MergeDuplicateRecordState(
            MechanicalChildcareAuthorizationRecord keep,
            MechanicalChildcareAuthorizationRecord duplicate)
        {
            if (!keep.DefaultPriorityInitialized && duplicate.DefaultPriorityInitialized)
            {
                keep.DefaultPriority = duplicate.DefaultPriority;
                keep.DefaultPriorityInitialized = true;
            }
        }

        private MechanicalChildcareAuthorizationRecord? FindRecordForPawn(Pawn pawn)
        {
            if (recordByPawn.TryGetValue(pawn, out MechanicalChildcareAuthorizationRecord? indexed))
            {
                return indexed;
            }

            for (int i = 0; i < authorizationRecords.Count; i++)
            {
                MechanicalChildcareAuthorizationRecord candidate = authorizationRecords[i];
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
