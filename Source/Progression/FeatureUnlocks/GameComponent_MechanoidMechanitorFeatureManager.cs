using System.Collections.Generic;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class GameComponent_MechanoidMechanitorFeatureManager : GameComponent
    {
        private bool pendingForcedSync;
        private Dictionary<string, bool>? lastKnownUnlockStates;

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

            ManagedResearchAbilitySyncUtility.SyncPawn(pawn);
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            CaptureUnlockStates();
            pendingForcedSync = true;
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            CaptureUnlockStates();
            pendingForcedSync = true;
        }

        public override void GameComponentUpdate()
        {
            base.GameComponentUpdate();
            if (pendingForcedSync)
            {
                if (Current.ProgramState != ProgramState.Playing)
                {
                    return;
                }

                if (LongEventHandler.AnyEventNowOrWaiting)
                {
                    return;
                }

                if (Current.Game.GetComponent<GameComponent_MechanoidMechanitorRegistry>() == null)
                {
                    return;
                }

                pendingForcedSync = false;
                CaptureUnlockStates();
                ManagedResearchAbilitySyncUtility.SyncAllRelevantPawns();
                return;
            }

            // 支持调试重置科研：每帧只比对少量缓存解锁状态，不扫描全部 Pawn。
            if (Current.ProgramState == ProgramState.Playing
                && !LongEventHandler.AnyEventNowOrWaiting
                && HasUnlockStateChanged())
            {
                CaptureUnlockStates();
                ManagedResearchAbilitySyncUtility.SyncAllRelevantPawns();
            }
        }

        private void OnResearchProjectFinished()
        {
            if (HasUnlockStateChanged())
            {
                CaptureUnlockStates();
                ManagedResearchAbilitySyncUtility.SyncAllRelevantPawns();
            }
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
