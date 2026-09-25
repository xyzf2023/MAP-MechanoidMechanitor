using System;
using System.Collections.Generic;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 仿生伴侣动态授权注册表。与机械族机械师身份注册表相互独立。
    /// </summary>
    public sealed class GameComponent_SyntheticCompanionRegistry : GameComponent
    {
        private const string CompanionHediffDefName = "MAP_BionicCompanionModuleInstalled";
        private static HediffDef? cachedCompanionHediffDef;

        private static readonly IReadOnlyList<SyntheticCompanionAuthorizationRecord>
            EmptyAuthorizationSnapshot =
                Array.Empty<SyntheticCompanionAuthorizationRecord>();

        private List<SyntheticCompanionAuthorizationRecord> authorizationRecords =
            new List<SyntheticCompanionAuthorizationRecord>();

        private Dictionary<Pawn, SyntheticCompanionAuthorizationRecord> recordByPawn =
            new Dictionary<Pawn, SyntheticCompanionAuthorizationRecord>();

        private static GameComponent_SyntheticCompanionRegistry? CurrentRegistry =>
            CurrentGameComponentCache<GameComponent_SyntheticCompanionRegistry>.Get();

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
            if (record == null || !record.AuthorizationEnabled)
            {
                return false;
            }

            state = record;
            return true;
        }

        /// <summary>
        /// 是否存在有效动态授权记录（含死亡/尸体中/离图；不含停用记录与 Discarded）。
        /// </summary>
        public static bool HasAuthorizationRecord(Pawn? pawn)
        {
            GameComponent_SyntheticCompanionRegistry? registry = CurrentRegistry;
            if (registry == null || pawn == null || pawn.Discarded)
            {
                return false;
            }

            return registry.FindRecordForPawn(pawn)?.AuthorizationEnabled == true;
        }

        /// <summary>
        /// 动态授权记录快照：新集合，不暴露内部可变列表。
        /// 包含死亡、尸体中、远行队、未生成与暂时离图；排除空记录、停用记录与 Discarded。
        /// </summary>
        public static IReadOnlyList<SyntheticCompanionAuthorizationRecord>
            GetAuthorizationRecordSnapshot()
        {
            GameComponent_SyntheticCompanionRegistry? registry = CurrentRegistry;
            if (registry == null)
            {
                return EmptyAuthorizationSnapshot;
            }

            List<SyntheticCompanionAuthorizationRecord> snapshot =
                new List<SyntheticCompanionAuthorizationRecord>();
            List<SyntheticCompanionAuthorizationRecord> records =
                registry.authorizationRecords;
            for (int i = 0; i < records.Count; i++)
            {
                SyntheticCompanionAuthorizationRecord? record = records[i];
                Pawn? pawn = record?.Pawn;
                if (record == null || !record.AuthorizationEnabled || pawn == null || pawn.Discarded)
                {
                    continue;
                }

                snapshot.Add(record);
            }

            return snapshot;
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

            SyntheticCompanionAuthorizationRecord? existing = registry.FindRecordForPawn(pawn);
            if (existing != null)
            {
                if (existing.AuthorizationEnabled) return false;
                existing.SetAuthorizationEnabled(true);
                ColonistLikeSocialTrackerUtility.EnsureTrackers(pawn);
                SynchronizeCompanionHediff(pawn, authorized: true);
                return true;
            }

            SyntheticCompanionAuthorizationRecord record =
                new SyntheticCompanionAuthorizationRecord(pawn);
            registry.AddRecord(record);
            ColonistLikeSocialTrackerUtility.EnsureTrackers(pawn);
            SynchronizeCompanionHediff(pawn, authorized: true);
            return true;
        }

        /// <summary>
        /// 撤销动态授权但保留设置，以便重新授权。不销毁 tracker、关系、床位、子女或孕期。
        /// </summary>
        public static bool TryRevokeAuthorization(Pawn? pawn)
        {
            GameComponent_SyntheticCompanionRegistry? registry = CurrentRegistry;
            if (registry == null || pawn == null)
            {
                return false;
            }

            SyntheticCompanionAuthorizationRecord? record = registry.FindRecordForPawn(pawn);
            if (record == null || !record.AuthorizationEnabled) return false;
            record.SetAuthorizationEnabled(false);
            SynchronizeCompanionHediff(pawn, authorized: false);
            foreach (Pawn partner in SyntheticCompanionRelationshipUtility.GetPartners(pawn))
                SyntheticCompanionRelationshipUtility.StopPairJobs(pawn, partner);
            // 无伴侣时也可能仍在执行或排队等待追求；撤销后不能把它带入下一次存档。
            pawn.jobs?.jobQueue?.RemoveAll(pawn, job => job.def == MAPMechanitor_JobDefOf.MAP_SyntheticRomance);
            if (!pawn.Dead && pawn.Spawned && pawn.CurJobDef == MAPMechanitor_JobDefOf.MAP_SyntheticRomance)
                pawn.jobs?.EndCurrentJob(Verse.AI.JobCondition.InterruptForced);
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
            SynchronizeCompanionHediffs();
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            authorizationRecords ??= new List<SyntheticCompanionAuthorizationRecord>();
            CleanupInvalidRecords();
            RebuildRecordIndex();
            EnsureTrackersForIndexedRecords();
            SynchronizeCompanionHediffs();
        }

        private void SynchronizeCompanionHediffs()
        {
            foreach (KeyValuePair<Pawn, SyntheticCompanionAuthorizationRecord> pair in recordByPawn)
            {
                SynchronizeCompanionHediff(pair.Key, pair.Value.AuthorizationEnabled);
            }
        }

        private static void SynchronizeCompanionHediff(Pawn pawn, bool authorized)
        {
            if (pawn.Discarded || pawn.health?.hediffSet == null)
            {
                return;
            }

            HediffDef? def = cachedCompanionHediffDef ??=
                DefDatabase<HediffDef>.GetNamedSilentFail(CompanionHediffDefName);
            if (def == null)
            {
                Log.Error($"[MAP-机械族机械师] 未找到仿生伴侣健康状态 {CompanionHediffDefName}。");
                return;
            }

            try
            {
                Hediff? keeper = null;
                List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
                for (int i = hediffs.Count - 1; i >= 0; i--)
                {
                    Hediff? hediff = hediffs[i];
                    if (hediff?.def != def)
                    {
                        continue;
                    }

                    if (authorized && keeper == null)
                    {
                        keeper = hediff;
                    }
                    else
                    {
                        pawn.health.RemoveHediff(hediff);
                    }
                }

                if (authorized && keeper == null && !pawn.Destroyed)
                {
                    pawn.health.AddHediff(def);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[MAP-机械族机械师] 同步仿生伴侣健康状态失败：" +
                    $"pawn={pawn.LabelShort}（{pawn.ThingID}）：{ex}");
            }
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
                if (pair.Value.AuthorizationEnabled)
                    ColonistLikeSocialTrackerUtility.EnsureTrackers(pair.Key);
            }
        }
    }
}
