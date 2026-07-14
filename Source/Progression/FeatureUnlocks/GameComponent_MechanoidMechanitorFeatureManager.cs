using System;
using System.Collections.Generic;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class GameComponent_MechanoidMechanitorFeatureManager : GameComponent
    {
        private const int UnlockStateSafetyCheckIntervalTicks = 60000;
        private const int UnlockLetterRetryIntervalTicks = 60;

        private const int ForcedSyncShortRetryIntervalTicks = 60;
        private const int ForcedSyncMediumRetryIntervalTicks = 600;
        private const int ForcedSyncLongRetryIntervalTicks = 60000;
        private const int ForcedSyncShortStageMaxFailures = 3;
        private const int ForcedSyncMediumStageMaxFailures = 6;

        private const int DynamicConsciousnessRefreshShortRetryIntervalTicks = 60;
        private const int DynamicConsciousnessRefreshMediumRetryIntervalTicks = 600;
        private const int DynamicConsciousnessRefreshLongRetryIntervalTicks = 60000;
        private const int DynamicConsciousnessRefreshShortStageMaxFailures = 3;
        private const int DynamicConsciousnessRefreshMediumStageMaxFailures = 6;

        private const int PendingPawnSyncShortRetryIntervalTicks = 60;
        private const int PendingPawnSyncMediumRetryIntervalTicks = 600;
        private const int PendingPawnSyncLongRetryIntervalTicks = 60000;
        private const int PendingPawnSyncShortStageMaxFailures = 3;
        private const int PendingPawnSyncMediumStageMaxFailures = 6;

        private const string AbilityUnlockLetterTitleKey =
            "MAP_MechanoidMechanitor.AbilityUnlockLetter.Title";
        private const string AbilityUnlockLetterSpecialScenarioTextKey =
            "MAP_MechanoidMechanitor.AbilityUnlockLetter.SpecialScenarioText";
        private const string AbilityUnlockLetterJusticeOnlyTextKey =
            "MAP_MechanoidMechanitor.AbilityUnlockLetter.JusticeOnlyText";
        private const string FeatureUnlockLetterTitleKey =
            "MAP_MechanoidMechanitor.FeatureUnlockLetter.Title";
        private const string FeatureUnlockLetterTextKey =
            "MAP_MechanoidMechanitor.FeatureUnlockLetter.Text";

        private bool pendingForcedSync;
        private bool pendingDynamicConsciousnessRefresh;
        private readonly HashSet<Pawn> pendingPawnSyncs = new HashSet<Pawn>();
        private readonly HashSet<string> pendingUnlockLetterIds = new HashSet<string>();
        private readonly HashSet<string> pendingFeatureUnlockLetterIds = new HashSet<string>();
        private Dictionary<string, bool>? lastKnownUnlockStates;
        private Dictionary<string, bool>? lastKnownFeatureUnlockStates;
        private int nextUnlockStateSafetyCheckTick;
        private int nextForcedSyncAttemptTick;
        private int nextUnlockLetterAttemptTick;
        private int forcedSyncFailureCount;
        private string? lastForcedSyncFailureSignature;
        private int dynamicConsciousnessRefreshFailureCount;
        private int nextDynamicConsciousnessRefreshAttemptTick;
        private int pendingPawnSyncFailureCount;
        private int nextPendingPawnSyncAttemptTick;

        public GameComponent_MechanoidMechanitorFeatureManager(Game game)
        {
        }

        private static GameComponent_MechanoidMechanitorFeatureManager? CurrentManager
        {
            get
            {
                if (Current.Game == null)
                {
                    return null;
                }

                return Current.Game
                    .GetComponent<GameComponent_MechanoidMechanitorFeatureManager>();
            }
        }

        public static void NotifyResearchProjectFinished()
        {
            CurrentManager?.OnResearchProjectFinished();
        }

        /// <summary>
        /// 机械族机械师注册数量可能变化时调用。仅设置 pending，由 GameComponentUpdate 在安全时合并刷新。
        /// </summary>
        public static void NotifyMechanitorRosterChanged()
        {
            GameComponent_MechanoidMechanitorFeatureManager? manager = CurrentManager;
            if (manager == null)
            {
                return;
            }

            // 新 pending：允许立即处理。已有 pending（含失败退避中）：保留重试时间，避免连续通知绕过节流。
            if (!manager.pendingDynamicConsciousnessRefresh)
            {
                manager.nextDynamicConsciousnessRefreshAttemptTick = 0;
            }

            manager.pendingDynamicConsciousnessRefresh = true;
        }

        public static void NotifyMechanitorInitialized(Pawn? pawn)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead)
            {
                return;
            }

            GameComponent_MechanoidMechanitorFeatureManager? manager = CurrentManager;
            if (manager == null)
            {
                return;
            }

            if (manager.IsSyncEnvironmentSafe())
            {
                try
                {
                    ManagedResearchAbilitySyncUtility.SyncPawn(pawn);
                }
                catch (Exception ex)
                {
                    // 立即同步失败：并入 pending，不向初始化调用链传播异常。
                    manager.pendingPawnSyncs.Add(pawn);
                    TickManager? tickManager = Find.TickManager;
                    int ticksGame = tickManager?.TicksGame ?? 0;
                    manager.SchedulePendingPawnSyncRetry(ticksGame);
                    Log.Error(
                        "[MAP-机械族机械师] 机械师初始化时科研能力同步失败，已加入待处理队列并进入分级退避重试：" +
                        $"pawn={pawn.LabelShort}（{pawn.ThingID}）：{ex}");
                }

                return;
            }

            // 环境暂不安全：入队但不计为同步失败。
            manager.EnqueuePendingPawnSync(pawn);
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            CaptureAbilityUnlockStates();
            CaptureFeatureUnlockStates();
            pendingForcedSync = true;
            nextUnlockLetterAttemptTick = 0;
            ResetForcedSyncRetryState();
            ResetDynamicConsciousnessRefreshRetryState();
            ResetPendingPawnSyncRetryState();
            ScheduleNextUnlockStateSafetyCheck();
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            CaptureAbilityUnlockStates();
            CaptureFeatureUnlockStates();
            pendingForcedSync = true;
            nextUnlockLetterAttemptTick = 0;
            ResetForcedSyncRetryState();
            ResetDynamicConsciousnessRefreshRetryState();
            ResetPendingPawnSyncRetryState();
            ScheduleNextUnlockStateSafetyCheck();
        }

        public override void GameComponentUpdate()
        {
            base.GameComponentUpdate();
            if (!IsSyncEnvironmentSafe())
            {
                return;
            }

            if (pendingForcedSync)
            {
                TryProcessPendingForcedSync();
                return;
            }

            ProcessPendingPawnSyncs();
            ProcessPendingDynamicConsciousnessRefresh();
            TryProcessPendingUnlockLetters();
            TryRunUnlockStateSafetyCheck();
        }

        private void TryProcessPendingForcedSync()
        {
            TickManager? tickManager = Find.TickManager;
            int ticksGame = tickManager?.TicksGame ?? 0;
            if (ticksGame < nextForcedSyncAttemptTick)
            {
                return;
            }

            if (TryPerformForcedFullSync())
            {
                pendingForcedSync = false;
                pendingPawnSyncs.Clear();
                ScheduleNextUnlockStateSafetyCheck();
                SendPendingUnlockLetters();
                SchedulePendingUnlockLetterRetryIfNeeded();
            }

            // 失败时由 TryPerformForcedFullSync 内统一计数、排程与日志，此处不再重复处理。
        }

        private bool TryPerformForcedFullSync()
        {
            try
            {
                CaptureAbilityUnlockStates();
                CaptureFeatureUnlockStates();
                ManagedResearchAbilitySyncUtility.SyncAllRelevantPawns();

                // 全量同步前清除 pending；失败时恢复原 pending。同步过程中新置位的请求予以保留。
                bool hadPendingConsciousnessRefresh = pendingDynamicConsciousnessRefresh;
                pendingDynamicConsciousnessRefresh = false;
                if (!ManagedResearchFeatureSyncUtility.SyncAllFeatures())
                {
                    if (hadPendingConsciousnessRefresh)
                    {
                        pendingDynamicConsciousnessRefresh = true;
                    }

                    throw new InvalidOperationException(
                        "一个或多个受管理特性同步失败。");
                }

                if (forcedSyncFailureCount > 0)
                {
                    Log.Message(
                        "[MAP-机械族机械师] 受管理科研能力/特性全量同步已恢复。");
                }

                ResetForcedSyncRetryState();
                // SyncAllFeatures 已含动态意识刷新；仅清除失败退避状态，不覆盖同步中新产生的 pending。
                ResetDynamicConsciousnessRefreshRetryState();
                // SyncAllRelevantPawns 已同步相关能力；仅清除单 Pawn pending 退避状态，不清空集合。
                ResetPendingPawnSyncRetryState();
                return true;
            }
            catch (Exception ex)
            {
                TickManager? tickManager = Find.TickManager;
                int ticksGame = tickManager?.TicksGame ?? 0;
                ScheduleForcedSyncRetry(ticksGame, ex);
                return false;
            }
        }

        private void ResetForcedSyncRetryState()
        {
            forcedSyncFailureCount = 0;
            nextForcedSyncAttemptTick = 0;
            lastForcedSyncFailureSignature = null;
        }

        private void ScheduleForcedSyncRetry(int currentTick, Exception exception)
        {
            forcedSyncFailureCount++;
            pendingForcedSync = true;

            int retryIntervalTicks;
            if (forcedSyncFailureCount <= ForcedSyncShortStageMaxFailures)
            {
                retryIntervalTicks = ForcedSyncShortRetryIntervalTicks;
            }
            else if (forcedSyncFailureCount <= ForcedSyncMediumStageMaxFailures)
            {
                retryIntervalTicks = ForcedSyncMediumRetryIntervalTicks;
            }
            else
            {
                retryIntervalTicks = ForcedSyncLongRetryIntervalTicks;
            }

            nextForcedSyncAttemptTick = currentTick + retryIntervalTicks;
            LogForcedSyncFailure(exception, retryIntervalTicks);
        }

        private static string BuildForcedSyncFailureSignature(Exception exception)
        {
            Exception root = exception;
            while (root.InnerException != null)
            {
                root = root.InnerException;
            }

            return root.GetType().FullName + ": " + (root.Message ?? string.Empty);
        }

        private void LogForcedSyncFailure(Exception exception, int retryIntervalTicks)
        {
            string signature = BuildForcedSyncFailureSignature(exception);
            if (forcedSyncFailureCount == 1
                || !string.Equals(
                    lastForcedSyncFailureSignature, signature, StringComparison.Ordinal))
            {
                Log.Error(
                    "[MAP-机械族机械师] 受管理科研能力/特性全量同步失败，已进入分级退避重试："
                    + exception);
                lastForcedSyncFailureSignature = signature;
            }

            if (forcedSyncFailureCount == ForcedSyncShortStageMaxFailures + 1)
            {
                Log.Warning(
                    "[MAP-机械族机械师] 强制全量同步已连续失败 " +
                    $"{forcedSyncFailureCount} 次，进入中期退避，下次重试间隔 {retryIntervalTicks} tick。");
            }
            else if (forcedSyncFailureCount == ForcedSyncMediumStageMaxFailures + 1)
            {
                Log.Warning(
                    "[MAP-机械族机械师] 强制全量同步已连续失败 " +
                    $"{forcedSyncFailureCount} 次，进入长期退避，下次重试间隔 {retryIntervalTicks} tick。");
            }
        }

        private void ProcessPendingPawnSyncs()
        {
            if (pendingPawnSyncs.Count == 0)
            {
                ResetPendingPawnSyncRetryState();
                return;
            }

            TickManager? tickManager = Find.TickManager;
            if (tickManager == null)
            {
                // TickManager 暂不可用：保留整个 pending 集合，不视为失败。
                return;
            }

            int ticksGame = tickManager.TicksGame;
            if (ticksGame < nextPendingPawnSyncAttemptTick)
            {
                return;
            }

            // 先复制再清空：同步过程中的新请求可留在原集合。
            List<Pawn> toSync = new List<Pawn>(pendingPawnSyncs);
            pendingPawnSyncs.Clear();

            bool anySyncFailed = false;
            for (int i = 0; i < toSync.Count; i++)
            {
                Pawn pawn = toSync[i];
                if (pawn == null || pawn.Destroyed || pawn.Dead)
                {
                    continue;
                }

                try
                {
                    ManagedResearchAbilitySyncUtility.SyncPawn(pawn);
                }
                catch (Exception ex)
                {
                    // 失败回填直接 Add，避免经入队辅助方法因“集合为空”而重置连续失败次数。
                    pendingPawnSyncs.Add(pawn);
                    anySyncFailed = true;
                    Log.Error(
                        "[MAP-机械族机械师] 待处理 Pawn 同步异常：" +
                        $"pawn={pawn.LabelShort}（{pawn.ThingID}）：{ex}");
                }
            }

            if (anySyncFailed)
            {
                SchedulePendingPawnSyncRetry(ticksGame);
            }
            else
            {
                ResetPendingPawnSyncRetryState();
            }
        }

        /// <summary>
        /// 环境不安全时的入队。集合从空变为非空时清除旧退避，使其可立即处理。
        /// 失败回填不要调用本方法。
        /// </summary>
        private void EnqueuePendingPawnSync(Pawn pawn)
        {
            bool wasEmpty = pendingPawnSyncs.Count == 0;
            pendingPawnSyncs.Add(pawn);
            if (wasEmpty)
            {
                ResetPendingPawnSyncRetryState();
            }
        }

        private void ResetPendingPawnSyncRetryState()
        {
            pendingPawnSyncFailureCount = 0;
            nextPendingPawnSyncAttemptTick = 0;
        }

        private void SchedulePendingPawnSyncRetry(int currentTick)
        {
            pendingPawnSyncFailureCount++;
            int retryIntervalTicks;
            if (pendingPawnSyncFailureCount <= PendingPawnSyncShortStageMaxFailures)
            {
                retryIntervalTicks = PendingPawnSyncShortRetryIntervalTicks;
            }
            else if (pendingPawnSyncFailureCount <= PendingPawnSyncMediumStageMaxFailures)
            {
                retryIntervalTicks = PendingPawnSyncMediumRetryIntervalTicks;
            }
            else
            {
                retryIntervalTicks = PendingPawnSyncLongRetryIntervalTicks;
            }

            nextPendingPawnSyncAttemptTick = currentTick + retryIntervalTicks;
        }

        private void TryRunUnlockStateSafetyCheck()
        {
            TickManager? tickManager = Find.TickManager;
            if (tickManager == null)
            {
                return;
            }

            int ticksGame = tickManager.TicksGame;
            if (ticksGame < nextUnlockStateSafetyCheckTick)
            {
                return;
            }

            nextUnlockStateSafetyCheckTick =
                ticksGame + UnlockStateSafetyCheckIntervalTicks;

            if (!HasAbilityUnlockStateChanged() && !HasFeatureUnlockStateChanged())
            {
                return;
            }

            if (TryPerformForcedFullSync())
            {
                pendingPawnSyncs.Clear();
                SendPendingUnlockLetters();
                SchedulePendingUnlockLetterRetryIfNeeded();
            }

            // 失败时由 TryPerformForcedFullSync 内统一计数与排程。
        }

        private void OnResearchProjectFinished()
        {
            if (Current.ProgramState != ProgramState.Playing)
            {
                pendingForcedSync = true;
                return;
            }

            EnqueueNewlyUnlockedAbilityLetterIds();
            EnqueueNewlyUnlockedFeatureLetterIds();

            if (!IsSyncEnvironmentSafe())
            {
                pendingForcedSync = true;
                return;
            }

            if (!HasAbilityUnlockStateChanged()
                && !HasFeatureUnlockStateChanged()
                && pendingUnlockLetterIds.Count == 0
                && pendingFeatureUnlockLetterIds.Count == 0)
            {
                return;
            }

            // 已在失败退避或排队中：保留 pending 与重试时间，信件已入队，由统一入口执行。
            if (pendingForcedSync)
            {
                return;
            }

            if (TryPerformForcedFullSync())
            {
                pendingPawnSyncs.Clear();
                ScheduleNextUnlockStateSafetyCheck();
                SendPendingUnlockLetters();
                SchedulePendingUnlockLetterRetryIfNeeded();
            }

            // 失败时由 TryPerformForcedFullSync 内统一计数与排程。
        }

        /// <summary>
        /// 处理机械师名册变化引起的动态意识合并刷新请求。
        /// 失败时按连续失败次数分级退避，避免每个 Unity 更新帧立即重试。
        /// </summary>
        private void ProcessPendingDynamicConsciousnessRefresh()
        {
            if (!pendingDynamicConsciousnessRefresh)
            {
                return;
            }

            TickManager? tickManager = Find.TickManager;
            if (tickManager == null)
            {
                // TickManager 暂不可用：不视为同步失败，保留 pending，不写错误日志。
                return;
            }

            int ticksGame = tickManager.TicksGame;
            if (ticksGame < nextDynamicConsciousnessRefreshAttemptTick)
            {
                return;
            }

            // 先清除再同步：同步过程中若死亡/名册变化再次置位，可保留到下一次处理。
            pendingDynamicConsciousnessRefresh = false;
            try
            {
                if (ManagedResearchFeatureSyncUtility.SyncDynamicConsciousnessBonuses())
                {
                    ResetDynamicConsciousnessRefreshRetryState();
                    return;
                }

                // 底层已记录具体失败 Pawn，此处不再写泛化错误。
                ScheduleDynamicConsciousnessRefreshRetry(ticksGame);
            }
            catch (Exception ex)
            {
                ScheduleDynamicConsciousnessRefreshRetry(ticksGame);
                Log.Error(
                    "[MAP-机械族机械师] 机械师名册变化后的动态意识加成刷新失败，已进入分级退避重试："
                    + ex);
            }
        }

        private void ResetDynamicConsciousnessRefreshRetryState()
        {
            dynamicConsciousnessRefreshFailureCount = 0;
            nextDynamicConsciousnessRefreshAttemptTick = 0;
        }

        private void ScheduleDynamicConsciousnessRefreshRetry(int currentTick)
        {
            dynamicConsciousnessRefreshFailureCount++;
            int retryIntervalTicks;
            if (dynamicConsciousnessRefreshFailureCount
                <= DynamicConsciousnessRefreshShortStageMaxFailures)
            {
                retryIntervalTicks = DynamicConsciousnessRefreshShortRetryIntervalTicks;
            }
            else if (dynamicConsciousnessRefreshFailureCount
                <= DynamicConsciousnessRefreshMediumStageMaxFailures)
            {
                retryIntervalTicks = DynamicConsciousnessRefreshMediumRetryIntervalTicks;
            }
            else
            {
                retryIntervalTicks = DynamicConsciousnessRefreshLongRetryIntervalTicks;
            }

            nextDynamicConsciousnessRefreshAttemptTick = currentTick + retryIntervalTicks;
            pendingDynamicConsciousnessRefresh = true;
        }

        private List<ManagedResearchAbilityDescriptor> CollectNewlyUnlockedAbilities()
        {
            List<ManagedResearchAbilityDescriptor> newlyUnlocked =
                new List<ManagedResearchAbilityDescriptor>();
            lastKnownUnlockStates ??= new Dictionary<string, bool>();

            IReadOnlyList<ManagedResearchAbilityDescriptor> all =
                ManagedResearchAbilityCatalog.All;
            for (int i = 0; i < all.Count; i++)
            {
                ManagedResearchAbilityDescriptor descriptor = all[i];
                bool current = ResearchFeatureUnlockUtility.IsAbilityUnlocked(descriptor);
                if (!lastKnownUnlockStates.TryGetValue(descriptor.Id, out bool previous))
                {
                    continue;
                }

                if (!previous && current)
                {
                    newlyUnlocked.Add(descriptor);
                }
            }

            return newlyUnlocked;
        }

        private List<ManagedResearchFeatureDescriptor> CollectNewlyUnlockedFeatures()
        {
            List<ManagedResearchFeatureDescriptor> newlyUnlocked =
                new List<ManagedResearchFeatureDescriptor>();
            lastKnownFeatureUnlockStates ??= new Dictionary<string, bool>();

            IReadOnlyList<ManagedResearchFeatureDescriptor> all =
                ManagedResearchFeatureCatalog.All;
            for (int i = 0; i < all.Count; i++)
            {
                ManagedResearchFeatureDescriptor descriptor = all[i];
                bool current = ResearchFeatureUnlockUtility.IsFeatureUnlocked(descriptor);
                if (!lastKnownFeatureUnlockStates.TryGetValue(descriptor.Id, out bool previous))
                {
                    continue;
                }

                if (!previous && current)
                {
                    newlyUnlocked.Add(descriptor);
                }
            }

            return newlyUnlocked;
        }

        private void EnqueueNewlyUnlockedAbilityLetterIds()
        {
            List<ManagedResearchAbilityDescriptor> newlyUnlocked =
                CollectNewlyUnlockedAbilities();
            for (int i = 0; i < newlyUnlocked.Count; i++)
            {
                pendingUnlockLetterIds.Add(newlyUnlocked[i].Id);
            }
        }

        private void EnqueueNewlyUnlockedFeatureLetterIds()
        {
            List<ManagedResearchFeatureDescriptor> newlyUnlocked =
                CollectNewlyUnlockedFeatures();
            for (int i = 0; i < newlyUnlocked.Count; i++)
            {
                pendingFeatureUnlockLetterIds.Add(newlyUnlocked[i].Id);
            }
        }

        private void TryProcessPendingUnlockLetters()
        {
            if (pendingUnlockLetterIds.Count == 0
                && pendingFeatureUnlockLetterIds.Count == 0)
            {
                nextUnlockLetterAttemptTick = 0;
                return;
            }

            TickManager? tickManager = Find.TickManager;
            if (tickManager == null)
            {
                return;
            }

            int ticksGame = tickManager.TicksGame;
            if (ticksGame < nextUnlockLetterAttemptTick)
            {
                return;
            }

            SendPendingUnlockLetters();
            SchedulePendingUnlockLetterRetryIfNeeded();
        }

        private void SchedulePendingUnlockLetterRetryIfNeeded()
        {
            if (pendingUnlockLetterIds.Count == 0
                && pendingFeatureUnlockLetterIds.Count == 0)
            {
                nextUnlockLetterAttemptTick = 0;
                return;
            }

            TickManager? tickManager = Find.TickManager;
            if (tickManager != null)
            {
                nextUnlockLetterAttemptTick =
                    tickManager.TicksGame + UnlockLetterRetryIntervalTicks;
            }
        }

        private void SendPendingUnlockLetters()
        {
            if (Current.ProgramState != ProgramState.Playing
                || LongEventHandler.AnyEventNowOrWaiting
                || Find.LetterStack == null)
            {
                return;
            }

            SendPendingAbilityUnlockLetters();
            SendPendingFeatureUnlockLetters();
        }

        private void SendPendingAbilityUnlockLetters()
        {
            if (pendingUnlockLetterIds.Count == 0)
            {
                return;
            }

            List<string> ids = new List<string>(pendingUnlockLetterIds);
            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                ManagedResearchAbilityDescriptor? descriptor = FindAbilityDescriptorById(id);
                if (descriptor == null)
                {
                    pendingUnlockLetterIds.Remove(id);
                    continue;
                }

                if (!ResearchFeatureUnlockUtility.IsAbilityUnlocked(descriptor))
                {
                    pendingUnlockLetterIds.Remove(id);
                    continue;
                }

                AbilityDef? abilityDef = descriptor.AbilityDef;
                if (abilityDef == null)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 无法发送科研解锁信件：缺少 AbilityDef，" +
                        $"descriptorId={descriptor.Id}，" +
                        $"abilityDefName={descriptor.AbilityDefName}。");
                    pendingUnlockLetterIds.Remove(id);
                    continue;
                }

                try
                {
                    SendAbilityUnlockLetter(abilityDef);
                    pendingUnlockLetterIds.Remove(id);
                }
                catch (Exception ex)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 发送科研解锁信件失败：" +
                        $"descriptorId={descriptor.Id}，" +
                        $"abilityDefName={descriptor.AbilityDefName}：{ex}");
                }
            }
        }

        private void SendPendingFeatureUnlockLetters()
        {
            if (pendingFeatureUnlockLetterIds.Count == 0)
            {
                return;
            }

            List<string> ids = new List<string>(pendingFeatureUnlockLetterIds);
            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                ManagedResearchFeatureDescriptor? descriptor = FindFeatureDescriptorById(id);
                if (descriptor == null)
                {
                    pendingFeatureUnlockLetterIds.Remove(id);
                    continue;
                }

                if (!ResearchFeatureUnlockUtility.IsFeatureUnlocked(descriptor))
                {
                    pendingFeatureUnlockLetterIds.Remove(id);
                    continue;
                }

                ResearchProjectDef? research = descriptor.ResearchProjectDef;
                if (research == null)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 无法发送特性解锁信件：缺少 ResearchProjectDef，" +
                        $"descriptorId={descriptor.Id}，" +
                        $"research={descriptor.ResearchProjectDefName}。");
                    pendingFeatureUnlockLetterIds.Remove(id);
                    continue;
                }

                try
                {
                    SendFeatureUnlockLetter(research);
                    pendingFeatureUnlockLetterIds.Remove(id);
                }
                catch (Exception ex)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 发送特性解锁信件失败：" +
                        $"descriptorId={descriptor.Id}，" +
                        $"research={descriptor.ResearchProjectDefName}：{ex}");
                }
            }
        }

        private void SendAbilityUnlockLetter(AbilityDef abilityDef)
        {
            TaggedString abilityLabel = abilityDef.LabelCap;
            TaggedString title = AbilityUnlockLetterTitleKey.Translate(abilityLabel);
            TaggedString text = GameComponent_MechanoidMechanitorScenarioState.IsEnabled
                ? AbilityUnlockLetterSpecialScenarioTextKey.Translate(abilityLabel)
                : AbilityUnlockLetterJusticeOnlyTextKey.Translate(abilityLabel);

            Find.LetterStack.ReceiveLetter(title, text, LetterDefOf.PositiveEvent);
        }

        private void SendFeatureUnlockLetter(ResearchProjectDef research)
        {
            TaggedString researchLabel = research.LabelCap;
            TaggedString title = FeatureUnlockLetterTitleKey.Translate(researchLabel);
            TaggedString text = FeatureUnlockLetterTextKey.Translate(researchLabel);
            Find.LetterStack.ReceiveLetter(title, text, LetterDefOf.PositiveEvent);
        }

        private static ManagedResearchAbilityDescriptor? FindAbilityDescriptorById(string id)
        {
            IReadOnlyList<ManagedResearchAbilityDescriptor> all =
                ManagedResearchAbilityCatalog.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Id == id)
                {
                    return all[i];
                }
            }

            return null;
        }

        private static ManagedResearchFeatureDescriptor? FindFeatureDescriptorById(string id)
        {
            IReadOnlyList<ManagedResearchFeatureDescriptor> all =
                ManagedResearchFeatureCatalog.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Id == id)
                {
                    return all[i];
                }
            }

            return null;
        }

        private bool IsSyncEnvironmentSafe()
        {
            if (Current.Game == null)
            {
                return false;
            }

            if (Current.ProgramState != ProgramState.Playing)
            {
                return false;
            }

            if (LongEventHandler.AnyEventNowOrWaiting)
            {
                return false;
            }

            if (Current.Game.GetComponent<GameComponent_MechanoidMechanitorRegistry>() == null)
            {
                return false;
            }

            return Current.Game.researchManager != null;
        }

        private void ScheduleNextUnlockStateSafetyCheck()
        {
            TickManager? tickManager = Find.TickManager;
            if (tickManager == null)
            {
                nextUnlockStateSafetyCheckTick = UnlockStateSafetyCheckIntervalTicks;
                return;
            }

            nextUnlockStateSafetyCheckTick =
                tickManager.TicksGame + UnlockStateSafetyCheckIntervalTicks;
        }

        private bool HasAbilityUnlockStateChanged()
        {
            lastKnownUnlockStates ??= new Dictionary<string, bool>();
            IReadOnlyList<ManagedResearchAbilityDescriptor> all =
                ManagedResearchAbilityCatalog.All;
            for (int i = 0; i < all.Count; i++)
            {
                ManagedResearchAbilityDescriptor descriptor = all[i];
                bool current = ResearchFeatureUnlockUtility.IsAbilityUnlocked(descriptor);
                if (!lastKnownUnlockStates.TryGetValue(descriptor.Id, out bool previous)
                    || previous != current)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasFeatureUnlockStateChanged()
        {
            lastKnownFeatureUnlockStates ??= new Dictionary<string, bool>();
            IReadOnlyList<ManagedResearchFeatureDescriptor> all =
                ManagedResearchFeatureCatalog.All;
            for (int i = 0; i < all.Count; i++)
            {
                ManagedResearchFeatureDescriptor descriptor = all[i];
                bool current = ResearchFeatureUnlockUtility.IsFeatureUnlocked(descriptor);
                if (!lastKnownFeatureUnlockStates.TryGetValue(descriptor.Id, out bool previous)
                    || previous != current)
                {
                    return true;
                }
            }

            return false;
        }

        private void CaptureAbilityUnlockStates()
        {
            lastKnownUnlockStates ??= new Dictionary<string, bool>();
            lastKnownUnlockStates.Clear();
            IReadOnlyList<ManagedResearchAbilityDescriptor> all =
                ManagedResearchAbilityCatalog.All;
            for (int i = 0; i < all.Count; i++)
            {
                ManagedResearchAbilityDescriptor descriptor = all[i];
                lastKnownUnlockStates[descriptor.Id] =
                    ResearchFeatureUnlockUtility.IsAbilityUnlocked(descriptor);
            }
        }

        private void CaptureFeatureUnlockStates()
        {
            lastKnownFeatureUnlockStates ??= new Dictionary<string, bool>();
            lastKnownFeatureUnlockStates.Clear();
            IReadOnlyList<ManagedResearchFeatureDescriptor> all =
                ManagedResearchFeatureCatalog.All;
            for (int i = 0; i < all.Count; i++)
            {
                ManagedResearchFeatureDescriptor descriptor = all[i];
                lastKnownFeatureUnlockStates[descriptor.Id] =
                    ResearchFeatureUnlockUtility.IsFeatureUnlocked(descriptor);
            }
        }
    }
}
