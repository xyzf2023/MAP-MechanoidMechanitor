using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>自律授权的唯一事实来源。查询无副作用，身份同步与运行修复分别进行。</summary>
    public sealed class GameComponent_AutonomousMechRegistry : GameComponent
    {
        private List<AutonomousMechAuthorizationRecord> records = new List<AutonomousMechAuthorizationRecord>();
        private readonly Dictionary<Pawn, AutonomousMechAuthorizationRecord> byPawn =
            new Dictionary<Pawn, AutonomousMechAuthorizationRecord>();
        private readonly Dictionary<Pawn, bool> pendingRefresh = new Dictionary<Pawn, bool>();
        private readonly Dictionary<Pawn, HashSet<Pawn>> pendingOverseerEffectRefresh =
            new Dictionary<Pawn, HashSet<Pawn>>();
        // 旧档中节点 Comp 可能先于身份注册表恢复；仅对本次新建记录延后导入旧阈值。
        private readonly HashSet<AutonomousMechAuthorizationRecord> pendingLegacyRechargeImport =
            new HashSet<AutonomousMechAuthorizationRecord>();
        private bool refreshing;
        private bool pendingSettingsRefresh;

        private static GameComponent_AutonomousMechRegistry? CurrentRegistry =>
            CurrentGameComponentCache<GameComponent_AutonomousMechRegistry>.Get();

        public GameComponent_AutonomousMechRegistry(Game game) { }

        public static bool IsAuthorized(Pawn? pawn) =>
            ModsConfig.BiotechActive && pawn != null && !pawn.Destroyed && !pawn.Discarded
            && pawn.RaceProps?.IsMechanoid == true && pawn.OverseerSubject != null
            && TryGetRecord(pawn, out AutonomousMechAuthorizationRecord? record)
            && record!.Sources != AutonomousMechAuthorizationSource.None;

        /// <summary>持久记录查询，包含死亡、尸体和离图 Pawn，不代表当前可被玩家控制。</summary>
        public static bool TryGetRecord(Pawn? pawn, out AutonomousMechAuthorizationRecord? record)
        {
            record = null;
            GameComponent_AutonomousMechRegistry? registry = CurrentRegistry;
            if (pawn == null || pawn.Discarded || registry == null)
                return false;
            if (registry.byPawn.TryGetValue(pawn, out record))
                return true;
            // PostLoadInit 次序不保证索引已恢复；只读回退，不在状态 getter 中写入。
            if (Scribe.mode != LoadSaveMode.Inactive
                || MechanoidMechanitorPostLoadSafetyCoordinator.LoadInProgress)
                record = registry.FindPersistentRecord(pawn);
            return record != null;
        }

        public static bool HasAuthorizationRecord(Pawn? pawn) => TryGetRecord(pawn, out _);

        public static IReadOnlyList<AutonomousMechAuthorizationRecord> GetAuthorizationRecordSnapshot()
        {
            var result = new List<AutonomousMechAuthorizationRecord>();
            if (CurrentRegistry?.records != null)
            {
                foreach (AutonomousMechAuthorizationRecord record in CurrentRegistry.records)
                {
                    if (record?.Pawn != null && !record.Pawn.Discarded)
                        result.Add(record);
                }
            }
            return result;
        }

        /// <summary>仅增加独立来源；重复授权返回 false，且不重复执行运行修复。</summary>
        public static bool TryAuthorize(Pawn? pawn)
        {
            if (!AutonomousMechUtility.CanReceiveAuthorization(pawn) || CurrentRegistry == null)
                return false;
            CurrentRegistry.SynchronizePawn(pawn!);
            AutonomousMechAuthorizationRecord record = CurrentRegistry.GetOrCreate(pawn!);
            if (record.HasIndependentAuthorization)
                return false;
            bool wasAuthorized = record.Sources != AutonomousMechAuthorizationSource.None;
            record.SetSources(record.Sources | AutonomousMechAuthorizationSource.Independent);
            CurrentRegistry.NotifyChanged(pawn!, !wasAuthorized);
            CurrentRegistry.FlushPendingRefreshes();
            return true;
        }

        /// <summary>只撤销独立来源；身份、先天组件和静态节点提供的资格不受影响。</summary>
        public static bool TryRevokeAuthorization(Pawn? pawn)
        {
            if (pawn == null || CurrentRegistry == null)
                return false;
            CurrentRegistry.SynchronizePawn(pawn);
            if (!TryGetRecord(pawn, out AutonomousMechAuthorizationRecord? record)
                || !record!.HasIndependentAuthorization)
                return false;
            record.SetSources(record.Sources & ~AutonomousMechAuthorizationSource.Independent);
            bool lostAuthorization = record.Sources == AutonomousMechAuthorizationSource.None;
            if (lostAuthorization && pawn.GetComp<CompAutonomousMech>()?.IsSettingsControlled != true)
                CurrentRegistry.Remove(record);
            CurrentRegistry.NotifyChanged(pawn, lostAuthorization);
            CurrentRegistry.FlushPendingRefreshes();
            return true;
        }

        public static bool TrySetRechargeThresholds(Pawn? pawn, FloatRange thresholds)
        {
            if (pawn == null || pawn.Destroyed
                || !TryGetRecord(pawn, out AutonomousMechAuthorizationRecord? record))
                return false;
            record!.SetRechargeThresholds(thresholds);
            return true;
        }

        // 保留原公共入口，旧调用方仍可明确选择休眠或自律指令。
        public static bool TrySetSelfShutdown(Pawn? pawn, bool selfShutdown) =>
            TrySetBehaviorMode(pawn, selfShutdown ? MechWorkModeDefOf.SelfShutdown : null);

        public static bool TrySetBehaviorMode(Pawn? pawn, MechWorkModeDef? mode)
        {
            MechWorkModeDef sanitized = MechanoidMechanitorSelfWorkModeUtility.SanitizeWorkMode(mode);
            if (!AutonomousMechUtility.CanReceiveAuthorization(pawn)
                || !AutonomousMechUtility.IsPlayerAutonomousMech(pawn)
                || MechanoidMechanitorSelfWorkModeUtility.HasSelfWorkMode(pawn)
                || !TryGetRecord(pawn, out AutonomousMechAuthorizationRecord? record)
                || record!.BehaviorMode == sanitized)
                return false;
            record.SetBehaviorMode(sanitized);
            MechanoidMechanitorSelfWorkModeUtility.SyncSelfWorkModeEffects(pawn);
            MechanoidMechanitorSelfWorkModeUtility.NotifyModeChanged(pawn!, sanitized);
            return true;
        }

        internal static void SynchronizeAutomaticSources(Pawn? pawn)
        {
            if (pawn != null && !pawn.Discarded)
                CurrentRegistry?.SynchronizePawn(pawn);
        }

        /// <summary>设置事件只排队；下一次安全 Update 统一同步已有个体，暂停游戏时也生效。</summary>
        internal static void NotifySettingsChanged()
        {
            if (CurrentRegistry != null)
                CurrentRegistry.pendingSettingsRefresh = true;
        }

        /// <summary>机械师注册表结构改变时同步，包含被删除身份的旧授权记录。</summary>
        internal static void SynchronizeMechanitorSources()
        {
            GameComponent_AutonomousMechRegistry? registry = CurrentRegistry;
            if (registry == null)
                return;
            var candidates = new HashSet<Pawn>();
            foreach (var entry in GameComponent_MechanoidMechanitorRegistry.GetPersistentRecordSnapshot())
                candidates.Add(entry.Pawn);
            if (registry.records != null)
                foreach (var record in registry.records)
                    if (record?.Pawn != null) candidates.Add(record.Pawn);
            foreach (Pawn pawn in candidates)
                registry.SynchronizePawn(pawn);
        }

        internal static void NotifyPawnLifecycle(Pawn pawn)
        {
            SynchronizeAutomaticSources(pawn);
            if (HasAuthorizationRecord(pawn))
                CurrentRegistry?.QueueRefresh(pawn, false);
        }

        private void SynchronizePawn(Pawn pawn)
        {
            if (pawn.Discarded)
                return;
            AutonomousMechAuthorizationRecord? record = FindPersistentRecord(pawn);
            CompAutonomousMech? innateComp = pawn.GetComp<CompAutonomousMech>();
            bool retainSettings = innateComp?.IsSettingsControlled == true
                && pawn.RaceProps?.IsMechanoid == true && pawn.OverseerSubject != null;
            // 关闭先天来源时保留个人阈值/模式及离图引用；空来源记录不代表自律资格。
            if (record == null && retainSettings)
                record = GetOrCreate(pawn);
            if (record != null && pendingLegacyRechargeImport.Contains(record)
                && GameComponent_MechanoidMechanitorRegistry.TryGetPersistentRecord(pawn,
                    out MechanoidMechanitorRecord? legacy))
            {
                record.SetRechargeThresholds(legacy!.RechargeThresholds);
                pendingLegacyRechargeImport.Remove(record);
            }
            AutonomousMechAuthorizationSource sources =
                (record?.Sources ?? AutonomousMechAuthorizationSource.None)
                & AutonomousMechAuthorizationSource.Independent;
            // 其他 GameComponent 尚未完成恢复时，只补来源，不据暂时缺失的身份删记录。
            if (Scribe.mode != LoadSaveMode.Inactive
                || MechanoidMechanitorPostLoadSafetyCoordinator.ShouldDeferPositiveRestore)
                sources |= record?.Sources ?? AutonomousMechAuthorizationSource.None;
            if (pawn.RaceProps?.IsMechanoid == true && pawn.OverseerSubject != null)
            {
                if (innateComp?.ProvidesAuthorization == true)
                    sources |= AutonomousMechAuthorizationSource.InnateComp;
                if (GameComponent_MechanoidMechanitorRegistry.TryGetPersistentRecord(pawn, out _))
                    sources |= AutonomousMechAuthorizationSource.MechanitorIdentity;
                CompProperties_MAPMechanitorNode? props = pawn.GetComp<CompMAPMechanitorNode>()?.NodeProps;
                if (props?.controlBackend == MAPMechanitorControlBackend.Vanilla
                    && props.requiresExternalOverseer == false)
                    sources |= AutonomousMechAuthorizationSource.LegacyNode;
            }
            AutonomousMechAuthorizationSource previous = record?.Sources ?? AutonomousMechAuthorizationSource.None;
            if (previous == sources)
            {
                if (record != null && sources == AutonomousMechAuthorizationSource.None && !retainSettings)
                    Remove(record);
                return;
            }
            if (sources == AutonomousMechAuthorizationSource.None && !retainSettings)
            {
                if (record != null) Remove(record);
            }
            else
            {
                (record ?? GetOrCreate(pawn)).SetSources(sources);
            }
            NotifyChanged(pawn, previous == AutonomousMechAuthorizationSource.None
                || sources == AutonomousMechAuthorizationSource.None);
        }

        private AutonomousMechAuthorizationRecord GetOrCreate(Pawn pawn)
        {
            records ??= new List<AutonomousMechAuthorizationRecord>();
            AutonomousMechAuthorizationRecord? existing = FindPersistentRecord(pawn);
            if (existing != null)
            {
                byPawn[pawn] = existing;
                return existing;
            }
            FloatRange thresholds = MechanitorControlGroup.DefaultMechRechargeThresholds;
            // 只在首次创建时导入旧字段；不能覆盖独立授权后调整的个人设置。
            bool hasLegacy = GameComponent_MechanoidMechanitorRegistry.TryGetPersistentRecord(pawn,
                out MechanoidMechanitorRecord? legacy);
            if (hasLegacy)
                thresholds = legacy!.RechargeThresholds;
            var record = new AutonomousMechAuthorizationRecord(pawn, thresholds);
            if (!hasLegacy && (Scribe.mode != LoadSaveMode.Inactive
                || MechanoidMechanitorPostLoadSafetyCoordinator.LoadInProgress))
                pendingLegacyRechargeImport.Add(record);
            records.Add(record);
            byPawn[pawn] = record;
            return record;
        }

        private AutonomousMechAuthorizationRecord? FindPersistentRecord(Pawn pawn)
        {
            if (byPawn.TryGetValue(pawn, out AutonomousMechAuthorizationRecord? record))
                return record;
            if (records != null)
                foreach (AutonomousMechAuthorizationRecord candidate in records)
                    if (candidate != null && ReferenceEquals(candidate.Pawn, pawn)) return candidate;
            return null;
        }

        private void Remove(AutonomousMechAuthorizationRecord record)
        {
            records.Remove(record);
            pendingLegacyRechargeImport.Remove(record);
            if (record.Pawn != null) byPawn.Remove(record.Pawn);
        }

        private void NotifyChanged(Pawn pawn, bool eligibilityChanged)
        {
            MAPMechanitorNodeUtility.InvalidateVanillaControlNodeProfileCache();
            if (eligibilityChanged)
                QueueRefresh(pawn, !MechanoidMechanitorPostLoadSafetyCoordinator.LoadInProgress
                    && Scribe.mode == LoadSaveMode.Inactive);
        }

        private void QueueRefresh(Pawn pawn, bool reevaluateJobs)
        {
            pendingRefresh.TryGetValue(pawn, out bool previous);
            pendingRefresh[pawn] = previous || reevaluateJobs;
        }

        internal static void QueueOverseerEffectRefresh(Pawn pawn, IEnumerable<Pawn> formerOverseers)
        {
            GameComponent_AutonomousMechRegistry? registry = CurrentRegistry;
            if (registry == null) return;
            if (!registry.pendingOverseerEffectRefresh.TryGetValue(pawn, out HashSet<Pawn>? providers))
            {
                providers = new HashSet<Pawn>();
                registry.pendingOverseerEffectRefresh.Add(pawn, providers);
            }
            providers.UnionWith(formerOverseers);
            registry.QueueRefresh(pawn, false);
        }

        internal static bool RefreshPendingOverseerEffects(Pawn pawn, bool reevaluateJobs)
        {
            GameComponent_AutonomousMechRegistry? registry = CurrentRegistry;
            if (registry == null
                || !registry.pendingOverseerEffectRefresh.TryGetValue(pawn, out HashSet<Pawn>? formerOverseers))
                return true;
            // 成功后再移除：关系已解除后发生异常，重试仍能找到旧增益提供者。
            if (AutonomousMechEffectUtility.RefreshAfterOverseerChange(pawn, formerOverseers))
            {
                registry.pendingOverseerEffectRefresh.Remove(pawn);
                return true;
            }
            registry.QueueRefresh(pawn, reevaluateJobs);
            return false;
        }

        /// <summary>由安全读档协调器调用。只处理事件积累的 Pawn，不扫描全体机械体。</summary>
        internal static void ApplyDeferredRuntimeRefreshes()
        {
            SynchronizeMechanitorSources();
            CurrentRegistry?.pendingLegacyRechargeImport.Clear();
            CurrentRegistry?.FlushPendingRefreshes(allowLoadCoordinator: true);
        }

        private void FlushPendingRefreshes(bool allowLoadCoordinator = false)
        {
            if (refreshing || pendingRefresh.Count == 0 || Scribe.mode != LoadSaveMode.Inactive
                || (!allowLoadCoordinator && MechanoidMechanitorPostLoadSafetyCoordinator.LoadInProgress)
                || LongEventHandler.AnyEventNowOrWaiting || Current.ProgramState != ProgramState.Playing)
                return;
            refreshing = true;
            try
            {
                var pending = new List<KeyValuePair<Pawn, bool>>(pendingRefresh);
                foreach (var entry in pending)
                {
                    pendingRefresh.Remove(entry.Key);
                    try
                    {
                        AutonomousMechUtility.RefreshRuntime(entry.Key, entry.Value && !allowLoadCoordinator);
                        // 不再具备运行条件的个体可能提前返回；清退死亡/销毁目标的待处理事件。
                        if (!CanRefreshLivingPawn(entry.Key))
                            RefreshPendingOverseerEffects(entry.Key, entry.Value && !allowLoadCoordinator);
                    }
                    catch
                    {
                        // 交给读档协调器或游戏的异常处理；失败项目不能被静默丢弃。
                        QueueRefresh(entry.Key, entry.Value);
                        throw;
                    }
                }
            }
            finally { refreshing = false; }
        }

        private static bool CanRefreshLivingPawn(Pawn pawn) =>
            !pawn.Dead && !pawn.Destroyed && !pawn.Discarded;

        public override void GameComponentUpdate()
        {
            base.GameComponentUpdate();
            if (pendingSettingsRefresh && Scribe.mode == LoadSaveMode.Inactive
                && !MechanoidMechanitorPostLoadSafetyCoordinator.LoadInProgress
                && !LongEventHandler.AnyEventNowOrWaiting && Current.ProgramState == ProgramState.Playing)
            {
                SynchronizeKnownPawns(innateOnly: true);
                pendingSettingsRefresh = false;
            }
            FlushPendingRefreshes();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving)
                RebuildIndex();
            Scribe_Collections.Look(ref records, "autonomousMechAuthorizationRecords", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                RebuildIndex();
                SynchronizeMechanitorSources();
                foreach (var record in records)
                    if (record.Pawn != null) QueueRefresh(record.Pawn, false);
            }
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            ReconcileGame();
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            ReconcileGame();
        }

        private void ReconcileGame()
        {
            RebuildIndex();
            SynchronizeMechanitorSources();
            SynchronizeKnownPawns();
            foreach (var record in records)
                if (record.Pawn != null) QueueRefresh(record.Pawn, false);
        }

        private void SynchronizeKnownPawns(bool innateOnly = false)
        {
            // 仅开局、读档、设置变更时扫描；包括容器、远行队与持久记录中的离图机体。
            var candidates = new HashSet<Pawn>(PawnsFinder.All_AliveOrDead);
            foreach (AutonomousMechAuthorizationRecord record in records)
                if (record.Pawn != null) candidates.Add(record.Pawn);
            foreach (Pawn pawn in candidates)
                if (!innateOnly || pawn.GetComp<CompAutonomousMech>() != null)
                    SynchronizePawn(pawn);
        }

        private void RebuildIndex()
        {
            records ??= new List<AutonomousMechAuthorizationRecord>();
            byPawn.Clear();
            for (int i = 0; i < records.Count; i++)
            {
                AutonomousMechAuthorizationRecord? record = records[i];
                Pawn? pawn = record?.Pawn;
                if (pawn == null || pawn.Discarded)
                {
                    records.RemoveAt(i--);
                    continue;
                }
                if (byPawn.TryGetValue(pawn, out AutonomousMechAuthorizationRecord? first))
                {
                    first.SetSources(first.Sources | record!.Sources);
                    records.RemoveAt(i--);
                }
                else byPawn.Add(pawn, record!);
            }
            MAPMechanitorNodeUtility.InvalidateVanillaControlNodeProfileCache();
            pendingLegacyRechargeImport.RemoveWhere(record => !records.Contains(record));
        }
    }
}
