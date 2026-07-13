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
        private const int ForcedSyncRetryIntervalTicks = 60;
        private const int UnlockLetterRetryIntervalTicks = 60;

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
                ManagedResearchAbilitySyncUtility.SyncPawn(pawn);
                return;
            }

            manager.pendingPawnSyncs.Add(pawn);
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            CaptureAbilityUnlockStates();
            CaptureFeatureUnlockStates();
            pendingForcedSync = true;
            nextForcedSyncAttemptTick = 0;
            nextUnlockLetterAttemptTick = 0;
            ScheduleNextUnlockStateSafetyCheck();
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            CaptureAbilityUnlockStates();
            CaptureFeatureUnlockStates();
            pendingForcedSync = true;
            nextForcedSyncAttemptTick = 0;
            nextUnlockLetterAttemptTick = 0;
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
                nextForcedSyncAttemptTick = 0;
                ScheduleNextUnlockStateSafetyCheck();
                SendPendingUnlockLetters();
                SchedulePendingUnlockLetterRetryIfNeeded();
                return;
            }

            nextForcedSyncAttemptTick = ticksGame + ForcedSyncRetryIntervalTicks;
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

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 受管理科研能力/特性全量同步失败，将在稍后重试：" + ex);
                return false;
            }
        }

        private void ProcessPendingPawnSyncs()
        {
            if (pendingPawnSyncs.Count == 0)
            {
                return;
            }

            List<Pawn> toSync = new List<Pawn>(pendingPawnSyncs);
            pendingPawnSyncs.Clear();

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
                    Log.Error(
                        "[MAP-机械族机械师] 待处理 Pawn 同步异常：" +
                        $"pawn={pawn.LabelShort}（{pawn.ThingID}）：{ex}");
                }
            }
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
            else
            {
                pendingForcedSync = true;
                nextForcedSyncAttemptTick = ticksGame + ForcedSyncRetryIntervalTicks;
            }
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

            if (TryPerformForcedFullSync())
            {
                pendingPawnSyncs.Clear();
                ScheduleNextUnlockStateSafetyCheck();
                SendPendingUnlockLetters();
                SchedulePendingUnlockLetterRetryIfNeeded();
            }
            else
            {
                pendingForcedSync = true;
                TickManager? tickManager = Find.TickManager;
                int ticksGame = tickManager?.TicksGame ?? 0;
                nextForcedSyncAttemptTick = ticksGame + ForcedSyncRetryIntervalTicks;
            }
        }

        /// <summary>
        /// 处理机械师名册变化引起的动态意识合并刷新请求。
        /// </summary>
        private void ProcessPendingDynamicConsciousnessRefresh()
        {
            if (!pendingDynamicConsciousnessRefresh)
            {
                return;
            }

            // 先清除再同步：同步过程中若死亡/名册变化再次置位，可保留到下一次处理。
            pendingDynamicConsciousnessRefresh = false;
            try
            {
                if (!ManagedResearchFeatureSyncUtility.SyncDynamicConsciousnessBonuses())
                {
                    pendingDynamicConsciousnessRefresh = true;
                }
            }
            catch (Exception ex)
            {
                pendingDynamicConsciousnessRefresh = true;
                Log.Error(
                    "[MAP-机械族机械师] 机械师名册变化后的动态意识加成刷新失败，将保留 pending 稍后重试："
                    + ex);
            }
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
