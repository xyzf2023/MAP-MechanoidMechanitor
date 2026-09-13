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
        private sealed class PendingFusionStart
        {
            public Pawn Source = null!;
            public Pawn Wearer = null!;
            public bool SendFailureMessage;
        }

        private List<MechFusionSession> sessions = new List<MechFusionSession>();
        private Dictionary<string, MechFusionSession>? sessionById;
        private Dictionary<Pawn, MechFusionSession>? sessionByWearer;
        private Dictionary<Pawn, MechFusionSession>? sessionBySource;
        private readonly List<MechFusionSession> tickSnapshot =
            new List<MechFusionSession>();

        private static readonly List<PendingFusionStart> PendingStarts =
            new List<PendingFusionStart>();

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
            return registry.sessionById!.TryGetValue(id, out session);
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
            registry.sessions.Remove(session);
            if (session.SourcePawn != null)
            {
                registry.sessionBySource!.Remove(session.SourcePawn);
            }

            if (session.WearerPawn != null)
            {
                registry.sessionByWearer!.Remove(session.WearerPawn);
            }

            registry.sessionById!.Remove(session.SessionId);
        }

        internal static bool TryQueueStart(
            Pawn? source,
            Pawn? wearer,
            bool sendFailureMessage,
            out string? failureReason)
        {
            failureReason = null;
            if (!MechFusionValidator.CanStart(source, wearer, out failureReason))
            {
                return false;
            }

            for (int i = 0; i < PendingStarts.Count; i++)
            {
                PendingFusionStart existing = PendingStarts[i];
                if (ReferenceEquals(existing.Source, source)
                    && ReferenceEquals(existing.Wearer, wearer))
                {
                    return true;
                }
            }

            PendingStarts.Add(new PendingFusionStart
            {
                Source = source!,
                Wearer = wearer!,
                SendFailureMessage = sendFailureMessage
            });
            return true;
        }

        private static void ClearPendingStarts()
        {
            PendingStarts.Clear();
        }

        private void ProcessPendingStarts()
        {
            if (PendingStarts.Count == 0)
            {
                return;
            }

            PendingFusionStart[] pending = PendingStarts.ToArray();
            PendingStarts.Clear();
            for (int i = 0; i < pending.Length; i++)
            {
                PendingFusionStart entry = pending[i];
                if (!MechFusionStartService.TryStartFusion(
                        entry.Source,
                        entry.Wearer,
                        out string? failureReason)
                    && entry.SendFailureMessage)
                {
                    Messages.Message(
                        failureReason
                            ?? "MAP_MechanoidMechanitor.Fusion.Failure.Unexpected"
                                .Translate(),
                        entry.Source,
                        MessageTypeDefOf.RejectInput,
                        historical: false);
                }
            }
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            ProcessPendingStarts();

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
            ClearPendingStarts();
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            cachedRegistryGame = Current.Game;
            cachedRegistry = this;
            RebuildIndexes();
            ClearPendingStarts();
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
