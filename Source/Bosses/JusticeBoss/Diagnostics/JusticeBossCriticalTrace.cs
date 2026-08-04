using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor
{
    internal static class JusticeBossDiagnosticsRuntime
    {
        internal static void Refresh()
        {
            JusticeBossDiagnosticPatchManager.Refresh();
            JusticeBossCriticalTracePatchManager.Refresh();
        }
    }

    internal sealed class JusticeBossCriticalContext
    {
        public int eventId;
        public JusticeBossDropRole role;
        public int landingPawnId = -1;
        public string? landingPawnKind;
    }

    internal struct JusticeBossCriticalLandingState
    {
        public bool active;
        public long startedTimestamp;
        public Pawn? pawn;
        public JusticeBossCriticalContext? previousContext;
    }

    internal struct JusticeBossCriticalStageState
    {
        public bool active;
        public long startedTimestamp;
        public string? stage;
        public Pawn? pawn;
        public Lord? lord;
        public CompCanBeDormant? dormantComp;
    }

    internal sealed class JusticeBossCriticalDutyFrame
    {
        public long startedTimestamp;
        public LordToil_AssaultColony? toil;
        public Lord? lord;
        public int lastPawnIndex = -1;
        public Pawn? lastPawn;
    }

    internal struct JusticeBossCriticalDutyState
    {
        public bool active;
        public JusticeBossCriticalDutyFrame? frame;
    }

    internal static class JusticeBossCriticalTrace
    {
        private const string TraceFileName = "JusticeBossCriticalTrace.log";
        private const int TraceIoErrorKey = 196840731;

        private static readonly object SyncRoot = new object();

        private static FileStream? traceStream;
        private static StreamWriter? traceWriter;
        private static long sequence;
        private static bool ioFailureReported;

        [ThreadStatic]
        private static JusticeBossCriticalContext? activeContext;

        [ThreadStatic]
        private static Stack<JusticeBossCriticalDutyFrame>? dutyFrames;

        internal static string TraceFilePath =>
            Path.Combine(GenFilePaths.SaveDataFolderPath, TraceFileName);

        internal static bool Enabled =>
            JusticeBossDiagnosticUtility.Enabled
            && JusticeBossCriticalTracePatchManager.Installed;

        internal static bool HasLandingContext =>
            Enabled && activeContext != null;

        internal static void OpenSession()
        {
            lock (SyncRoot)
            {
                CloseWriterUnsafe();
                try
                {
                    traceStream = new FileStream(
                        TraceFilePath,
                        FileMode.Append,
                        FileAccess.Write,
                        FileShare.ReadWrite);
                    traceWriter = new StreamWriter(
                        traceStream,
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
                    {
                        AutoFlush = true,
                    };
                    sequence = 0L;
                    ioFailureReported = false;
                    WriteLineUnsafe(
                        "SESSION_BEGIN",
                        "process=" + Process.GetCurrentProcess().Id
                            + " file=" + Sanitize(TraceFilePath),
                        includeContext: false);
                }
                catch (Exception exception)
                {
                    CloseWriterUnsafe();
                    ReportIoFailure(exception);
                }
            }
        }

        internal static void CloseSession(string reason)
        {
            lock (SyncRoot)
            {
                if (traceWriter != null)
                {
                    WriteLineUnsafe(
                        "SESSION_END",
                        "reason=" + Sanitize(reason),
                        includeContext: false);
                }

                CloseWriterUnsafe();
            }

            activeContext = null;
            dutyFrames = null;
        }

        internal static JusticeBossCriticalLandingState BeginLanding(
            PendingJusticeBossDrop? entry)
        {
            if (!Enabled || entry?.pawn == null)
            {
                return default;
            }

            Pawn pawn = entry.pawn;
            JusticeBossCriticalLandingState state =
                new JusticeBossCriticalLandingState
                {
                    active = true,
                    startedTimestamp = Stopwatch.GetTimestamp(),
                    pawn = pawn,
                    previousContext = activeContext,
                };

            activeContext = new JusticeBossCriticalContext
            {
                eventId = entry.justiceEventId,
                role = entry.role,
                landingPawnId = pawn.thingIDNumber,
                landingPawnKind = pawn.kindDef?.defName,
            };

            Write(
                "LANDING_BEGIN",
                DescribePawn(pawn)
                    + " registeredTick=" + entry.registeredTick
                    + " anchor=" + entry.anchorCell);
            return state;
        }

        internal static Exception? EndLanding(
            Exception? exception,
            JusticeBossCriticalLandingState state)
        {
            if (!state.active)
            {
                return exception;
            }

            string stage = exception == null
                ? "LANDING_END"
                : "LANDING_EXCEPTION";
            string detail = DescribePawn(state.pawn)
                + " elapsedMs=" + FormatElapsed(state.startedTimestamp);
            if (exception != null)
            {
                detail += " exception=" + Sanitize(exception.GetType().FullName)
                    + " message=" + Sanitize(exception.Message);
            }

            Write(stage, detail, requireEnabled: false);
            activeContext = state.previousContext;
            return exception;
        }

        internal static JusticeBossCriticalStageState BeginStage(
            string stage,
            Pawn? pawn,
            Lord? lord = null,
            CompCanBeDormant? dormantComp = null,
            string? extra = null)
        {
            if (!HasLandingContext)
            {
                return default;
            }

            JusticeBossCriticalStageState state =
                new JusticeBossCriticalStageState
                {
                    active = true,
                    startedTimestamp = Stopwatch.GetTimestamp(),
                    stage = stage,
                    pawn = pawn,
                    lord = lord,
                    dormantComp = dormantComp,
                };

            StringBuilder detail = new StringBuilder(256);
            detail.Append(DescribePawn(pawn));
            if (lord != null)
            {
                detail.Append(" lord=").Append(DescribeLord(lord));
            }

            if (dormantComp != null)
            {
                detail.Append(" awakeBefore=").Append(SafeAwake(dormantComp));
                detail.Append(" wokeUpTickBefore=").Append(dormantComp.wokeUpTick);
                detail.Append(" wakeUpOnTickBefore=").Append(dormantComp.wakeUpOnTick);
            }

            if (!string.IsNullOrWhiteSpace(extra))
            {
                detail.Append(' ').Append(extra);
            }

            Write(stage + "_BEGIN", detail.ToString());
            return state;
        }

        internal static Exception? EndStage(
            Exception? exception,
            JusticeBossCriticalStageState state)
        {
            if (!state.active || string.IsNullOrEmpty(state.stage))
            {
                return exception;
            }

            StringBuilder detail = new StringBuilder(256);
            detail.Append(DescribePawn(state.pawn));
            if (state.lord != null)
            {
                detail.Append(" lord=").Append(DescribeLord(state.lord));
            }

            if (state.dormantComp != null)
            {
                detail.Append(" awakeAfter=").Append(SafeAwake(state.dormantComp));
                detail.Append(" wokeUpTickAfter=").Append(state.dormantComp.wokeUpTick);
                detail.Append(" wakeUpOnTickAfter=").Append(state.dormantComp.wakeUpOnTick);
            }

            detail.Append(" elapsedMs=").Append(
                FormatElapsed(state.startedTimestamp));
            if (exception != null)
            {
                detail.Append(" exception=").Append(
                    Sanitize(exception.GetType().FullName));
                detail.Append(" message=").Append(Sanitize(exception.Message));
            }

            Write(
                state.stage + (exception == null ? "_END" : "_EXCEPTION"),
                detail.ToString(),
                requireEnabled: false);
            return exception;
        }

        internal static JusticeBossCriticalDutyState BeginDutyRefresh(
            LordToil_AssaultColony? toil)
        {
            if (!HasLandingContext || toil?.lord == null)
            {
                return default;
            }

            JusticeBossCriticalDutyFrame frame =
                new JusticeBossCriticalDutyFrame
                {
                    startedTimestamp = Stopwatch.GetTimestamp(),
                    toil = toil,
                    lord = toil.lord,
                };

            dutyFrames ??= new Stack<JusticeBossCriticalDutyFrame>();
            dutyFrames.Push(frame);
            Write(
                "UPDATE_DUTIES_BEGIN",
                "lord=" + DescribeLord(frame.lord)
                    + " toil=" + Sanitize(toil.GetType().FullName));

            return new JusticeBossCriticalDutyState
            {
                active = true,
                frame = frame,
            };
        }

        internal static Pawn TraceDutyPawn(List<Pawn> pawns, int index)
        {
            Pawn pawn = pawns[index];
            if (!HasLandingContext
                || dutyFrames == null
                || dutyFrames.Count == 0)
            {
                return pawn;
            }

            JusticeBossCriticalDutyFrame frame = dutyFrames.Peek();
            if (frame.lastPawn != null)
            {
                Write(
                    "UPDATE_DUTIES_PAWN_END",
                    "index=" + frame.lastPawnIndex
                        + " " + DescribePawn(frame.lastPawn));
            }

            frame.lastPawnIndex = index;
            frame.lastPawn = pawn;
            Write(
                "UPDATE_DUTIES_PAWN_BEGIN",
                "index=" + index + " " + DescribePawn(pawn));
            return pawn;
        }

        internal static Exception? EndDutyRefresh(
            Exception? exception,
            JusticeBossCriticalDutyState state)
        {
            if (!state.active || state.frame == null)
            {
                return exception;
            }

            JusticeBossCriticalDutyFrame frame = state.frame;
            if (frame.lastPawn != null)
            {
                Write(
                    "UPDATE_DUTIES_PAWN_END",
                    "index=" + frame.lastPawnIndex
                        + " " + DescribePawn(frame.lastPawn),
                    requireEnabled: false);
            }

            string detail = "lord=" + DescribeLord(frame.lord)
                + " elapsedMs=" + FormatElapsed(frame.startedTimestamp);
            if (exception != null)
            {
                detail += " exception=" + Sanitize(exception.GetType().FullName)
                    + " message=" + Sanitize(exception.Message);
            }

            Write(
                exception == null
                    ? "UPDATE_DUTIES_END"
                    : "UPDATE_DUTIES_EXCEPTION",
                detail,
                requireEnabled: false);

            if (dutyFrames != null && dutyFrames.Count > 0)
            {
                if (ReferenceEquals(dutyFrames.Peek(), frame))
                {
                    dutyFrames.Pop();
                }
                else
                {
                    Stack<JusticeBossCriticalDutyFrame> rebuilt =
                        new Stack<JusticeBossCriticalDutyFrame>();
                    while (dutyFrames.Count > 0)
                    {
                        JusticeBossCriticalDutyFrame current = dutyFrames.Pop();
                        if (ReferenceEquals(current, frame))
                        {
                            break;
                        }

                        rebuilt.Push(current);
                    }

                    while (rebuilt.Count > 0)
                    {
                        dutyFrames.Push(rebuilt.Pop());
                    }
                }
            }

            return exception;
        }

        internal static void Write(
            string stage,
            string detail,
            bool requireEnabled = true)
        {
            if (requireEnabled && !Enabled)
            {
                return;
            }

            lock (SyncRoot)
            {
                if (traceWriter == null)
                {
                    return;
                }

                WriteLineUnsafe(stage, detail, includeContext: true);
            }
        }

        internal static string DescribePawn(Pawn? pawn)
        {
            if (pawn == null)
            {
                return "actualPawnId=-1 actualPawnKind=null actualPawnRace=null";
            }

            try
            {
                Pawn_JobTracker? jobs = pawn.jobs;
                Lord? lord = pawn.GetLord();
                CompCanBeDormant? dormant = pawn.TryGetComp<CompCanBeDormant>();
                return "actualPawnId=" + pawn.thingIDNumber
                    + " actualPawnKind=" + Sanitize(pawn.kindDef?.defName)
                    + " actualPawnRace=" + Sanitize(pawn.def?.defName)
                    + " job=" + Sanitize(jobs?.curJob?.def?.defName)
                    + " jobDriver=" + Sanitize(jobs?.curDriver?.GetType().FullName)
                    + " duty=" + Sanitize(pawn.mindState?.duty?.def?.defName)
                    + " lord=" + (lord == null ? "null" : DescribeLord(lord))
                    + " spawned=" + pawn.Spawned
                    + " map=" + (pawn.Map?.uniqueID ?? -1)
                    + " position=" + (pawn.Spawned ? pawn.Position.ToString() : "unspawned")
                    + " downed=" + pawn.Downed
                    + " dead=" + pawn.Dead
                    + " dormant=" + (dormant == null ? "none" : (!SafeAwake(dormant)).ToString());
            }
            catch (Exception exception)
            {
                return "actualPawnId=" + pawn.thingIDNumber
                    + " describeException=" + Sanitize(exception.GetType().FullName);
            }
        }

        internal static string DescribeLord(Lord? lord)
        {
            if (lord == null)
            {
                return "null";
            }

            try
            {
                return lord.GetHashCode()
                    + ":loadID=" + lord.loadID
                    + ":pawns=" + (lord.ownedPawns?.Count ?? -1)
                    + ":job=" + Sanitize(lord.LordJob?.GetType().FullName)
                    + ":toil=" + Sanitize(lord.CurLordToil?.GetType().FullName);
            }
            catch (Exception exception)
            {
                return lord.GetHashCode()
                    + ":describeException="
                    + Sanitize(exception.GetType().FullName);
            }
        }

        internal static string Sanitize(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "null";
            }

            return value!
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Replace('\t', ' ');
        }

        private static string FormatElapsed(long startedTimestamp)
        {
            if (startedTimestamp <= 0L)
            {
                return "-1";
            }

            double milliseconds =
                (Stopwatch.GetTimestamp() - startedTimestamp)
                * 1000d
                / Stopwatch.Frequency;
            return milliseconds.ToString("0.###");
        }

        private static bool SafeAwake(CompCanBeDormant comp)
        {
            try
            {
                return comp.Awake;
            }
            catch
            {
                return false;
            }
        }

        private static void WriteLineUnsafe(
            string stage,
            string detail,
            bool includeContext)
        {
            if (traceWriter == null || traceStream == null)
            {
                return;
            }

            try
            {
                StringBuilder line = new StringBuilder(512);
                line.Append("seq=").Append(++sequence);
                line.Append(" utc=").Append(DateTime.UtcNow.ToString("O"));
                line.Append(" tick=").Append(Find.TickManager?.TicksGame ?? -1);
                line.Append(" thread=").Append(
                    Thread.CurrentThread.ManagedThreadId);
                line.Append(" stage=").Append(Sanitize(stage));

                if (includeContext && activeContext != null)
                {
                    line.Append(" event=").Append(activeContext.eventId);
                    line.Append(" role=").Append(activeContext.role);
                    line.Append(" landingPawnId=").Append(
                        activeContext.landingPawnId);
                    line.Append(" landingPawnKind=").Append(
                        Sanitize(activeContext.landingPawnKind));
                }

                if (!string.IsNullOrWhiteSpace(detail))
                {
                    line.Append(' ').Append(detail);
                }

                traceWriter.WriteLine(line.ToString());
                traceWriter.Flush();
                traceStream.Flush();
            }
            catch (Exception exception)
            {
                CloseWriterUnsafe();
                ReportIoFailure(exception);
            }
        }

        private static void CloseWriterUnsafe()
        {
            try
            {
                traceWriter?.Dispose();
            }
            catch
            {
            }

            try
            {
                traceStream?.Dispose();
            }
            catch
            {
            }

            traceWriter = null;
            traceStream = null;
        }

        private static void ReportIoFailure(Exception exception)
        {
            if (ioFailureReported)
            {
                return;
            }

            ioFailureReported = true;
            Log.ErrorOnce(
                "[MAP JusticeBoss CriticalTrace] 无法写入关键轨迹文件："
                    + exception,
                TraceIoErrorKey);
        }
    }

    internal static class JusticeBossCriticalTracePatchManager
    {
        private const string HarmonyId =
            "MAP_MechanoidMechanitor.JusticeBossCriticalTrace";

        private static readonly Harmony Harmony = new Harmony(HarmonyId);
        private static bool installed;

        internal static bool Installed => installed;

        internal static void Refresh()
        {
            bool shouldInstall = JusticeBossDiagnosticUtility.Enabled;
            if (shouldInstall == installed)
            {
                return;
            }

            if (shouldInstall)
            {
                Install();
            }
            else
            {
                Uninstall();
            }
        }

        private static void Install()
        {
            if (installed)
            {
                return;
            }

            JusticeBossCriticalTrace.OpenSession();
            try
            {
                PatchRequired(
                    "Tracker.CompleteLanding",
                    AccessTools.Method(
                        typeof(MapComponent_JusticeBossDropTracker),
                        "CompleteLanding",
                        new[] { typeof(PendingJusticeBossDrop) }),
                    nameof(JusticeBossCriticalTraceHooks.LandingPrefix),
                    null,
                    nameof(JusticeBossCriticalTraceHooks.LandingFinalizer));

                PatchRequired(
                    "Lord.AddPawn",
                    AccessTools.Method(
                        typeof(Lord),
                        nameof(Lord.AddPawn),
                        new[] { typeof(Pawn) }),
                    nameof(JusticeBossCriticalTraceHooks.AddPawnPrefix),
                    null,
                    nameof(JusticeBossCriticalTraceHooks.StageFinalizer));

                PatchRequired(
                    "LordToil_AssaultColony.UpdateAllDuties",
                    AccessTools.Method(
                        typeof(LordToil_AssaultColony),
                        nameof(LordToil_AssaultColony.UpdateAllDuties),
                        Type.EmptyTypes),
                    nameof(JusticeBossCriticalTraceHooks.DutyPrefix),
                    nameof(JusticeBossCriticalTraceHooks.DutyTranspiler),
                    nameof(JusticeBossCriticalTraceHooks.DutyFinalizer));

                PatchRequired(
                    "PawnJobTracker.EndCurrentJob",
                    AccessTools.Method(
                        typeof(Pawn_JobTracker),
                        nameof(Pawn_JobTracker.EndCurrentJob),
                        new[] { typeof(JobCondition), typeof(bool), typeof(bool) }),
                    nameof(JusticeBossCriticalTraceHooks.EndJobPrefix),
                    null,
                    nameof(JusticeBossCriticalTraceHooks.StageFinalizer));

                PatchRequired(
                    "CompCanBeDormant.WakeUp",
                    AccessTools.Method(
                        typeof(CompCanBeDormant),
                        nameof(CompCanBeDormant.WakeUp),
                        Type.EmptyTypes),
                    nameof(JusticeBossCriticalTraceHooks.WakeUpPrefix),
                    null,
                    nameof(JusticeBossCriticalTraceHooks.StageFinalizer));

                installed = true;
                JusticeBossCriticalTrace.Write(
                    "PATCH_INSTALL_END",
                    "result=success file="
                        + JusticeBossCriticalTrace.Sanitize(
                            JusticeBossCriticalTrace.TraceFilePath));
            }
            catch (Exception exception)
            {
                Harmony.UnpatchAll(HarmonyId);
                installed = false;
                JusticeBossCriticalTrace.Write(
                    "PATCH_INSTALL_EXCEPTION",
                    "exception="
                        + JusticeBossCriticalTrace.Sanitize(
                            exception.GetType().FullName)
                        + " message="
                        + JusticeBossCriticalTrace.Sanitize(exception.Message),
                    requireEnabled: false);
                JusticeBossCriticalTrace.CloseSession("install-failed");
                Log.Error(
                    "[MAP JusticeBoss CriticalTrace] 关键轨迹补丁安装失败："
                        + exception);
            }
        }

        private static void Uninstall()
        {
            if (installed)
            {
                JusticeBossCriticalTrace.Write(
                    "PATCH_UNINSTALL_BEGIN",
                    "result=pending",
                    requireEnabled: false);
                Harmony.UnpatchAll(HarmonyId);
                installed = false;
            }

            JusticeBossCriticalTrace.CloseSession("disabled");
        }

        private static void PatchRequired(
            string description,
            MethodBase? original,
            string? prefixName,
            string? transpilerName,
            string? finalizerName)
        {
            if (original == null)
            {
                throw new MissingMethodException(description);
            }

            MethodInfo? prefix = ResolveHook(prefixName);
            MethodInfo? transpiler = ResolveHook(transpilerName);
            MethodInfo? finalizer = ResolveHook(finalizerName);

            Harmony.Patch(
                original,
                prefix: prefix == null ? null : new HarmonyMethod(prefix),
                postfix: null,
                transpiler: transpiler == null
                    ? null
                    : new HarmonyMethod(transpiler),
                finalizer: finalizer == null
                    ? null
                    : new HarmonyMethod(finalizer));
        }

        private static MethodInfo? ResolveHook(string? name)
        {
            if (name == null)
            {
                return null;
            }

            MethodInfo? method = AccessTools.Method(
                typeof(JusticeBossCriticalTraceHooks),
                name);
            if (method == null)
            {
                throw new MissingMethodException(
                    typeof(JusticeBossCriticalTraceHooks).FullName,
                    name);
            }

            return method;
        }
    }

    internal static class JusticeBossCriticalTraceHooks
    {
        public static void LandingPrefix(
            PendingJusticeBossDrop entry,
            out JusticeBossCriticalLandingState __state)
        {
            __state = JusticeBossCriticalTrace.BeginLanding(entry);
        }

        public static Exception? LandingFinalizer(
            Exception? __exception,
            JusticeBossCriticalLandingState __state)
        {
            return JusticeBossCriticalTrace.EndLanding(
                __exception,
                __state);
        }

        public static void AddPawnPrefix(
            Lord __instance,
            Pawn p,
            out JusticeBossCriticalStageState __state)
        {
            __state = JusticeBossCriticalTrace.BeginStage(
                "ADD_PAWN",
                p,
                __instance,
                extra: "beforeCount="
                    + (__instance?.ownedPawns?.Count ?? -1));
        }

        public static void EndJobPrefix(
            Pawn ___pawn,
            JobCondition condition,
            bool startNewJob,
            bool canReturnToPool,
            out JusticeBossCriticalStageState __state)
        {
            __state = JusticeBossCriticalTrace.BeginStage(
                "END_JOB",
                ___pawn,
                extra: "condition=" + condition
                    + " startNewJob=" + startNewJob
                    + " canReturnToPool=" + canReturnToPool);
        }

        public static void WakeUpPrefix(
            CompCanBeDormant __instance,
            out JusticeBossCriticalStageState __state)
        {
            __state = JusticeBossCriticalTrace.BeginStage(
                "WAKE_UP",
                __instance?.parent as Pawn,
                dormantComp: __instance);
        }

        public static Exception? StageFinalizer(
            Exception? __exception,
            JusticeBossCriticalStageState __state)
        {
            return JusticeBossCriticalTrace.EndStage(
                __exception,
                __state);
        }

        public static void DutyPrefix(
            LordToil_AssaultColony __instance,
            out JusticeBossCriticalDutyState __state)
        {
            __state = JusticeBossCriticalTrace.BeginDutyRefresh(__instance);
        }

        public static IEnumerable<CodeInstruction> DutyTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes =
                new List<CodeInstruction>(instructions);
            MethodInfo? pawnIndexer = AccessTools.PropertyGetter(
                typeof(List<Pawn>),
                "Item");
            MethodInfo? traceMethod = AccessTools.Method(
                typeof(JusticeBossCriticalTraceHooks),
                nameof(GetPawnAndTraceDutyIteration));
            if (pawnIndexer == null || traceMethod == null)
            {
                throw new MissingMethodException(
                    "Unable to resolve LordToil_AssaultColony pawn indexer trace methods.");
            }

            int matchCount = 0;
            for (int i = 0; i < codes.Count; i++)
            {
                if (!codes[i].Calls(pawnIndexer))
                {
                    continue;
                }

                codes[i].opcode = OpCodes.Call;
                codes[i].operand = traceMethod;
                matchCount++;
            }

            if (matchCount != 1)
            {
                throw new InvalidOperationException(
                    "LordToil_AssaultColony.UpdateAllDuties expected exactly one "
                        + "List<Pawn>.get_Item call but found " + matchCount + ".");
            }

            return codes;
        }

        public static Pawn GetPawnAndTraceDutyIteration(
            List<Pawn> pawns,
            int index)
        {
            return JusticeBossCriticalTrace.TraceDutyPawn(pawns, index);
        }

        public static Exception? DutyFinalizer(
            Exception? __exception,
            JusticeBossCriticalDutyState __state)
        {
            return JusticeBossCriticalTrace.EndDutyRefresh(
                __exception,
                __state);
        }
    }
}
