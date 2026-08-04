using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Verse;

namespace MAP_MechanoidMechanitor
{
    internal enum JusticeBossTraceMode
    {
        None = 0,
        RecentBatch = 1,
        FullEvent = 2,
    }

    internal sealed class TrackedJusticeBossEvent
    {
        internal int eventId;
        internal int mapId = -1;
        internal int registeredTick = -1;
        internal int landedTotal;
        internal int lastLaunchedWave;
        internal int totalWaves;
        internal bool finalAssaultWaveLaunched;
        internal JusticeBossTraceMode mode = JusticeBossTraceMode.None;
        internal int windowStartTick = -1;
        internal int windowEndTick = -1;
        internal int hardStopTick = -1;
        internal readonly HashSet<int> pawnIds = new HashSet<int>();
        internal readonly HashSet<int> recentBatchPawnIds = new HashSet<int>();

        internal bool WindowActive => mode != JusticeBossTraceMode.None;

        internal bool TracesPawn(int pawnId, int tick)
        {
            if (!WindowActive
                || tick < windowStartTick
                || tick > windowEndTick)
            {
                return false;
            }

            return mode == JusticeBossTraceMode.FullEvent
                ? pawnIds.Contains(pawnId)
                : recentBatchPawnIds.Contains(pawnId);
        }

        internal void ClearWindow()
        {
            mode = JusticeBossTraceMode.None;
            windowStartTick = -1;
            windowEndTick = -1;
            hardStopTick = -1;
            recentBatchPawnIds.Clear();
        }
    }

    internal sealed class JusticeBossLandingBatch
    {
        internal int mapId = -1;
        internal int pendingBefore;
        internal long startedTimestamp;
        internal int landedCount;
        internal readonly Dictionary<int, List<int>> landedPawnIdsByEvent =
            new Dictionary<int, List<int>>();
        internal readonly HashSet<int> assaultEvents = new HashSet<int>();
        internal readonly Dictionary<string, int> kindCounts =
            new Dictionary<string, int>();

        internal void Reset()
        {
            mapId = -1;
            pendingBefore = 0;
            startedTimestamp = 0L;
            landedCount = 0;
            landedPawnIdsByEvent.Clear();
            assaultEvents.Clear();
            kindCounts.Clear();
        }
    }

    internal struct JusticeBossLandingBatchState
    {
        public bool active;
        public JusticeBossLandingBatch? batch;
        public JusticeBossLandingBatch? previous;
    }

    internal struct JusticeBossGameTickState
    {
        public bool active;
        public int expectedTick;
        public long startedTimestamp;
    }

    internal sealed class JusticeBossPawnTraceContext
    {
        internal Pawn? pawn;
        internal int pawnId = -1;
        internal int eventId = -1;
        internal JusticeBossTraceMode mode = JusticeBossTraceMode.None;
        internal bool isRecentBatch;
        internal long startedTimestamp;

        internal void Clear()
        {
            pawn = null;
            pawnId = -1;
            eventId = -1;
            mode = JusticeBossTraceMode.None;
            isRecentBatch = false;
            startedTimestamp = 0L;
        }
    }

    internal struct JusticeBossPawnTickState
    {
        public bool active;
        public JusticeBossPawnTraceContext? context;
    }

    internal struct JusticeBossNestedTraceState
    {
        public bool active;
        public long startedTimestamp;
    }

    internal static class JusticeBossPostLandingTraceState
    {
        internal const int RecentBatchPawnThreshold = 8;
        internal const int RecentBatchWindowTicks = 1;
        internal const int FullEventWindowTicks = 3;
        internal const int MaxWindowTicks = 5;

        private const int MaxLoggedPawnIds = 16;

        private static readonly Dictionary<int, TrackedJusticeBossEvent> Events =
            new Dictionary<int, TrackedJusticeBossEvent>();
        private static readonly Dictionary<int, int> PawnToEvent =
            new Dictionary<int, int>();
        private static readonly List<int> EventBuffer = new List<int>();
        private static readonly List<int> PawnBuffer = new List<int>();

        private static volatile bool anyWindowActive;
        private static int activeWindowCount;
        private static int lastWindowArmedTick = -1;

        [ThreadStatic]
        private static JusticeBossLandingBatch? activeLandingBatch;

        [ThreadStatic]
        private static List<JusticeBossPawnTraceContext>? pawnContextStack;

        [ThreadStatic]
        private static int pawnContextDepth;

        internal static bool AnyWindowActive =>
            anyWindowActive && JusticeBossTraceFileWriter.CanWriteNormal;

        internal static bool HasPawnContext =>
            pawnContextDepth > 0 && JusticeBossTraceFileWriter.CanWriteNormal;

        internal static JusticeBossPawnTraceContext? CurrentPawnContext =>
            pawnContextDepth > 0 && pawnContextStack != null
                ? pawnContextStack[pawnContextDepth - 1]
                : null;

        internal static int CurrentTick()
        {
            return Current.Game?.tickManager?.TicksGame ?? -1;
        }

        internal static void OnSessionOpened()
        {
            ResetAll("session-opened", writeReset: false);
            JusticeBossTraceFileWriter.Write(
                "TRACE_STATE_READY",
                "recentBatchThreshold=" + RecentBatchPawnThreshold
                    + " recentBatchWindowTicks=" + RecentBatchWindowTicks
                    + " fullEventWindowTicks=" + FullEventWindowTicks
                    + " maxWindowTicks=" + MaxWindowTicks,
                JusticeBossTraceWriteMode.Critical);
        }

        internal static void OnSessionClosing(string reason)
        {
            ResetAll(reason, writeReset: true);
        }

        internal static void ResetAll(string reason, bool writeReset)
        {
            if (writeReset && JusticeBossTraceFileWriter.IsOpen)
            {
                JusticeBossTraceFileWriter.Write(
                    "TRACE_STATE_RESET",
                    "reason=" + JusticeBossTraceFormatting.Sanitize(reason)
                        + " events=" + Events.Count
                        + " trackedPawns=" + PawnToEvent.Count
                        + " activeWindows=" + activeWindowCount,
                    JusticeBossTraceWriteMode.Emergency);
            }

            Events.Clear();
            PawnToEvent.Clear();
            EventBuffer.Clear();
            PawnBuffer.Clear();
            activeWindowCount = 0;
            anyWindowActive = false;
            lastWindowArmedTick = -1;
            ClearThreadContexts();
        }

        internal static void StopAfterNormalLimit()
        {
            if (!JusticeBossTraceFileWriter.NormalLimitReached)
            {
                return;
            }

            JusticeBossTraceFileWriter.Write(
                "TRACE_STOPPED_AFTER_LIMIT",
                "events=" + Events.Count
                    + " trackedPawns=" + PawnToEvent.Count
                    + " activeWindows=" + activeWindowCount,
                JusticeBossTraceWriteMode.Emergency);
            ResetAll("normal-limit-reached", writeReset: false);
        }

        internal static void ClearThreadContexts()
        {
            activeLandingBatch = null;
            if (pawnContextStack != null)
            {
                for (int i = 0; i < pawnContextStack.Count; i++)
                {
                    pawnContextStack[i].Clear();
                }
            }

            pawnContextDepth = 0;
        }

        internal static void NotifyEventRegistered(int eventId, int mapId)
        {
            if (!JusticeBossTraceFileWriter.IsOpen || eventId == 0)
            {
                return;
            }

            TrackedJusticeBossEvent tracked = GetOrCreateEvent(eventId);
            tracked.mapId = mapId;
            tracked.registeredTick = CurrentTick();
            JusticeBossTraceFileWriter.Write(
                "EVENT_REGISTERED",
                "event=" + eventId + " map=" + mapId,
                JusticeBossTraceWriteMode.Critical);
        }

        internal static void NotifyWaveProgress(
            int eventId,
            int waveCount,
            int totalWaves)
        {
            if (!JusticeBossTraceFileWriter.IsOpen
                || eventId == 0
                || waveCount <= 0
                || totalWaves <= 0)
            {
                return;
            }

            TrackedJusticeBossEvent tracked = GetOrCreateEvent(eventId);
            bool changed = waveCount > tracked.lastLaunchedWave
                || totalWaves != tracked.totalWaves;
            tracked.lastLaunchedWave = Math.Max(tracked.lastLaunchedWave, waveCount);
            tracked.totalWaves = totalWaves;
            tracked.finalAssaultWaveLaunched =
                tracked.lastLaunchedWave >= tracked.totalWaves;

            if (changed)
            {
                JusticeBossTraceFileWriter.Write(
                    tracked.finalAssaultWaveLaunched
                        ? "FINAL_WAVE_LAUNCHED"
                        : "WAVE_PROGRESS",
                    "event=" + eventId
                        + " wave=" + tracked.lastLaunchedWave
                        + " totalWaves=" + tracked.totalWaves
                        + " final=" + tracked.finalAssaultWaveLaunched,
                    tracked.finalAssaultWaveLaunched
                        ? JusticeBossTraceWriteMode.Critical
                        : JusticeBossTraceWriteMode.Buffered);
            }
        }

        internal static void NotifyEventCleared(int eventId, string reason)
        {
            if (eventId == 0
                || !Events.TryGetValue(eventId, out TrackedJusticeBossEvent? tracked))
            {
                return;
            }

            PawnBuffer.Clear();
            foreach (KeyValuePair<int, int> pair in PawnToEvent)
            {
                if (pair.Value == eventId)
                {
                    PawnBuffer.Add(pair.Key);
                }
            }

            for (int i = 0; i < PawnBuffer.Count; i++)
            {
                PawnToEvent.Remove(PawnBuffer[i]);
            }

            bool hadWindow = tracked.WindowActive;
            tracked.ClearWindow();
            Events.Remove(eventId);
            if (hadWindow)
            {
                RecountActiveWindows();
            }

            JusticeBossTraceFileWriter.Write(
                "EVENT_CLEARED",
                "event=" + eventId
                    + " reason=" + JusticeBossTraceFormatting.Sanitize(reason)
                    + " landedTotal=" + tracked.landedTotal,
                JusticeBossTraceWriteMode.Emergency);
        }

        internal static JusticeBossLandingBatchState BeginLandingBatch(
            int mapId,
            int pendingBefore)
        {
            if (!JusticeBossTraceFileWriter.CanWriteNormal || pendingBefore <= 0)
            {
                return default;
            }

            JusticeBossLandingBatch batch = new JusticeBossLandingBatch
            {
                mapId = mapId,
                pendingBefore = pendingBefore,
                startedTimestamp = Stopwatch.GetTimestamp(),
            };

            JusticeBossLandingBatchState state = new JusticeBossLandingBatchState
            {
                active = true,
                batch = batch,
                previous = activeLandingBatch,
            };
            activeLandingBatch = batch;
            return state;
        }

        internal static void NotifyPawnLanded(
            Pawn? pawn,
            int eventId,
            JusticeBossDropRole role,
            int mapId)
        {
            if (pawn == null || !JusticeBossTraceFileWriter.IsOpen)
            {
                return;
            }

            TrackedJusticeBossEvent tracked = GetOrCreateEvent(eventId);
            if (tracked.mapId < 0)
            {
                tracked.mapId = mapId;
            }

            int pawnId = pawn.thingIDNumber;
            if (tracked.pawnIds.Add(pawnId))
            {
                tracked.landedTotal++;
            }

            PawnToEvent[pawnId] = eventId;

            JusticeBossLandingBatch? batch = activeLandingBatch;
            if (batch == null)
            {
                return;
            }

            batch.landedCount++;
            if (!batch.landedPawnIdsByEvent.TryGetValue(eventId, out List<int>? ids))
            {
                ids = new List<int>(16);
                batch.landedPawnIdsByEvent[eventId] = ids;
            }

            ids.Add(pawnId);
            if (role == JusticeBossDropRole.Assault)
            {
                batch.assaultEvents.Add(eventId);
            }

            string kindKey = (pawn.kindDef?.defName ?? "null") + "/" + role;
            batch.kindCounts.TryGetValue(kindKey, out int current);
            batch.kindCounts[kindKey] = current + 1;
        }

        internal static void EndLandingBatch(
            JusticeBossLandingBatchState state,
            int pendingAfter,
            Exception? exception)
        {
            if (!state.active || state.batch == null)
            {
                return;
            }

            JusticeBossLandingBatch batch = state.batch;
            activeLandingBatch = state.previous;

            try
            {
                string kindCounts =
                    JusticeBossTraceFormatting.DescribeKindCounts(batch.kindCounts);
                double elapsedMs = ParseElapsed(batch.startedTimestamp);

                if (exception != null)
                {
                    JusticeBossTraceFileWriter.Write(
                        "LANDING_BATCH_EXCEPTION",
                        "map=" + batch.mapId
                            + " pendingBefore=" + batch.pendingBefore
                            + " pendingAfter=" + pendingAfter
                            + " landed=" + batch.landedCount
                            + " "
                            + JusticeBossTraceFormatting.DescribeException(exception),
                        JusticeBossTraceWriteMode.Emergency);
                }

                JusticeBossDiagnosticUtility.WriteLandingSummary(
                    batch.mapId,
                    batch.pendingBefore,
                    pendingAfter,
                    batch.landedCount,
                    batch.landedPawnIdsByEvent.Count,
                    kindCounts,
                    elapsedMs,
                    exception);

                if (batch.landedCount <= 0)
                {
                    return;
                }

                JusticeBossTraceFileWriter.Write(
                    "LANDING_BATCH_COMPLETE",
                    "map=" + batch.mapId
                        + " pendingBefore=" + batch.pendingBefore
                        + " pendingAfter=" + pendingAfter
                        + " landed=" + batch.landedCount
                        + " events=" + batch.landedPawnIdsByEvent.Count
                        + " kindCounts=" + kindCounts
                        + " elapsedMs=" + elapsedMs.ToString("0.###"),
                    JusticeBossTraceWriteMode.Buffered);

                int tick = CurrentTick();
                if (tick < 0 || !JusticeBossTraceFileWriter.CanWriteNormal)
                {
                    return;
                }

                foreach (KeyValuePair<int, List<int>> pair in batch.landedPawnIdsByEvent)
                {
                    if (!Events.TryGetValue(pair.Key, out TrackedJusticeBossEvent? tracked))
                    {
                        continue;
                    }

                    bool finalAssaultBatch =
                        pendingAfter <= 0
                        && batch.assaultEvents.Contains(pair.Key)
                        && tracked.finalAssaultWaveLaunched;

                    if (finalAssaultBatch)
                    {
                        ArmWindow(
                            tracked,
                            JusticeBossTraceMode.FullEvent,
                            FullEventWindowTicks,
                            tick,
                            pair.Value,
                            "final-assault-wave-landed");
                    }
                    else if (pair.Value.Count >= RecentBatchPawnThreshold)
                    {
                        ArmWindow(
                            tracked,
                            JusticeBossTraceMode.RecentBatch,
                            RecentBatchWindowTicks,
                            tick,
                            pair.Value,
                            "wave-batch-landed");
                    }
                }
            }
            finally
            {
                batch.Reset();
            }
        }

        internal static bool TryBeginGameTick(out JusticeBossGameTickState state)
        {
            state = default;
            if (!AnyWindowActive || Current.Game == null)
            {
                return false;
            }

            int expectedTick = CurrentTick() + 1;
            if (!HasWindowForTick(expectedTick))
            {
                return false;
            }

            state = new JusticeBossGameTickState
            {
                active = true,
                expectedTick = expectedTick,
                startedTimestamp = Stopwatch.GetTimestamp(),
            };

            JusticeBossTraceFileWriter.Write(
                "GAME_TICK_BEGIN",
                "expectedTick=" + expectedTick
                    + " activeWindows=" + activeWindowCount,
                JusticeBossTraceWriteMode.Critical);
            return true;
        }

        internal static void EndGameTick(
            JusticeBossGameTickState state,
            Exception? exception)
        {
            int tick = CurrentTick();

            if (JusticeBossTraceFileWriter.NormalLimitReached)
            {
                StopAfterNormalLimit();
                return;
            }

            bool armedDuringTick = !state.active
                && lastWindowArmedTick == tick
                && anyWindowActive;
            if (!state.active && !armedDuringTick)
            {
                return;
            }

            if (exception != null)
            {
                JusticeBossTraceFileWriter.Write(
                    "GAME_TICK_EXCEPTION",
                    "tick=" + tick + " "
                        + JusticeBossTraceFormatting.DescribeException(exception),
                    JusticeBossTraceWriteMode.Emergency);
            }
            else
            {
                JusticeBossTraceFileWriter.Write(
                    "GAME_TICK_END",
                    "tick=" + tick
                        + " beganBeforeWindow=" + state.active
                        + " elapsedMs="
                        + JusticeBossTraceFormatting.FormatElapsed(
                            state.startedTimestamp),
                    JusticeBossTraceWriteMode.Buffered);
            }

            ExpireWindows(tick);
            JusticeBossTraceFileWriter.FlushBuffered();
        }

        internal static bool TryBeginPawnTick(
            Pawn? pawn,
            out JusticeBossPawnTickState state)
        {
            state = default;
            if (!AnyWindowActive || pawn == null)
            {
                return false;
            }

            int pawnId = pawn.thingIDNumber;
            if (!PawnToEvent.TryGetValue(pawnId, out int eventId)
                || !Events.TryGetValue(eventId, out TrackedJusticeBossEvent? tracked))
            {
                return false;
            }

            int tick = CurrentTick();
            if (!tracked.TracesPawn(pawnId, tick))
            {
                return false;
            }

            if (pawn.Dead || pawn.Destroyed || !pawn.Spawned || pawn.Map == null)
            {
                RemovePawn(pawnId, eventId);
                return false;
            }

            JusticeBossPawnTraceContext context = PushPawnContext(
                pawn,
                pawnId,
                eventId,
                tracked.mode,
                tracked.recentBatchPawnIds.Contains(pawnId));
            state = new JusticeBossPawnTickState
            {
                active = true,
                context = context,
            };
            return true;
        }

        internal static void EndPawnTick(JusticeBossPawnTickState state)
        {
            if (state.active && state.context != null)
            {
                PopPawnContext(state.context);
            }
        }

        internal static JusticeBossNestedTraceState BeginNestedStage()
        {
            return new JusticeBossNestedTraceState
            {
                active = true,
                startedTimestamp = Stopwatch.GetTimestamp(),
            };
        }

        internal static void WriteContextTrace(
            string stage,
            string? detail,
            JusticeBossTraceWriteMode mode)
        {
            JusticeBossPawnTraceContext? context = CurrentPawnContext;
            if (context == null)
            {
                JusticeBossTraceFileWriter.Write(stage, detail, mode);
                return;
            }

            StringBuilder builder = new StringBuilder(256);
            builder.Append("event=").Append(context.eventId);
            builder.Append(" pawnId=").Append(context.pawnId);
            builder.Append(" traceMode=").Append(context.mode);
            builder.Append(" isRecentBatch=").Append(context.isRecentBatch);
            if (!string.IsNullOrWhiteSpace(detail))
            {
                builder.Append(' ').Append(detail);
            }

            JusticeBossTraceFileWriter.Write(stage, builder.ToString(), mode);
        }

        private static void ArmWindow(
            TrackedJusticeBossEvent tracked,
            JusticeBossTraceMode mode,
            int windowTicks,
            int tick,
            List<int> batchPawnIds,
            string trigger)
        {
            if (!JusticeBossTraceFileWriter.CanWriteNormal)
            {
                return;
            }

            if (tracked.mode == JusticeBossTraceMode.FullEvent
                && mode == JusticeBossTraceMode.RecentBatch)
            {
                return;
            }

            int requestedStart = tick + 1;
            if (!tracked.WindowActive)
            {
                tracked.windowStartTick = requestedStart;
                tracked.hardStopTick = requestedStart + MaxWindowTicks - 1;
            }

            int requestedEnd = requestedStart + windowTicks - 1;
            if (requestedEnd > tracked.hardStopTick)
            {
                requestedEnd = tracked.hardStopTick;
            }

            bool wasActive = tracked.WindowActive;
            tracked.mode = mode;
            if (requestedStart < tracked.windowStartTick || tracked.windowStartTick < 0)
            {
                tracked.windowStartTick = requestedStart;
            }

            if (requestedEnd > tracked.windowEndTick)
            {
                tracked.windowEndTick = requestedEnd;
            }

            tracked.recentBatchPawnIds.Clear();
            for (int i = 0; i < batchPawnIds.Count; i++)
            {
                tracked.recentBatchPawnIds.Add(batchPawnIds[i]);
            }

            if (!wasActive)
            {
                activeWindowCount++;
            }

            anyWindowActive = activeWindowCount > 0;
            lastWindowArmedTick = tick;

            JusticeBossTraceFileWriter.Write(
                "TRACE_WINDOW_ARMED",
                "event=" + tracked.eventId
                    + " trigger=" + JusticeBossTraceFormatting.Sanitize(trigger)
                    + " traceMode=" + mode
                    + " fromTick=" + tracked.windowStartTick
                    + " windowEndTick=" + tracked.windowEndTick
                    + " hardStopTick=" + tracked.hardStopTick
                    + " batchPawns=" + batchPawnIds.Count
                    + " trackedPawns="
                    + (mode == JusticeBossTraceMode.FullEvent
                        ? tracked.pawnIds.Count
                        : tracked.recentBatchPawnIds.Count)
                    + " pawnIds=" + DescribePawnIds(batchPawnIds),
                JusticeBossTraceWriteMode.Critical);
        }

        private static bool HasWindowForTick(int tick)
        {
            foreach (KeyValuePair<int, TrackedJusticeBossEvent> pair in Events)
            {
                TrackedJusticeBossEvent tracked = pair.Value;
                if (tracked.WindowActive
                    && tick >= tracked.windowStartTick
                    && tick <= tracked.windowEndTick)
                {
                    return true;
                }
            }

            return false;
        }

        private static void ExpireWindows(int tick)
        {
            if (activeWindowCount <= 0)
            {
                return;
            }

            EventBuffer.Clear();
            foreach (KeyValuePair<int, TrackedJusticeBossEvent> pair in Events)
            {
                TrackedJusticeBossEvent tracked = pair.Value;
                if (tracked.WindowActive && tick >= tracked.windowEndTick)
                {
                    EventBuffer.Add(pair.Key);
                }
            }

            for (int i = 0; i < EventBuffer.Count; i++)
            {
                if (!Events.TryGetValue(
                        EventBuffer[i],
                        out TrackedJusticeBossEvent? tracked))
                {
                    continue;
                }

                JusticeBossTraceFileWriter.Write(
                    "TRACE_WINDOW_EXPIRED",
                    "event=" + tracked.eventId
                        + " traceMode=" + tracked.mode
                        + " tick=" + tick
                        + " windowEndTick=" + tracked.windowEndTick,
                    JusticeBossTraceWriteMode.Critical);
                tracked.ClearWindow();
            }

            EventBuffer.Clear();
            RecountActiveWindows();
        }

        private static void RecountActiveWindows()
        {
            int count = 0;
            foreach (KeyValuePair<int, TrackedJusticeBossEvent> pair in Events)
            {
                if (pair.Value.WindowActive)
                {
                    count++;
                }
            }

            activeWindowCount = count;
            anyWindowActive = count > 0;
        }

        private static TrackedJusticeBossEvent GetOrCreateEvent(int eventId)
        {
            if (Events.TryGetValue(eventId, out TrackedJusticeBossEvent? tracked))
            {
                return tracked;
            }

            tracked = new TrackedJusticeBossEvent
            {
                eventId = eventId,
            };
            Events[eventId] = tracked;
            return tracked;
        }

        private static void RemovePawn(int pawnId, int eventId)
        {
            PawnToEvent.Remove(pawnId);
            if (Events.TryGetValue(eventId, out TrackedJusticeBossEvent? tracked))
            {
                tracked.pawnIds.Remove(pawnId);
                tracked.recentBatchPawnIds.Remove(pawnId);
            }
        }

        private static JusticeBossPawnTraceContext PushPawnContext(
            Pawn pawn,
            int pawnId,
            int eventId,
            JusticeBossTraceMode mode,
            bool isRecentBatch)
        {
            pawnContextStack ??= new List<JusticeBossPawnTraceContext>(4);
            if (pawnContextDepth >= pawnContextStack.Count)
            {
                pawnContextStack.Add(new JusticeBossPawnTraceContext());
            }

            JusticeBossPawnTraceContext context = pawnContextStack[pawnContextDepth++];
            context.pawn = pawn;
            context.pawnId = pawnId;
            context.eventId = eventId;
            context.mode = mode;
            context.isRecentBatch = isRecentBatch;
            context.startedTimestamp = Stopwatch.GetTimestamp();
            return context;
        }

        private static void PopPawnContext(JusticeBossPawnTraceContext context)
        {
            if (pawnContextStack == null || pawnContextDepth <= 0)
            {
                return;
            }

            if (ReferenceEquals(pawnContextStack[pawnContextDepth - 1], context))
            {
                pawnContextDepth--;
                context.Clear();
                return;
            }

            for (int i = pawnContextDepth - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(pawnContextStack[i], context))
                {
                    continue;
                }

                JusticeBossTraceFileWriter.Write(
                    "TRACE_CONTEXT_MISMATCH",
                    "expectedDepth=" + pawnContextDepth + " recoveredIndex=" + i,
                    JusticeBossTraceWriteMode.Emergency);
                for (int j = pawnContextDepth - 1; j >= i; j--)
                {
                    pawnContextStack[j].Clear();
                }

                pawnContextDepth = i;
                return;
            }

            context.Clear();
        }

        private static string DescribePawnIds(List<int> pawnIds)
        {
            if (pawnIds.Count == 0)
            {
                return "none";
            }

            int limit = Math.Min(pawnIds.Count, MaxLoggedPawnIds);
            StringBuilder builder = new StringBuilder(limit * 8);
            for (int i = 0; i < limit; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                builder.Append(pawnIds[i]);
            }

            if (limit < pawnIds.Count)
            {
                builder.Append(",+").Append(pawnIds.Count - limit);
            }

            return builder.ToString();
        }

        private static double ParseElapsed(long startedTimestamp)
        {
            if (startedTimestamp <= 0L)
            {
                return -1d;
            }

            return (Stopwatch.GetTimestamp() - startedTimestamp)
                * 1000d
                / Stopwatch.Frequency;
        }
    }
}
