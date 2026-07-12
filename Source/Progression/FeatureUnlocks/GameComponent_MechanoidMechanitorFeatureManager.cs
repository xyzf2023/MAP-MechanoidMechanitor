using System;
using System.Collections.Generic;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class GameComponent_MechanoidMechanitorFeatureManager : GameComponent
    {
        private const int UnlockStateSafetyCheckIntervalTicks = 120;

        private bool pendingForcedSync;
        private readonly HashSet<Pawn> pendingPawnSyncs = new HashSet<Pawn>();
        private Dictionary<string, bool>? lastKnownUnlockStates;
        private int nextUnlockStateSafetyCheckTick;

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
            ScheduleNextUnlockStateSafetyCheck();
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            CaptureUnlockStates();
            pendingForcedSync = true;
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
                pendingForcedSync = false;
                CaptureUnlockStates();
                ManagedResearchAbilitySyncUtility.SyncAllRelevantPawns();
                // 全量同步已覆盖相关 Pawn，清空队列避免同帧重复单 Pawn 同步。
                pendingPawnSyncs.Clear();
                ScheduleNextUnlockStateSafetyCheck();
                return;
            }

            ProcessPendingPawnSyncs();
            TryRunUnlockStateSafetyCheck();
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

            CaptureUnlockStates();
            ManagedResearchAbilitySyncUtility.SyncAllRelevantPawns();
            pendingPawnSyncs.Clear();
        }

        private void OnResearchProjectFinished()
        {
            if (HasUnlockStateChanged())
            {
                CaptureUnlockStates();
                ManagedResearchAbilitySyncUtility.SyncAllRelevantPawns();
                pendingPawnSyncs.Clear();
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
