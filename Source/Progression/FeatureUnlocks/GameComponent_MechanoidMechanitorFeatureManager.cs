using System;
using System.Collections.Generic;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class GameComponent_MechanoidMechanitorFeatureManager : GameComponent
    {
        private const int UnlockStateSafetyCheckIntervalTicks = 120;
        private const int ForcedSyncRetryIntervalTicks = 60;

        private const string AbilityUnlockLetterTitleKey =
            "MAP_MechanoidMechanitor.AbilityUnlockLetter.Title";
        private const string AbilityUnlockLetterSpecialScenarioTextKey =
            "MAP_MechanoidMechanitor.AbilityUnlockLetter.SpecialScenarioText";
        private const string AbilityUnlockLetterJusticeOnlyTextKey =
            "MAP_MechanoidMechanitor.AbilityUnlockLetter.JusticeOnlyText";

        private bool pendingForcedSync;
        private readonly HashSet<Pawn> pendingPawnSyncs = new HashSet<Pawn>();
        private readonly HashSet<string> pendingUnlockLetterIds = new HashSet<string>();
        private Dictionary<string, bool>? lastKnownUnlockStates;
        private int nextUnlockStateSafetyCheckTick;
        private int nextForcedSyncAttemptTick;

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
            CaptureUnlockStates();
            pendingForcedSync = true;
            nextForcedSyncAttemptTick = 0;
            ScheduleNextUnlockStateSafetyCheck();
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            CaptureUnlockStates();
            pendingForcedSync = true;
            nextForcedSyncAttemptTick = 0;
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
                return;
            }

            // 失败后延迟重试，避免每帧刷错误日志。
            nextForcedSyncAttemptTick = ticksGame + ForcedSyncRetryIntervalTicks;
        }

        private bool TryPerformForcedFullSync()
        {
            try
            {
                CaptureUnlockStates();
                ManagedResearchAbilitySyncUtility.SyncAllRelevantPawns();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 受管理科研能力全量同步失败，将在稍后重试：" + ex);
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

            if (!HasUnlockStateChanged())
            {
                return;
            }

            // 低频检查只修正能力状态，不把意外发现的 false→true 记为待发信件。
            if (TryPerformForcedFullSync())
            {
                pendingPawnSyncs.Clear();
                SendPendingUnlockLetters();
            }
            else
            {
                pendingForcedSync = true;
                nextForcedSyncAttemptTick = ticksGame + ForcedSyncRetryIntervalTicks;
            }
        }

        private void OnResearchProjectFinished()
        {
            // 初始化阶段（如起始科研）：只延迟同步，不记录解锁信件。
            if (Current.ProgramState != ProgramState.Playing)
            {
                pendingForcedSync = true;
                return;
            }

            // Playing 下即使 LongEvent 导致暂时不安全，也先记录 false→true 待发信件。
            EnqueueNewlyUnlockedLetterIds();

            if (!IsSyncEnvironmentSafe())
            {
                pendingForcedSync = true;
                return;
            }

            if (!HasUnlockStateChanged() && pendingUnlockLetterIds.Count == 0)
            {
                return;
            }

            if (TryPerformForcedFullSync())
            {
                pendingPawnSyncs.Clear();
                ScheduleNextUnlockStateSafetyCheck();
                SendPendingUnlockLetters();
            }
            else
            {
                pendingForcedSync = true;
                TickManager? tickManager = Find.TickManager;
                int ticksGame = tickManager?.TicksGame ?? 0;
                nextForcedSyncAttemptTick = ticksGame + ForcedSyncRetryIntervalTicks;
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
                    // 缓存缺失视为初始化/读档状态，不视为正常科研解锁。
                    continue;
                }

                if (!previous && current)
                {
                    newlyUnlocked.Add(descriptor);
                }
            }

            return newlyUnlocked;
        }

        private void EnqueueNewlyUnlockedLetterIds()
        {
            List<ManagedResearchAbilityDescriptor> newlyUnlocked =
                CollectNewlyUnlockedAbilities();
            for (int i = 0; i < newlyUnlocked.Count; i++)
            {
                pendingUnlockLetterIds.Add(newlyUnlocked[i].Id);
            }
        }

        private void SendPendingUnlockLetters()
        {
            if (pendingUnlockLetterIds.Count == 0)
            {
                return;
            }

            if (Current.ProgramState != ProgramState.Playing
                || LongEventHandler.AnyEventNowOrWaiting
                || Find.LetterStack == null)
            {
                return;
            }

            List<string> ids = new List<string>(pendingUnlockLetterIds);
            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                ManagedResearchAbilityDescriptor? descriptor = FindDescriptorById(id);
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

        private void SendAbilityUnlockLetter(AbilityDef abilityDef)
        {
            TaggedString abilityLabel = abilityDef.LabelCap;
            TaggedString title = AbilityUnlockLetterTitleKey.Translate(abilityLabel);
            TaggedString text = GameComponent_MechanoidMechanitorScenarioState.IsEnabled
                ? AbilityUnlockLetterSpecialScenarioTextKey.Translate(abilityLabel)
                : AbilityUnlockLetterJusticeOnlyTextKey.Translate(abilityLabel);

            Find.LetterStack.ReceiveLetter(title, text, LetterDefOf.PositiveEvent);
        }

        private static ManagedResearchAbilityDescriptor? FindDescriptorById(string id)
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

            // Find.ResearchManager 在 Game 非空时访问 researchManager 字段是安全的。
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

        private bool HasUnlockStateChanged()
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

        private void CaptureUnlockStates()
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
    }
}
