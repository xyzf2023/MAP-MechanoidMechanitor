using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class GameComponent_MechanoidMechanitorRegistry : GameComponent
    {
        private const string MechanicalConsciousnessHediffDefName = "MAP_MechanicalConsciousness";
        private const string NativeMechanitorHediffDefName = "MAP_NativeMechanoidMechanitor";

        private static HediffDef? cachedMechanicalConsciousnessHediffDef;
        private static HediffDef? cachedNativeMechanitorHediffDef;

        private Pawn? mechanicalConsciousnessHost;
        private List<MechanoidMechanitorRecord> mechanitorRecords = new List<MechanoidMechanitorRecord>();
        private Dictionary<Pawn, MechanoidMechanitorRecord> recordByPawn =
            new Dictionary<Pawn, MechanoidMechanitorRecord>();
        private List<Pawn>? registeredMechanitorsCache;
        private ReadOnlyCollection<Pawn>? registeredMechanitorsReadOnlyCache;
        private HashSet<Pawn> pendingMechanitorInitializations = new HashSet<Pawn>();

        public Pawn? MechanicalConsciousnessHost => mechanicalConsciousnessHost;

        private static readonly IReadOnlyList<Pawn> EmptyRegisteredMechanitors =
            Array.Empty<Pawn>();

        public IReadOnlyList<Pawn> RegisteredMechanitors
        {
            get
            {
                EnsureRegisteredMechanitorsCache();
                return registeredMechanitorsReadOnlyCache!;
            }
        }

        /// <summary>
        /// 当前游戏中已注册且活跃的机械族机械师只读缓存（排除 Dead/Destroyed）。
        /// 无游戏或注册表时返回空列表。调试窗口应改用 GetPersistentRecordSnapshot()。
        /// </summary>
        public static IReadOnlyList<Pawn> CurrentRegisteredMechanitors
        {
            get
            {
                GameComponent_MechanoidMechanitorRegistry? registry = CurrentRegistry;
                return registry?.RegisteredMechanitors ?? EmptyRegisteredMechanitors;
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

        public static bool IsMechanicalConsciousnessHost(Pawn? pawn)
        {
            return pawn != null && ReferenceEquals(CurrentMechanicalConsciousnessHost, pawn);
        }

        public static bool CanHostMechanicalConsciousness(Pawn? pawn)
        {
            return MechanoidMechanitorScenarioUtility.IsScenarioActive
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

        /// <summary>
        /// 是否存在持久化机械师记录（含死亡/尸体中/离图；不含 Discarded）。
        /// </summary>
        public static bool HasPersistentRecord(Pawn? pawn)
        {
            GameComponent_MechanoidMechanitorRegistry? registry = CurrentRegistry;
            if (registry == null || pawn == null || pawn.Discarded)
            {
                return false;
            }

            return registry.FindRecordForPawn(pawn) != null;
        }

        /// <summary>
        /// 持久化记录快照：供调试窗口读取真实存档记录，而非活跃缓存。
        /// 包含死亡、尸体中、远行队、未生成与暂时离图；排除空记录与 Discarded。
        /// </summary>
        public static IReadOnlyList<MechanoidMechanitorRegistrySnapshotEntry>
            GetPersistentRecordSnapshot()
        {
            GameComponent_MechanoidMechanitorRegistry? registry = CurrentRegistry;
            if (registry == null)
            {
                return Array.Empty<MechanoidMechanitorRegistrySnapshotEntry>();
            }

            List<MechanoidMechanitorRegistrySnapshotEntry> snapshot =
                new List<MechanoidMechanitorRegistrySnapshotEntry>();
            List<MechanoidMechanitorRecord> records = registry.mechanitorRecords;
            for (int i = 0; i < records.Count; i++)
            {
                MechanoidMechanitorRecord? record = records[i];
                Pawn? pawn = record?.Pawn;
                if (record == null || pawn == null || pawn.Discarded)
                {
                    continue;
                }

                snapshot.Add(new MechanoidMechanitorRegistrySnapshotEntry(
                    pawn,
                    record.Origin,
                    ReferenceEquals(registry.mechanicalConsciousnessHost, pawn)));
            }

            return snapshot;
        }

        /// <summary>
        /// 开发者模式统一注册入口。同步由注册表与现有权威升格流程负责。
        /// </summary>
        public static bool TryRegisterFromDebug(Pawn? pawn)
        {
            GameComponent_MechanoidMechanitorRegistry? registry = CurrentRegistry;
            if (registry == null
                || pawn == null
                || pawn.Dead
                || pawn.Destroyed
                || pawn.Discarded
                || pawn.RaceProps == null
                || !pawn.RaceProps.IsMechanoid)
            {
                return false;
            }

            if (registry.FindRecordForPawn(pawn) != null)
            {
                return false;
            }

            if (MechanoidMechanitorRoleUtility.HasNativeMechanitorMarker(pawn))
            {
                if (!EnsureNativeMechanitorRecord(pawn) || !HasPersistentRecord(pawn))
                {
                    return false;
                }

                // 注册表已提交：后续同步异常只记日志，仍返回成功，避免“失败但已有记录”。
                try
                {
                    MechanoidMechanitorWorkAuthorizationUtility.GrantAndEnsureInfrastructure(pawn);
                }
                catch (Exception ex)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 调试注册原生机械族机械师后状态同步失败：" +
                        $"phase=GrantAndEnsureInfrastructure，" +
                        $"pawn={pawn.LabelShort}（{pawn.ThingID}）：{ex}");
                }

                try
                {
                    MechanoidMechanitorRoleUtility.EnsureRoleState(pawn);
                }
                catch (Exception ex)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 调试注册原生机械族机械师后状态同步失败：" +
                        $"phase=EnsureRoleState，" +
                        $"pawn={pawn.LabelShort}（{pawn.ThingID}）：{ex}");
                }

                try
                {
                    NotifyScenarioColonistDisplaysIfNeeded();
                }
                catch (Exception ex)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 调试注册原生机械族机械师后状态同步失败：" +
                        $"phase=NotifyScenarioColonistDisplays，" +
                        $"pawn={pawn.LabelShort}（{pawn.ThingID}）：{ex}");
                }

                return HasPersistentRecord(pawn);
            }

            return MechanoidMechanitorRoleUtility.PromoteToAcquiredMechanoidMechanitor(pawn);
        }

        /// <summary>
        /// 开发者模式统一删除入口。由注册表删除记录并收尾身份健康状态与派生同步。
        /// </summary>
        public static bool TryUnregisterFromDebug(Pawn? pawn)
        {
            GameComponent_MechanoidMechanitorRegistry? registry = CurrentRegistry;
            if (registry == null || pawn == null)
            {
                return false;
            }

            MechanoidMechanitorRecord? record = registry.FindRecordForPawn(pawn);
            if (record == null)
            {
                return false;
            }

            MechanoidMechanitorOrigin origin = record.Origin;
            bool wasHost = ReferenceEquals(registry.mechanicalConsciousnessHost, pawn);

            // 原生/后天均撤销附属动态工作授权；单个注册表失败不阻止主记录删除。
            try
            {
                MechanoidMechanitorWorkAuthorizationUtility.RevokeGrantedAuthorizations(pawn);
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 调试删除机械族机械师时撤销工作授权异常：" +
                    $"pawn={pawn.LabelShort}（{pawn.ThingID}）：{ex}");
            }

            registry.RemoveRecordForPawnInternal(pawn);
            registry.pendingMechanitorInitializations.Remove(pawn);

            if (wasHost)
            {
                registry.mechanicalConsciousnessHost = null;
            }

            try
            {
                HediffDef? consciousnessDef = GetMechanicalConsciousnessHediffDef();
                if (consciousnessDef != null)
                {
                    RemoveAllHediffsFromPawn(pawn, consciousnessDef, "机械意识");
                }

                if (origin == MechanoidMechanitorOrigin.Acquired)
                {
                    HediffDef? acquiredDef =
                        MechanoidMechanitorRoleUtility.GetAcquiredIdentityDef();
                    if (acquiredDef != null)
                    {
                        RemoveAllHediffsFromPawn(pawn, acquiredDef, "机械族机械师");
                    }
                }
                else if (origin == MechanoidMechanitorOrigin.Native)
                {
                    HediffDef? nativeDef = GetNativeMechanitorHediffDef();
                    if (nativeDef != null)
                    {
                        RemoveAllHediffsFromPawn(pawn, nativeDef, "先天机械族机械师");
                    }
                }

                registry.SynchronizeMechanicalConsciousnessHediff();

                // 尸体 InnerPawn 为 Destroyed，业务同步跳过；健康状态已在上方按 Origin 清理。
                if (!pawn.Destroyed && !pawn.Discarded)
                {
                    MechanoidMechanitorSelfWorkModeUtility.SyncSelfWorkModeEffects(pawn);
                    pawn.Notify_DisabledWorkTypesChanged();
                    PawnComponentsUtility.AddAndRemoveDynamicComponents(
                        pawn,
                        actAsIfSpawned: true);
                    pawn.mechanitor?.Notify_BandwidthChanged();
                }

                NotifyScenarioColonistDisplaysIfNeeded();
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 调试删除机械族机械师记录后状态同步失败：" +
                    $"pawn={pawn.LabelShort}（{pawn.ThingID}）：{ex}");
            }

            return true;
        }

        public static bool EnsureNativeMechanitorRecord(Pawn? pawn)
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
                bool convertedToNative = existing.Origin != MechanoidMechanitorOrigin.Native;
                if (convertedToNative)
                {
                    existing.Origin = MechanoidMechanitorOrigin.Native;
                    registry.InvalidateDerivedCaches();
                    registry.SynchronizeAcquiredMechanitorHediffs();
                }

                registry.SynchronizeNativeMechanitorHediffs();
                return true;
            }

            registry.AddRecord(new MechanoidMechanitorRecord(
                pawn,
                MechanoidMechanitorOrigin.Native));
            registry.SynchronizeAcquiredMechanitorHediffs();
            registry.SynchronizeNativeMechanitorHediffs();
            return true;
        }

        /// <summary>
        /// 仅用于新 Pawn 生成事务失败、且该 Pawn 从未成为正式宿主时的注册表清理。
        /// </summary>
        /// <returns>
        /// 成功清理或确认无记录且非宿主时返回 true；宿主拒绝、注册表不可用或异常时返回 false。
        /// </returns>
        internal static bool RemoveFailedGeneratedMechanitor(Pawn? pawn)
        {
            try
            {
                GameComponent_MechanoidMechanitorRegistry? registry = CurrentRegistry;
                if (registry == null)
                {
                    return false;
                }

                if (pawn == null)
                {
                    return true;
                }

                if (IsMechanicalConsciousnessHost(pawn))
                {
                    Log.Error(
                        "[MAP-机械族机械师] 拒绝清理失败生成 Pawn：其已成为机械意识宿主，" +
                        $"pawn={pawn.LabelShort}（{pawn.ThingID}）。");
                    return false;
                }

                registry.RemoveRecordForPawnInternal(pawn);
                registry.pendingMechanitorInitializations.Remove(pawn);

                if (HasRecordForPawnIncludingDestroyed(pawn))
                {
                    Log.Error(
                        "[MAP-机械族机械师] 失败生成 Pawn 注册表清理验证失败：记录仍存在，" +
                        $"pawn={pawn.LabelShort}（{pawn.ThingID}），" +
                        $"pawnDestroyed={pawn.Destroyed}。");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 失败生成 Pawn 注册表清理异常：" +
                    $"pawn={pawn?.LabelShort ?? "null"}（{pawn?.ThingID ?? "null"}）：{ex}");
                return false;
            }
        }

        /// <summary>
        /// 按引用查询机械师记录；包含已 Destroyed 的 Pawn，仅供失败事务清理与诊断使用。
        /// </summary>
        internal static bool HasRecordForPawnIncludingDestroyed(Pawn? pawn)
        {
            GameComponent_MechanoidMechanitorRegistry? registry = CurrentRegistry;
            if (registry == null || pawn == null)
            {
                return false;
            }

            if (registry.recordByPawn.ContainsKey(pawn))
            {
                return true;
            }

            List<MechanoidMechanitorRecord> records = registry.mechanitorRecords;
            for (int i = 0; i < records.Count; i++)
            {
                MechanoidMechanitorRecord? record = records[i];
                if (record != null && ReferenceEquals(record.Pawn, pawn))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool GrantAcquiredMechanitorIdentity(Pawn? pawn)
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
                try
                {
                    AcquiredMechanitorStateUtility.EnsureAcquiredMechanitorState(
                        pawn,
                        existing);
                }
                catch (Exception ex)
                {
                    LogAcquiredMechanitorStateFailure(
                        pawn,
                        existing,
                        hadExistingRecord: true,
                        phase: "已有后天机械师状态修复",
                        ex);
                    return false;
                }

                registry.SynchronizeAcquiredMechanitorHediffs();
                registry.SynchronizeNativeMechanitorHediffs();
                NotifyScenarioColonistDisplaysIfNeeded();
                return true;
            }

            if (!MechanoidMechanitorRoleUtility.CanBecomeAcquiredMechanoidMechanitor(pawn))
            {
                return false;
            }

            MechanoidMechanitorRecord record = new MechanoidMechanitorRecord(
                pawn,
                MechanoidMechanitorOrigin.Acquired);
            registry.AddRecord(record);

            try
            {
                AcquiredMechanitorStateUtility.EnsureAcquiredMechanitorState(pawn, record);
            }
            catch (Exception ex)
            {
                LogAcquiredMechanitorStateFailure(
                    pawn,
                    record,
                    hadExistingRecord: false,
                    phase: "新建后天机械师初始化",
                    ex);

                try
                {
                    registry.RollbackFailedNewAcquiredIdentity(pawn);
                }
                catch (Exception rollbackEx)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 新建后天机械师初始化失败后回滚异常：" +
                        $"pawn={pawn.LabelShort}（{pawn.ThingID}）：{rollbackEx}");
                }

                return false;
            }

            registry.SynchronizeAcquiredMechanitorHediffs();
            registry.SynchronizeNativeMechanitorHediffs();
            NotifyScenarioColonistDisplaysIfNeeded();
            return true;
        }

        private static void LogAcquiredMechanitorStateFailure(
            Pawn pawn,
            MechanoidMechanitorRecord record,
            bool hadExistingRecord,
            string phase,
            Exception ex)
        {
            Log.Error(
                $"[MAP-机械族机械师] {phase}失败：" +
                $"pawn={pawn.LabelShort}（{pawn.ThingID}），" +
                $"hadExistingRecord={hadExistingRecord}，" +
                $"recordOrigin={record.Origin}：{ex}");
        }

        private void RollbackFailedNewAcquiredIdentity(Pawn pawn)
        {
            if (IsMechanicalConsciousnessHost(pawn))
            {
                Log.Error(
                    "[MAP-机械族机械师] 新建后天机械师初始化失败回滚中止：其为当前机械意识宿主，" +
                    $"pawn={pawn.LabelShort}（{pawn.ThingID}）。");
                return;
            }

            RemoveRecordForPawnInternal(pawn);
            pendingMechanitorInitializations.Remove(pawn);
        }

        private static void NotifyScenarioColonistDisplaysIfNeeded()
        {
            if (!GameComponent_MechanoidMechanitorScenarioState.IsEnabled)
            {
                return;
            }

            MechanoidMechanitorScenarioFreeColonistUtility.NotifyColonistDisplaysDirtyIfReady();
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
                || !MechanoidMechanitorScenarioUtility.IsScenarioActive)
            {
                return false;
            }

            if (!PrepareHost(pawn, promoteIfNeeded))
            {
                return false;
            }

            registry.mechanicalConsciousnessHost = pawn;
            FinalizeHostAssignment(pawn);
            registry.SynchronizeMechanicalConsciousnessHediff();
            return true;
        }

        public static bool SetMechanicalConsciousnessHost(
            Pawn? pawn,
            bool promoteIfNeeded = false)
        {
            GameComponent_MechanoidMechanitorRegistry? registry = CurrentRegistry;
            if (registry == null
                || pawn == null
                || !MechanoidMechanitorScenarioUtility.IsScenarioActive)
            {
                return false;
            }

            if (!PrepareHost(pawn, promoteIfNeeded))
            {
                return false;
            }

            registry.mechanicalConsciousnessHost = pawn;
            FinalizeHostAssignment(pawn);
            registry.SynchronizeMechanicalConsciousnessHediff();
            return true;
        }

        internal static bool TryReplaceMechanicalConsciousnessHost(
            Pawn expectedCurrentHost,
            Pawn newHost)
        {
            GameComponent_MechanoidMechanitorRegistry? registry = CurrentRegistry;
            if (registry == null
                || !MechanoidMechanitorScenarioUtility.IsScenarioActive)
            {
                return false;
            }

            if (expectedCurrentHost == null
                || newHost == null
                || ReferenceEquals(expectedCurrentHost, newHost))
            {
                return false;
            }

            if (!ReferenceEquals(registry.mechanicalConsciousnessHost, expectedCurrentHost))
            {
                return false;
            }

            if (!CanHostMechanicalConsciousness(newHost))
            {
                return false;
            }

            MechanoidMechanitorRoleUtility.EnsureRoleState(newHost);
            if (!MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(newHost))
            {
                return false;
            }

            MAPOverseerlessNodeUtility.ClearExternalOverseerIfNode(newHost);

            registry.mechanicalConsciousnessHost = newHost;

            try
            {
                registry.SynchronizeMechanicalConsciousnessHediff();
                NotifyScenarioColonistDisplaysIfNeeded();
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 替换机械意识宿主后 post-commit 处理失败：" +
                    $"newHost={newHost.LabelShort}（{newHost.ThingID}），" +
                    $"oldHost={expectedCurrentHost.LabelShort}（{expectedCurrentHost.ThingID}）：{ex}");
            }

            return true;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                // 仅清理永久 Discarded；不得因 Destroyed/Dead 误删可复活尸体中的记录。
                CleanupRecords();
            }

            Scribe_Collections.Look(
                ref mechanitorRecords,
                "mechanitorRecords",
                LookMode.Deep);
            Scribe_References.Look(
                ref mechanicalConsciousnessHost,
                "mechanicalConsciousnessHost");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                mechanitorRecords ??= new List<MechanoidMechanitorRecord>();
                CleanupRecords();
                RebuildRecordIndex();
            }
        }

        public static void QueuePostSpawnInitialization(Pawn? pawn)
        {
            if (pawn == null || pawn.Destroyed)
            {
                return;
            }

            CurrentRegistry?.QueuePostSpawnInitializationInternal(pawn);
        }

        public override void GameComponentUpdate()
        {
            base.GameComponentUpdate();
            ProcessPendingMechanitorInitializations();
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            mechanitorRecords ??= new List<MechanoidMechanitorRecord>();
            CleanupRecords();
            RebuildRecordIndex();
            TryRepairMechanicalConsciousnessHost();
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            mechanitorRecords ??= new List<MechanoidMechanitorRecord>();
            CleanupRecords();
            RebuildRecordIndex();
            RestoreAcquiredRecordsAfterLoad();
            SynchronizeAcquiredMechanitorHediffs();
            SynchronizeNativeMechanitorHediffs();
            SynchronizeSelfWorkModeEffectsAfterLoad();
            TryRepairMechanicalConsciousnessHost();

            if (mechanicalConsciousnessHost != null
                && MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(
                    mechanicalConsciousnessHost))
            {
                FinalizeHostAssignment(mechanicalConsciousnessHost);
            }

            SynchronizeMechanicalConsciousnessHediff();
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
            GameComponent_MechanoidMechanitorFeatureManager.NotifyMechanitorRosterChanged();
        }

        private void RemoveRecordForPawnInternal(Pawn pawn)
        {
            bool removed = false;
            for (int i = mechanitorRecords.Count - 1; i >= 0; i--)
            {
                MechanoidMechanitorRecord? record = mechanitorRecords[i];
                if (record != null && ReferenceEquals(record.Pawn, pawn))
                {
                    mechanitorRecords.RemoveAt(i);
                    removed = true;
                }
            }

            bool removedFromIndex = recordByPawn.Remove(pawn);
            InvalidateDerivedCaches();
            if (removed || removedFromIndex)
            {
                GameComponent_MechanoidMechanitorFeatureManager.NotifyMechanitorRosterChanged();
            }
        }

        /// <summary>
        /// 移除空记录、空 Pawn 引用，以及已经永久 Discarded 的 Pawn。
        /// 死亡、位于尸体中、未生成、远行队中或暂时离图的 Pawn 仍保留记录。
        /// 不可用 Destroyed 判断：尸体中的 InnerPawn 同样处于 Destroyed，但仍可复活。
        /// 永久清理应使用 Discarded。
        /// </summary>
        private void CleanupRecords()
        {
            mechanitorRecords ??= new List<MechanoidMechanitorRecord>();
            int countBefore = mechanitorRecords.Count;

            for (int i = mechanitorRecords.Count - 1; i >= 0; i--)
            {
                MechanoidMechanitorRecord record = mechanitorRecords[i];
                if (record == null || record.Pawn == null || record.Pawn.Discarded)
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

            if (mechanitorRecords.Count != countBefore)
            {
                GameComponent_MechanoidMechanitorFeatureManager.NotifyMechanitorRosterChanged();
            }
        }

        private void RebuildRecordIndex()
        {
            int previousActiveCount = CountActiveRegisteredInRecords();

            recordByPawn = new Dictionary<Pawn, MechanoidMechanitorRecord>();
            for (int i = 0; i < mechanitorRecords.Count; i++)
            {
                MechanoidMechanitorRecord record = mechanitorRecords[i];
                Pawn? pawn = record.Pawn;
                // 排除永久 Discarded；尸体中的死亡 Pawn（Destroyed 但仍可复活）保留索引。
                if (pawn != null && !pawn.Discarded)
                {
                    recordByPawn[pawn] = record;
                }
            }

            InvalidateDerivedCaches();

            int newActiveCount = CountActiveRegisteredInRecords();
            if (previousActiveCount != newActiveCount)
            {
                GameComponent_MechanoidMechanitorFeatureManager.NotifyMechanitorRosterChanged();
            }
        }

        private int CountActiveRegisteredInRecords()
        {
            int count = 0;
            for (int i = 0; i < mechanitorRecords.Count; i++)
            {
                Pawn? pawn = mechanitorRecords[i]?.Pawn;
                if (pawn == null || pawn.Destroyed || pawn.Dead)
                {
                    continue;
                }

                count++;
            }

            return count;
        }

        private void InvalidateDerivedCaches()
        {
            registeredMechanitorsCache = null;
            registeredMechanitorsReadOnlyCache = null;
        }

        /// <summary>
        /// 活跃机械师缓存：排除 Dead / Destroyed（含尸体 InnerPawn）。
        /// 调试管理窗口必须改读持久化记录快照，不得仅依赖本缓存。
        /// </summary>
        private void EnsureRegisteredMechanitorsCache()
        {
            if (registeredMechanitorsReadOnlyCache != null)
            {
                return;
            }

            registeredMechanitorsCache = new List<Pawn>();
            for (int i = 0; i < mechanitorRecords.Count; i++)
            {
                Pawn? pawn = mechanitorRecords[i].Pawn;
                if (pawn != null && !pawn.Dead && !pawn.Destroyed)
                {
                    registeredMechanitorsCache.Add(pawn);
                }
            }

            registeredMechanitorsReadOnlyCache =
                new ReadOnlyCollection<Pawn>(registeredMechanitorsCache);
        }

        private void SynchronizeSelfWorkModeEffectsAfterLoad()
        {
            for (int i = 0; i < mechanitorRecords.Count; i++)
            {
                MechanoidMechanitorRecord record = mechanitorRecords[i];
                Pawn? pawn = record.Pawn;
                if (pawn == null || pawn.Destroyed)
                {
                    continue;
                }

                CompMechanoidMechanitorSelfWorkModeUser? workModeComp =
                    CompMechanoidMechanitorSelfWorkModeUser.GetFor(pawn);
                workModeComp?.SyncSelfWorkModeEffectsFromAuthoritativeState();

                if (record.Origin == MechanoidMechanitorOrigin.Acquired
                    && MechanoidMechanitorSelfWorkModeUtility.HasSelfWorkMode(pawn))
                {
                    MechanoidMechanitorSelfWorkModeUtility.ApplyAcquiredSelfWorkMode(
                        pawn,
                        MechanoidMechanitorSelfWorkModeUtility.SanitizeWorkMode(
                            record.SelfWorkMode));
                }
            }
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

                try
                {
                    AcquiredMechanitorStateUtility.EnsureAcquiredMechanitorState(
                        record.Pawn,
                        record);
                }
                catch (Exception ex)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 读档恢复后天机械族机械师状态失败：" +
                        $"pawn={record.Pawn.LabelShort}（{record.Pawn.ThingID}）：{ex}");
                }
            }
        }

        private void QueuePostSpawnInitializationInternal(Pawn pawn)
        {
            pendingMechanitorInitializations.Add(pawn);
        }

        private void ProcessPendingMechanitorInitializations()
        {
            if (pendingMechanitorInitializations.Count == 0)
            {
                return;
            }

            if (LongEventHandler.AnyEventNowOrWaiting)
            {
                return;
            }

            List<Pawn> pending = new List<Pawn>(pendingMechanitorInitializations);
            for (int i = 0; i < pending.Count; i++)
            {
                Pawn pawn = pending[i];
                pendingMechanitorInitializations.Remove(pawn);

                if (pawn == null
                    || pawn.Destroyed
                    || pawn.Dead
                    || !pawn.Spawned
                    || pawn.Map == null
                    || pawn.Faction == null
                    || !pawn.Faction.IsPlayerSafe()
                    || !MAPMechanitorNodeUtility.IsMechanitorNodeController(pawn))
                {
                    continue;
                }

                MAPMechanitorInitializationUtility.FinalizeNow(pawn);
            }
        }

        private void TryRepairMechanicalConsciousnessHost()
        {
            if (!MechanoidMechanitorScenarioUtility.IsScenarioActive)
            {
                mechanicalConsciousnessHost = null;
                return;
            }

            if (mechanicalConsciousnessHost != null)
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

            mechanicalConsciousnessHost = fallback;
        }

        private static HediffDef? GetMechanicalConsciousnessHediffDef()
        {
            return cachedMechanicalConsciousnessHediffDef ??=
                DefDatabase<HediffDef>.GetNamedSilentFail(MechanicalConsciousnessHediffDefName);
        }

        private static HediffDef? GetNativeMechanitorHediffDef()
        {
            return cachedNativeMechanitorHediffDef ??=
                DefDatabase<HediffDef>.GetNamedSilentFail(NativeMechanitorHediffDefName);
        }

        private void SynchronizeAcquiredMechanitorHediffs()
        {
            if (Current.Game == null)
            {
                return;
            }

            try
            {
                HediffDef? def = MechanoidMechanitorRoleUtility.GetAcquiredIdentityDef();
                if (def == null)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 后天机械族机械师健康状态同步失败：未找到 HediffDef " +
                        "MAP_AcquiredMechanoidMechanitor。");
                    return;
                }

                for (int i = 0; i < mechanitorRecords.Count; i++)
                {
                    MechanoidMechanitorRecord? record = mechanitorRecords[i];
                    if (record == null)
                    {
                        continue;
                    }

                    Pawn? pawn = record.Pawn;
                    if (pawn == null || pawn.Destroyed)
                    {
                        continue;
                    }

                    if (record.Origin == MechanoidMechanitorOrigin.Acquired)
                    {
                        EnsureSingleHediffOnPawn(pawn, def, "机械族机械师");
                    }
                    else
                    {
                        RemoveAllHediffsFromPawn(pawn, def, "机械族机械师");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 后天机械族机械师健康状态同步异常：" +
                    $"{ex}");
            }
        }

        private void SynchronizeNativeMechanitorHediffs()
        {
            if (Current.Game == null)
            {
                return;
            }

            try
            {
                HediffDef? def = GetNativeMechanitorHediffDef();
                if (def == null)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 先天机械族机械师健康状态同步失败：未找到 HediffDef " +
                        $"{NativeMechanitorHediffDefName}。");
                    return;
                }

                for (int i = 0; i < mechanitorRecords.Count; i++)
                {
                    MechanoidMechanitorRecord? record = mechanitorRecords[i];
                    if (record == null)
                    {
                        continue;
                    }

                    Pawn? pawn = record.Pawn;
                    if (pawn == null || pawn.Destroyed)
                    {
                        continue;
                    }

                    if (record.Origin == MechanoidMechanitorOrigin.Native)
                    {
                        EnsureSingleHediffOnPawn(pawn, def, "先天机械族机械师");
                    }
                    else
                    {
                        RemoveAllHediffsFromPawn(pawn, def, "先天机械族机械师");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 先天机械族机械师健康状态同步异常：" +
                    $"{ex}");
            }
        }

        private void SynchronizeMechanicalConsciousnessHediff()
        {
            if (Current.Game == null)
            {
                return;
            }

            try
            {
                HediffDef? def = GetMechanicalConsciousnessHediffDef();
                if (def == null)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 机械意识健康状态同步失败：未找到 HediffDef " +
                        $"{MechanicalConsciousnessHediffDefName}。");
                    return;
                }

                Pawn? host = mechanicalConsciousnessHost;
                if (host != null && !host.Destroyed)
                {
                    EnsureSingleHediffOnPawn(host, def, "机械意识");
                }

                for (int i = 0; i < mechanitorRecords.Count; i++)
                {
                    MechanoidMechanitorRecord? record = mechanitorRecords[i];
                    Pawn? pawn = record?.Pawn;
                    if (pawn == null || pawn.Destroyed)
                    {
                        continue;
                    }

                    if (ReferenceEquals(pawn, host))
                    {
                        EnsureSingleHediffOnPawn(pawn, def, "机械意识");
                    }
                    else
                    {
                        RemoveAllHediffsFromPawn(pawn, def, "机械意识");
                    }
                }
            }
            catch (Exception ex)
            {
                Pawn? host = mechanicalConsciousnessHost;
                Log.Error(
                    "[MAP-机械族机械师] 机械意识健康状态同步异常：" +
                    $"currentHost={host?.LabelShort ?? "null"}（{host?.ThingID ?? "null"}）：{ex}");
            }
        }

        private static void EnsureSingleHediffOnPawn(
            Pawn pawn,
            HediffDef def,
            string contextLabel)
        {
            if (pawn.health?.hediffSet == null)
            {
                return;
            }

            Hediff? keeper = null;
            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            for (int i = hediffs.Count - 1; i >= 0; i--)
            {
                Hediff? hediff = hediffs[i];
                if (hediff?.def != def)
                {
                    continue;
                }

                if (keeper == null)
                {
                    keeper = hediff;
                    continue;
                }

                try
                {
                    pawn.health.RemoveHediff(hediff);
                }
                catch (Exception ex)
                {
                    Log.Error(
                        $"[MAP-机械族机械师] 移除重复{contextLabel}健康状态失败：" +
                        $"pawn={pawn.LabelShort}（{pawn.ThingID}），" +
                        $"hediffDef={def.defName}：{ex}");
                }
            }

            if (keeper != null)
            {
                return;
            }

            try
            {
                pawn.health.AddHediff(def);
            }
            catch (Exception ex)
            {
                Log.Error(
                    $"[MAP-机械族机械师] 添加{contextLabel}健康状态失败：" +
                    $"pawn={pawn.LabelShort}（{pawn.ThingID}），" +
                    $"hediffDef={def.defName}：{ex}");
            }
        }

        private static void RemoveAllHediffsFromPawn(
            Pawn pawn,
            HediffDef def,
            string contextLabel)
        {
            if (pawn.health?.hediffSet == null)
            {
                return;
            }

            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            for (int i = hediffs.Count - 1; i >= 0; i--)
            {
                Hediff? hediff = hediffs[i];
                if (hediff?.def != def)
                {
                    continue;
                }

                try
                {
                    pawn.health.RemoveHediff(hediff);
                }
                catch (Exception ex)
                {
                    Log.Error(
                        $"[MAP-机械族机械师] 移除{contextLabel}健康状态失败：" +
                        $"pawn={pawn.LabelShort}（{pawn.ThingID}），" +
                        $"hediffDef={def.defName}：{ex}");
                }
            }
        }
    }
}
