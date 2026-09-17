using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 本次合体实例的唯一权威注册表。资格表、通用形态表与本表严格分离：
    /// 资格表表示“能不能合体”，形态表表示 Pawn/Merged/Building，
    /// 本表表示“当前这一次合体”的全部实例状态。
    /// </summary>
    public sealed class GameComponent_MechFusionSessionRegistry : GameComponent
    {
        private List<MechFusionSession> sessions = new List<MechFusionSession>();
        private Dictionary<string, MechFusionSession>? sessionById;
        private Dictionary<Pawn, MechFusionSession>? sessionByWearer;
        private Dictionary<Pawn, MechFusionSession>? sessionBySource;
        private readonly List<MechFusionSession> tickSnapshot =
            new List<MechFusionSession>();

        private static Game? cachedRegistryGame;
        private static GameComponent_MechFusionSessionRegistry? cachedRegistry;

        public GameComponent_MechFusionSessionRegistry(Game game)
        {
        }

        private static GameComponent_MechFusionSessionRegistry? CurrentRegistry
        {
            get
            {
                Game? game = Current.Game;
                if (game == null)
                {
                    cachedRegistryGame = null;
                    cachedRegistry = null;
                    return null;
                }

                if (!ReferenceEquals(cachedRegistryGame, game))
                {
                    cachedRegistryGame = game;
                    cachedRegistry =
                        game.GetComponent<GameComponent_MechFusionSessionRegistry>();
                }
                else if (cachedRegistry == null)
                {
                    cachedRegistry =
                        game.GetComponent<GameComponent_MechFusionSessionRegistry>();
                }

                return cachedRegistry;
            }
        }

        public static bool HasAnySession
        {
            get
            {
                GameComponent_MechFusionSessionRegistry? registry = CurrentRegistry;
                return registry != null && registry.sessions.Count > 0;
            }
        }

        public static bool TryGetSessionForWearer(
            Pawn? pawn,
            out MechFusionSession? session)
        {
            session = null;
            GameComponent_MechFusionSessionRegistry? registry = CurrentRegistry;
            if (registry == null || pawn == null)
            {
                return false;
            }

            registry.EnsureIndexes();
            return registry.sessionByWearer!.TryGetValue(pawn, out session);
        }

        public static bool TryGetSessionForSource(
            Pawn? pawn,
            out MechFusionSession? session)
        {
            session = null;
            GameComponent_MechFusionSessionRegistry? registry = CurrentRegistry;
            if (registry == null || pawn == null)
            {
                return false;
            }

            registry.EnsureIndexes();
            return registry.sessionBySource!.TryGetValue(pawn, out session);
        }

        public static bool TryGetSessionById(
            string? id,
            out MechFusionSession? session)
        {
            session = null;
            GameComponent_MechFusionSessionRegistry? registry = CurrentRegistry;
            if (registry == null || string.IsNullOrEmpty(id))
            {
                return false;
            }

            registry.EnsureIndexes();
            return registry.sessionById!.TryGetValue(id!, out session);
        }

        internal static IReadOnlyList<MechFusionSession> GetSessionsForReading()
        {
            GameComponent_MechFusionSessionRegistry? registry = CurrentRegistry;
            return registry != null
                ? registry.sessions
                : Array.Empty<MechFusionSession>();
        }

        internal static bool RegisterSession(MechFusionSession session)
        {
            GameComponent_MechFusionSessionRegistry? registry = CurrentRegistry;
            if (registry == null
                || session == null
                || session.SourcePawn == null
                || session.WearerPawn == null)
            {
                return false;
            }

            registry.EnsureIndexes();
            if (registry.sessionBySource!.ContainsKey(session.SourcePawn)
                || registry.sessionByWearer!.ContainsKey(session.WearerPawn)
                || registry.sessionById!.ContainsKey(session.SessionId))
            {
                return false;
            }

            registry.sessions.Add(session);
            registry.sessionBySource[session.SourcePawn] = session;
            registry.sessionByWearer[session.WearerPawn] = session;
            registry.sessionById[session.SessionId] = session;
            return true;
        }

        internal static void RemoveSession(MechFusionSession? session)
        {
            GameComponent_MechFusionSessionRegistry? registry = CurrentRegistry;
            if (registry == null || session == null)
            {
                return;
            }

            registry.EnsureIndexes();
            session.MarkTeardownCompleted();
            registry.sessions.Remove(session);
            // 直接按 Pawn 删除索引会误删共享同一 Pawn 的其他会话索引，
            // 因此统一按剩余会话重建，保证索引与列表始终一致。
            registry.RebuildIndexes();
            DataProcessingPawnLifecycleCoordinator.EnqueueReactivation(session.SourcePawn);
            DataProcessingPawnLifecycleCoordinator.EnqueueReactivation(session.WearerPawn);
        }

        public override void GameComponentTick()        {
            base.GameComponentTick();
            GameComponent_DataProcessingAllocationRegistry.CurrentRegistry?.ValidateFusionAllocations();

            if (sessions.Count == 0)
            {
                return;
            }

            tickSnapshot.Clear();
            tickSnapshot.AddRange(sessions);
            for (int i = 0; i < tickSnapshot.Count; i++)
            {
                MechFusionSession? session = tickSnapshot[i];
                if (session == null)
                {
                    continue;
                }

                if (session.IsEnding && session.TeardownDeferred)
                {
                    MechFusionTeardownService.TryResumeDeferredTeardown(session);
                    continue;
                }

                if (session.IsPendingRecovery)
                {
                    MechFusionTeardownService.TryRecoverPendingSession(session);
                    continue;
                }

                if (session.IsActive)
                {
                    if (session.SourcePawn?.Dead == true)
                    {
                        MechFusionTeardownService.TryTeardown(
                            session,
                            MechFusionExitReason.StabilityDepleted,
                            force: false);
                        continue;
                    }

                    MechFusionEnergyUtility.TickSession(session);
                    if (session.State == MechFusionSessionState.Active)
                    {
                        MechFusionHealthEffectManager.ExpireTimedEffects(session);
                        MechFusionStabilityUtility.TickRepair(session);
                    }
                }
            }
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(
                ref sessions,
                "mechFusionSessions",
                LookMode.Deep);
            sessions ??= new List<MechFusionSession>();

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                sessionById = null;
                sessionByWearer = null;
                sessionBySource = null;
            }
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            cachedRegistryGame = Current.Game;
            cachedRegistry = this;
            sessions = new List<MechFusionSession>();
            sessionById = new Dictionary<string, MechFusionSession>(
                StringComparer.Ordinal);
            sessionByWearer = new Dictionary<Pawn, MechFusionSession>();
            sessionBySource = new Dictionary<Pawn, MechFusionSession>();
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            cachedRegistryGame = Current.Game;
            cachedRegistry = this;
            RebuildIndexes();
            MechFusionRepairUtility.RepairAfterLoad();
        }

        private void EnsureIndexes()
        {
            if (sessionById == null
                || sessionByWearer == null
                || sessionBySource == null)
            {
                RebuildIndexes();
            }
        }

        private void RebuildIndexes()
        {
            sessions ??= new List<MechFusionSession>();
            sessionById = new Dictionary<string, MechFusionSession>(
                StringComparer.Ordinal);
            sessionByWearer = new Dictionary<Pawn, MechFusionSession>();
            sessionBySource = new Dictionary<Pawn, MechFusionSession>();

            for (int i = sessions.Count - 1; i >= 0; i--)
            {
                MechFusionSession? session = sessions[i];
                if (session == null)
                {
                    sessions.RemoveAt(i);
                    continue;
                }

                session.EnsureInitialized();
                if (sessionById.ContainsKey(session.SessionId))
                {
                    sessions.RemoveAt(i);
                    continue;
                }

                sessionById[session.SessionId] = session;
                if (session.SourcePawn != null
                    && !sessionBySource!.ContainsKey(session.SourcePawn))
                {
                    sessionBySource[session.SourcePawn] = session;
                }

                if (session.WearerPawn != null
                    && !sessionByWearer!.ContainsKey(session.WearerPawn))
                {
                    sessionByWearer[session.WearerPawn] = session;
                }
            }
        }
    }
}
