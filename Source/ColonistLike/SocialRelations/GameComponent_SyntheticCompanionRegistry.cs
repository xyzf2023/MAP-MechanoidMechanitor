using System.Collections.Generic;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 仿生伴侣动态授权注册表。与机械族机械师身份注册表相互独立。
    /// </summary>
    public sealed class GameComponent_SyntheticCompanionRegistry : GameComponent
    {
        private List<SyntheticCompanionAuthorizationRecord> authorizationRecords =
            new List<SyntheticCompanionAuthorizationRecord>();

        private Dictionary<Pawn, SyntheticCompanionAuthorizationRecord> recordByPawn =
            new Dictionary<Pawn, SyntheticCompanionAuthorizationRecord>();

        private static GameComponent_SyntheticCompanionRegistry? CurrentRegistry
        {
            get
            {
                if (Current.Game == null)
                {
                    return null;
                }

                return Current.Game.GetComponent<GameComponent_SyntheticCompanionRegistry>();
            }
        }

        public GameComponent_SyntheticCompanionRegistry(Game game)
        {
        }

        public static bool IsAuthorized(Pawn? pawn)
        {
            return TryGetRecord(pawn, out _);
        }

        public static bool TryGetRecord(Pawn? pawn, out ISyntheticCompanionState? state)
        {
            state = null;
            GameComponent_SyntheticCompanionRegistry? registry = CurrentRegistry;
            if (registry == null || pawn == null || pawn.Destroyed)
            {
                return false;
            }

            SyntheticCompanionAuthorizationRecord? record = registry.FindRecordForPawn(pawn);
            if (record == null)
            {
                return false;
            }

            state = record;
            return true;
        }

        /// <summary>
        /// 为有效机械体创建默认仿生伴侣授权。已授权时返回 false。
        /// </summary>
        public static bool TryAuthorize(Pawn? pawn)
        {
            GameComponent_SyntheticCompanionRegistry? registry = CurrentRegistry;
            if (registry == null
                || pawn == null
                || pawn.Destroyed
                || pawn.RaceProps == null
                || !pawn.RaceProps.IsMechanoid)
            {
                return false;
            }

            if (registry.FindRecordForPawn(pawn) != null)
            {
                return false;
            }

            SyntheticCompanionAuthorizationRecord record =
                new SyntheticCompanionAuthorizationRecord(pawn);
            registry.AddRecord(record);
            ColonistLikeSocialTrackerUtility.EnsureTrackers(pawn);
            return true;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                CleanupInvalidRecords();
            }

            Scribe_Collections.Look(
                ref authorizationRecords,
                "syntheticCompanionAuthorizationRecords",
                LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                authorizationRecords ??= new List<SyntheticCompanionAuthorizationRecord>();
                CleanupInvalidRecords();
                RebuildRecordIndex();
                EnsureTrackersForIndexedRecords();
            }
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            authorizationRecords ??= new List<SyntheticCompanionAuthorizationRecord>();
            CleanupInvalidRecords();
            RebuildRecordIndex();
            EnsureTrackersForIndexedRecords();
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            authorizationRecords ??= new List<SyntheticCompanionAuthorizationRecord>();
            CleanupInvalidRecords();
            RebuildRecordIndex();
            EnsureTrackersForIndexedRecords();
        }

        private void AddRecord(SyntheticCompanionAuthorizationRecord record)
        {
            if (record.Pawn == null)
            {
                return;
            }

            authorizationRecords.Add(record);
            recordByPawn[record.Pawn] = record;
        }

        private SyntheticCompanionAuthorizationRecord? FindRecordForPawn(Pawn pawn)
        {
            if (recordByPawn.TryGetValue(pawn, out SyntheticCompanionAuthorizationRecord? indexed))
            {
                return indexed;
            }

            for (int i = 0; i < authorizationRecords.Count; i++)
            {
                SyntheticCompanionAuthorizationRecord candidate = authorizationRecords[i];
                if (candidate != null && ReferenceEquals(candidate.Pawn, pawn))
                {
                    if (!pawn.Discarded)
                    {
                        recordByPawn[pawn] = candidate;
                    }

                    return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// 移除空记录、空 Pawn 引用，以及已经永久 Discarded 的 Pawn。
        /// 死亡、位于尸体中、未生成、远行队中或暂时离图的 Pawn 仍保留授权；
        /// 不可用 Destroyed 判断，因尸体中的 InnerPawn 也会处于 Destroyed。
        /// </summary>
        private void CleanupInvalidRecords()
        {
            authorizationRecords ??= new List<SyntheticCompanionAuthorizationRecord>();

            for (int i = authorizationRecords.Count - 1; i >= 0; i--)
            {
                SyntheticCompanionAuthorizationRecord? record = authorizationRecords[i];
                if (record == null
                    || record.Pawn == null
                    || record.Pawn.Discarded)
                {
                    authorizationRecords.RemoveAt(i);
                }
            }

            for (int i = authorizationRecords.Count - 1; i >= 0; i--)
            {
                Pawn? pawn = authorizationRecords[i].Pawn;
                if (pawn == null)
                {
                    continue;
                }

                for (int j = i - 1; j >= 0; j--)
                {
                    if (ReferenceEquals(pawn, authorizationRecords[j].Pawn))
                    {
                        authorizationRecords.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        private void RebuildRecordIndex()
        {
            recordByPawn = new Dictionary<Pawn, SyntheticCompanionAuthorizationRecord>();
            for (int i = 0; i < authorizationRecords.Count; i++)
            {
                SyntheticCompanionAuthorizationRecord record = authorizationRecords[i];
                Pawn? pawn = record?.Pawn;
                // 排除永久 Discarded 的 Pawn；尸体中的死亡 Pawn（Destroyed 但仍可复活）保留索引。
                if (pawn != null && !pawn.Discarded)
                {
                    recordByPawn[pawn] = record!;
                }
            }
        }

        private void EnsureTrackersForIndexedRecords()
        {
            foreach (KeyValuePair<Pawn, SyntheticCompanionAuthorizationRecord> pair in recordByPawn)
            {
                ColonistLikeSocialTrackerUtility.EnsureTrackers(pair.Key);
            }
        }
    }
}
