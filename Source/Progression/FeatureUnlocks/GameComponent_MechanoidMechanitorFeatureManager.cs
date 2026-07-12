using System;
using System.Collections.Generic;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class GameComponent_MechanoidMechanitorFeatureManager : GameComponent
    {
        private const int UnlockStateSafetyCheckIntervalTicks = 120;
        private const int ForcedSyncRetryIntervalTicks = 60;

        private bool pendingForcedSync;
        private readonly HashSet<Pawn> pendingPawnSyncs = new HashSet<Pawn>();
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

            if (TryPerformForcedFullSync())
            {
                pendingPawnSyncs.Clear();
            }
            else
            {
                // 低频检查发现变化但同步失败时，转入强制同步重试路径。
                pendingForcedSync = true;
                nextForcedSyncAttemptTick = ticksGame + ForcedSyncRetryIntervalTicks;
            }
        }

        private void OnResearchProjectFinished()
        {
            if (!IsSyncEnvironmentSafe())
            {
                pendingForcedSync = true;
                return;
            }

            if (!HasUnlockStateChanged())
            {
                return;
            }

            if (TryPerformForcedFullSync())
            {
                pendingPawnSyncs.Clear();
                ScheduleNextUnlockStateSafetyCheck();
            }
            else
            {
                pendingForcedSync = true;
                TickManager? tickManager = Find.TickManager;
                int ticksGame = tickManager?.TicksGame ?? 0;
                nextForcedSyncAttemptTick = ticksGame + ForcedSyncRetryIntervalTicks;
            }
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
