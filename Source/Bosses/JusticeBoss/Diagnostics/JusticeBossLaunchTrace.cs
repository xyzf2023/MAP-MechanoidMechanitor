using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using Verse;
using Verse.Profile;

namespace MAP_MechanoidMechanitor
{
    internal struct JusticeBossWaveTraceContext
    {
        public bool active;
        public int eventId;
        public int waveIndex;
        public int totalWaves;
        public long startedTimestamp;
    }

    internal static class JusticeBossLaunchTracePatchManager
    {
        internal const int LandingSummaryPawnThreshold = 8;

        private const string HarmonyId =
            "MAP_MechanoidMechanitor.JusticeBossLaunchTrace";

        private static readonly Harmony Harmony = new Harmony(HarmonyId);
        private static bool installed;

        [ThreadStatic]
        private static Stack<JusticeBossWaveTraceContext>? waveContextStack;

        internal static bool Installed => installed;

        internal static bool Active =>
            installed
            && JusticeBossDiagnosticUtility.Enabled
            && JusticeBossTraceFileWriter.IsOpen;

        internal static bool CanWriteNormal =>
            Active && JusticeBossTraceFileWriter.CanWriteNormal;

        internal static bool CanWriteEmergency =>
            installed && JusticeBossTraceFileWriter.IsOpen;

        internal static void Refresh()
        {
            bool shouldInstall = JusticeBossDiagnosticUtility.Enabled;
            if (shouldInstall)
            {
                if (installed && JusticeBossTraceFileWriter.IsOpen)
                {
                    return;
                }

                Install();
                return;
            }

            Uninstall();
        }

        internal static int ResolveWaveIndex(int justiceEventId)
        {
            Stack<JusticeBossWaveTraceContext>? stack = waveContextStack;
            if (stack == null || stack.Count == 0)
            {
                return 0;
            }

            JusticeBossWaveTraceContext context = stack.Peek();
            return context.active && context.eventId == justiceEventId
                ? context.waveIndex
                : 0;
        }

        internal static void WriteLandingBatchSummary(
            int mapId,
            int pendingBefore,
            int pendingAfter,
            int landedCount,
            int eventCount,
            string kindCounts,
            double elapsedMs,
            Exception? exception)
        {
            if (landedCount < LandingSummaryPawnThreshold
                && !(pendingBefore > 0 && pendingAfter <= 0)
                && exception == null)
            {
                return;
            }

            Write(
                exception == null
                    ? "LANDING_BATCH_COMPLETE"
                    : "LANDING_BATCH_EXCEPTION",
                "map=" + mapId
                    + " pendingBefore=" + pendingBefore
                    + " pendingAfter=" + pendingAfter
                    + " landed=" + landedCount
                    + " events=" + eventCount
                    + " kindCounts=" + JusticeBossTraceFormatting.Sanitize(kindCounts)
                    + " elapsedMs=" + elapsedMs.ToString("0.###")
                    + (exception == null
                        ? string.Empty
                        : " " + JusticeBossTraceFormatting.DescribeException(exception)),
                exception == null
                    ? (pendingBefore > 0 && pendingAfter <= 0
                        ? JusticeBossTraceWriteMode.Critical
                        : JusticeBossTraceWriteMode.Buffered)
                    : JusticeBossTraceWriteMode.Emergency);
        }

        internal static void Write(
            string stage,
            string? detail,
            JusticeBossTraceWriteMode mode)
        {
            if (mode == JusticeBossTraceWriteMode.Emergency)
            {
                if (!CanWriteEmergency)
                {
                    return;
                }
            }
            else if (!CanWriteNormal)
            {
                return;
            }

            JusticeBossTraceFileWriter.Write(stage, detail, mode);
        }

        internal static void WriteException(
            string stage,
            string detail,
            Exception exception)
        {
            Write(
                stage,
                detail + " " + JusticeBossTraceFormatting.DescribeException(exception),
                JusticeBossTraceWriteMode.Emergency);
        }

        private static void Install()
        {
            if (installed)
            {
                Harmony.UnpatchAll(HarmonyId);
                installed = false;
            }

            ClearWaveContexts();

            if (!JusticeBossTraceFileWriter.OpenSession())
            {
                return;
            }

            try
            {
                PatchRequired(
                    "Controller.InitializeOnArrival",
                    AccessTools.Method(
                        typeof(CompJusticeBossController),
                        nameof(CompJusticeBossController.InitializeOnArrival),
                        Type.EmptyTypes),
                    null,
                    nameof(JusticeBossLaunchTraceHooks.ControllerInitializedPostfix),
                    null);

                PatchRequired(
                    "Controller.TrySpawnWave",
                    AccessTools.Method(
                        typeof(CompJusticeBossController),
                        "TrySpawnWave",
                        new[] { typeof(bool) }),
                    nameof(JusticeBossLaunchTraceHooks.WaveAttemptPrefix),
                    null,
                    nameof(JusticeBossLaunchTraceHooks.WaveAttemptFinalizer));

                PatchRequired(
                    "Controller.PostDestroy",
                    AccessTools.Method(
                        typeof(CompJusticeBossController),
                        nameof(CompJusticeBossController.PostDestroy),
                        new[] { typeof(DestroyMode), typeof(Map) }),
                    null,
                    nameof(JusticeBossLaunchTraceHooks.ControllerDestroyedPostfix),
                    null);

                PatchRequired(
                    "Controller.Notify_Killed",
                    AccessTools.Method(
                        typeof(CompJusticeBossController),
                        nameof(CompJusticeBossController.Notify_Killed),
                        new[] { typeof(Map), typeof(DamageInfo?) }),
                    null,
                    nameof(JusticeBossLaunchTraceHooks.ControllerKilledPostfix),
                    null);

                PatchRequired(
                    "MemoryUtility.ClearAllMapsAndWorld",
                    AccessTools.Method(
                        typeof(MemoryUtility),
                        nameof(MemoryUtility.ClearAllMapsAndWorld),
                        Type.EmptyTypes),
                    null,
                    nameof(JusticeBossLaunchTraceHooks.ClearAllMapsPostfix),
                    null);

                installed = true;
                Write(
                    "PATCH_INSTALL_END",
                    "module=launchTrace result=success"
                        + " focus=wave-generation-and-drop-launch"
                        + " landingSummaryThreshold="
                        + LandingSummaryPawnThreshold
                        + " file="
                        + JusticeBossTraceFormatting.Sanitize(
                            JusticeBossTraceFileWriter.TraceFilePath),
                    JusticeBossTraceWriteMode.Critical);
            }
            catch (Exception exception)
            {
                Harmony.UnpatchAll(HarmonyId);
                installed = false;
                ClearWaveContexts();
                JusticeBossTraceFileWriter.Write(
                    "PATCH_INSTALL_EXCEPTION",
                    "module=launchTrace "
                        + JusticeBossTraceFormatting.DescribeException(exception),
                    JusticeBossTraceWriteMode.Emergency);
                JusticeBossTraceFileWriter.CloseSession("install-failed");
                Log.Error(
                    "[MAP-机械族机械师] 正义 BOSS 轨迹： 生成与发射轨迹补丁安装失败："
                        + exception);
            }
        }

        private static void Uninstall()
        {
            if (installed)
            {
                Write(
                    "PATCH_UNINSTALL_BEGIN",
                    "module=launchTrace result=pending",
                    JusticeBossTraceWriteMode.Emergency);
                Harmony.UnpatchAll(HarmonyId);
                installed = false;
            }

            ClearWaveContexts();
            JusticeBossTraceFileWriter.CloseSession("disabled");
        }

        private static void PatchRequired(
            string description,
            MethodBase? original,
            string? prefixName,
            string? postfixName,
            string? finalizerName)
        {
            if (original == null)
            {
                throw new MissingMethodException(description);
            }

            MethodInfo? prefix = ResolveHook(prefixName);
            MethodInfo? postfix = ResolveHook(postfixName);
            MethodInfo? finalizer = ResolveHook(finalizerName);

            Harmony.Patch(
                original,
                prefix: prefix == null ? null : new HarmonyMethod(prefix),
                postfix: postfix == null ? null : new HarmonyMethod(postfix),
                transpiler: null,
                finalizer: finalizer == null ? null : new HarmonyMethod(finalizer));
        }

        private static MethodInfo? ResolveHook(string? name)
        {
            if (name == null)
            {
                return null;
            }

            MethodInfo? method = AccessTools.Method(
                typeof(JusticeBossLaunchTraceHooks),
                name);
            if (method == null)
            {
                throw new MissingMethodException(
                    typeof(JusticeBossLaunchTraceHooks).FullName,
                    name);
            }

            return method;
        }

        internal static JusticeBossWaveTraceContext BeginWaveAttempt(
            int eventId,
            int waveCount,
            int totalWaves)
        {
            bool contextActive = Active;
            JusticeBossWaveTraceContext context =
                new JusticeBossWaveTraceContext
                {
                    active = contextActive,
                    eventId = eventId,
                    waveIndex = waveCount + 1,
                    totalWaves = totalWaves,
                    startedTimestamp = contextActive
                        ? Stopwatch.GetTimestamp()
                        : 0L,
                };

            if (!context.active)
            {
                return context;
            }

            waveContextStack ??= new Stack<JusticeBossWaveTraceContext>();
            waveContextStack.Push(context);
            Write(
                "WAVE_ATTEMPT_BEGIN",
                "event=" + eventId
                    + " wave=" + context.waveIndex
                    + " waveCountBefore=" + waveCount
                    + " totalWaves=" + totalWaves,
                JusticeBossTraceWriteMode.Critical);
            return context;
        }

        internal static void EndWaveAttempt(
            JusticeBossWaveTraceContext context,
            int waveCountAfter,
            int totalWavesAfter,
            Exception? exception)
        {
            if (!context.active)
            {
                return;
            }

            try
            {
                if (exception != null)
                {
                    WriteException(
                        "WAVE_ATTEMPT_EXCEPTION",
                        "event=" + context.eventId
                            + " wave=" + context.waveIndex
                            + " waveCountAfter=" + waveCountAfter
                            + " totalWaves=" + totalWavesAfter
                            + " elapsedMs="
                            + JusticeBossTraceFormatting.FormatElapsed(
                                context.startedTimestamp),
                        exception);
                }
                else
                {
                    Write(
                        "WAVE_ATTEMPT_END",
                        "event=" + context.eventId
                            + " wave=" + context.waveIndex
                            + " waveCountAfter=" + waveCountAfter
                            + " totalWaves=" + totalWavesAfter
                            + " completed="
                            + (waveCountAfter >= context.waveIndex)
                            + " elapsedMs="
                            + JusticeBossTraceFormatting.FormatElapsed(
                                context.startedTimestamp),
                        JusticeBossTraceWriteMode.Critical);

                    if (waveCountAfter >= context.waveIndex)
                    {
                        Write(
                            "WAVE_PROGRESS",
                            "event=" + context.eventId
                                + " wave=" + waveCountAfter
                                + " totalWaves=" + totalWavesAfter
                                + " final="
                                + (waveCountAfter >= totalWavesAfter),
                            JusticeBossTraceWriteMode.Critical);
                    }
                }
            }
            finally
            {
                PopWaveContext(context);
            }
        }

        internal static void NotifyEventRegistered(int eventId, int mapId)
        {
            Write(
                "EVENT_REGISTERED",
                "event=" + eventId + " map=" + mapId,
                JusticeBossTraceWriteMode.Critical);
        }

        internal static void NotifyEventCleared(int eventId, string reason)
        {
            Write(
                "EVENT_CLEARED",
                "event=" + eventId
                    + " reason=" + JusticeBossTraceFormatting.Sanitize(reason),
                JusticeBossTraceWriteMode.Critical);
        }

        internal static void ResetForWorldClear()
        {
            int contextCount = waveContextStack?.Count ?? 0;
            ClearWaveContexts();
            Write(
                "TRACE_STATE_RESET",
                "reason=maps-and-world-cleared waveContexts=" + contextCount,
                JusticeBossTraceWriteMode.Critical);
            JusticeBossTraceFileWriter.FlushBuffered();
        }

        private static void PopWaveContext(JusticeBossWaveTraceContext expected)
        {
            Stack<JusticeBossWaveTraceContext>? stack = waveContextStack;
            if (stack == null || stack.Count == 0)
            {
                Write(
                    "TRACE_CONTEXT_MISMATCH",
                    "reason=missing-wave-context"
                        + " event=" + expected.eventId
                        + " wave=" + expected.waveIndex,
                    JusticeBossTraceWriteMode.Emergency);
                return;
            }

            JusticeBossWaveTraceContext actual = stack.Pop();
            if (actual.eventId != expected.eventId
                || actual.waveIndex != expected.waveIndex)
            {
                Write(
                    "TRACE_CONTEXT_MISMATCH",
                    "reason=unexpected-wave-context"
                        + " expectedEvent=" + expected.eventId
                        + " expectedWave=" + expected.waveIndex
                        + " actualEvent=" + actual.eventId
                        + " actualWave=" + actual.waveIndex,
                    JusticeBossTraceWriteMode.Emergency);
            }
        }

        private static void ClearWaveContexts()
        {
            waveContextStack?.Clear();
            waveContextStack = null;
        }
    }

    internal static class JusticeBossLaunchTraceHooks
    {
        public static void ControllerInitializedPostfix(
            CompJusticeBossController __instance)
        {
            JusticeBossLaunchTracePatchManager.NotifyEventRegistered(
                __instance?.JusticeEventId ?? 0,
                __instance?.parent?.Map?.uniqueID ?? -1);
        }

        public static void WaveAttemptPrefix(
            int ___justiceEventId,
            int ___waveCount,
            int ___difficultyTotalWaves,
            out JusticeBossWaveTraceContext __state)
        {
            __state = JusticeBossLaunchTracePatchManager.BeginWaveAttempt(
                ___justiceEventId,
                ___waveCount,
                ___difficultyTotalWaves);
        }

        public static Exception? WaveAttemptFinalizer(
            Exception? __exception,
            int ___waveCount,
            int ___difficultyTotalWaves,
            JusticeBossWaveTraceContext __state)
        {
            JusticeBossLaunchTracePatchManager.EndWaveAttempt(
                __state,
                ___waveCount,
                ___difficultyTotalWaves,
                __exception);
            return __exception;
        }

        public static void ControllerDestroyedPostfix(
            CompJusticeBossController __instance)
        {
            JusticeBossLaunchTracePatchManager.NotifyEventCleared(
                __instance?.JusticeEventId ?? 0,
                "controller-destroyed");
        }

        public static void ControllerKilledPostfix(
            CompJusticeBossController __instance)
        {
            JusticeBossLaunchTracePatchManager.NotifyEventCleared(
                __instance?.JusticeEventId ?? 0,
                "boss-killed");
        }

        public static void ClearAllMapsPostfix()
        {
            JusticeBossLaunchTracePatchManager.ResetForWorldClear();
        }
    }
}
