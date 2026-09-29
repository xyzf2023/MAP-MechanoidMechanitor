using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor
{
    internal static class JusticeBossDiagnosticsRuntime
    {
        internal static void Refresh()
        {
            JusticeBossLaunchTracePatchManager.Refresh();
            JusticeBossDiagnosticSummaryPatchManager.Refresh();
        }
    }

    internal struct JusticeBossSummaryScopeState
    {
        public bool active;
        public long startedTimestamp;
        public string? phase;
    }

    public static class JusticeBossDiagnosticUtility
    {
        private const string LogPrefix = "[MAP-机械族机械师] 正义 BOSS 诊断：";

        internal const double WaveSlowMs = 1000d;
        internal const double InfrastructureSlowMs = 1000d;

        public static string LastPhase { get; private set; } = "none";
        public static int LastGameTick { get; private set; } = -1;
        public static string LastDetail { get; private set; } = string.Empty;
        public static double LastElapsedMs { get; private set; } = -1d;

        public static bool Enabled =>
            MAPMechanitorMod.Settings?.enableJusticeBossDiagnosticLogging == true;

        internal static JusticeBossSummaryScopeState BeginSummaryScope(string phase)
        {
            if (!Enabled || !JusticeBossDiagnosticSummaryPatchManager.Installed)
            {
                return default;
            }

            return new JusticeBossSummaryScopeState
            {
                active = true,
                startedTimestamp = Stopwatch.GetTimestamp(),
                phase = phase,
            };
        }

        internal static void EndSummaryScope(
            JusticeBossSummaryScopeState state,
            string detail,
            double slowThresholdMs)
        {
            if (!state.active || state.phase == null)
            {
                return;
            }

            double elapsedMs = ElapsedMilliseconds(state.startedTimestamp);
            bool slow = slowThresholdMs > 0d && elapsedMs >= slowThresholdMs;
            Write(state.phase + ".End", detail + " slow=" + slow, elapsedMs, slow);
        }

        internal static Exception? FinalizeSummaryScope(
            Exception? exception,
            JusticeBossSummaryScopeState state)
        {
            if (state.active && exception != null)
            {
                Write(
                    "Diagnostic.Exception",
                    "phase=" + Sanitize(state.phase)
                        + " exception=" + Sanitize(exception.GetType().FullName)
                        + " message=" + Sanitize(exception.Message),
                    ElapsedMilliseconds(state.startedTimestamp),
                    warning: true);
            }

            return exception;
        }

        internal static void WriteLandingSummary(
            int mapId,
            int pendingBefore,
            int pendingAfter,
            int landedCount,
            int eventCount,
            string kindCounts,
            double elapsedMs,
            Exception? exception)
        {
            if (landedCount < JusticeBossLaunchTracePatchManager.LandingSummaryPawnThreshold
                && !(pendingBefore > 0 && pendingAfter <= 0)
                && exception == null)
            {
                return;
            }

            Write(
                "Wave.LandingSummary",
                "map=" + mapId
                    + " pendingBefore=" + pendingBefore
                    + " pendingAfter=" + pendingAfter
                    + " landed=" + landedCount
                    + " events=" + eventCount
                    + " kindCounts=" + Sanitize(kindCounts)
                    + (exception == null
                        ? string.Empty
                        : " exception=" + Sanitize(exception.GetType().FullName)
                            + " message=" + Sanitize(exception.Message)),
                elapsedMs,
                warning: exception != null);
        }

        internal static void Write(
            string phase,
            string detail,
            double elapsedMs = -1d,
            bool warning = false)
        {
            if (!Enabled || !JusticeBossDiagnosticSummaryPatchManager.Installed)
            {
                return;
            }

            int tick = Current.Game?.tickManager?.TicksGame ?? -1;
            StringBuilder builder = new StringBuilder(256);
            builder.Append(LogPrefix);
            builder.Append(" phase=").Append(Sanitize(phase));
            builder.Append(" tick=").Append(tick);
            if (elapsedMs >= 0d)
            {
                builder.Append(" elapsedMs=").Append(elapsedMs.ToString("0.###"));
            }

            if (!string.IsNullOrWhiteSpace(detail))
            {
                builder.Append(' ').Append(detail);
            }

            LastPhase = phase;
            LastGameTick = tick;
            LastDetail = detail ?? string.Empty;
            LastElapsedMs = elapsedMs;

            string message = builder.ToString();
            if (warning)
            {
                Log.Warning(message);
            }
            else
            {
                Log.Message(message);
            }
        }

        internal static string Sanitize(string? value) =>
            JusticeBossTraceFormatting.Sanitize(value);

        public static string GetLastStatusText()
        {
            return "diagnostics=" + (Enabled ? "on" : "off")
                + " summaryPatches="
                + (JusticeBossDiagnosticSummaryPatchManager.Installed ? "on" : "off")
                + " launchTrace="
                + (JusticeBossLaunchTracePatchManager.Active ? "on" : "off")
                + " traceFile="
                + (JusticeBossTraceFileWriter.IsOpen ? "open" : "closed")
                + " traceLimit="
                + (JusticeBossTraceFileWriter.NormalLimitReached ? "reached" : "ok")
                + " lastPhase=" + LastPhase
                + " lastTick=" + LastGameTick
                + " lastElapsedMs=" + LastElapsedMs.ToString("0.###")
                + " lastDetail=" + Sanitize(LastDetail);
        }

        internal static void ResetRuntimeState()
        {
            LastPhase = "none";
            LastGameTick = -1;
            LastDetail = string.Empty;
            LastElapsedMs = -1d;
        }

        private static double ElapsedMilliseconds(long startedTimestamp)
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

    internal static class JusticeBossDiagnosticSummaryPatchManager
    {
        private const string HarmonyId =
            "MAP_MechanoidMechanitor.JusticeBossDiagnosticSummary";

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

            try
            {
                PatchRequired(
                    "Controller.TryDeployInfrastructure",
                    AccessTools.Method(
                        typeof(CompJusticeBossController),
                        "TryDeployInfrastructure",
                        Type.EmptyTypes),
                    nameof(JusticeBossDiagnosticSummaryHooks.InfrastructurePrefix),
                    nameof(JusticeBossDiagnosticSummaryHooks.InfrastructurePostfix));

                PatchRequired(
                    "Controller.TrySpawnWave",
                    AccessTools.Method(
                        typeof(CompJusticeBossController),
                        "TrySpawnWave",
                        new[] { typeof(bool) }),
                    nameof(JusticeBossDiagnosticSummaryHooks.WavePrefix),
                    nameof(JusticeBossDiagnosticSummaryHooks.WavePostfix));

                PatchRequired(
                    "Spawn.BuildWaveComposition",
                    AccessTools.Method(
                        typeof(JusticeBossSpawnUtility),
                        "BuildWaveComposition",
                        new[] { typeof(int), typeof(int), typeof(bool) }),
                    nameof(JusticeBossDiagnosticSummaryHooks.CompositionPrefix),
                    nameof(JusticeBossDiagnosticSummaryHooks.CompositionPostfix));

                PatchRequired(
                    "Spawn.LaunchWaveDropPodsNear",
                    AccessTools.Method(
                        typeof(JusticeBossSpawnUtility),
                        "LaunchWaveDropPodsNear",
                        new[]
                        {
                            typeof(Pawn),
                            typeof(IntVec3),
                            typeof(int),
                            typeof(List<PawnKindDef>),
                            typeof(Lord).MakeByRefType(),
                        }),
                    nameof(JusticeBossDiagnosticSummaryHooks.LaunchWavePrefix),
                    nameof(JusticeBossDiagnosticSummaryHooks.LaunchWaveResultPostfix));

                PatchRequired(
                    "Spawn.LaunchGuardDropPodsNear",
                    AccessTools.Method(
                        typeof(JusticeBossSpawnUtility),
                        "LaunchGuardDropPodsNear",
                        new[]
                        {
                            typeof(Map),
                            typeof(Faction),
                            typeof(IntVec3),
                            typeof(int),
                            typeof(List<PawnKindDef>),
                            typeof(Lord).MakeByRefType(),
                        }),
                    nameof(JusticeBossDiagnosticSummaryHooks.LaunchGuardPrefix),
                    nameof(JusticeBossDiagnosticSummaryHooks.LaunchGuardResultPostfix));

                installed = true;
                WriteInstall("success");
            }
            catch (Exception exception)
            {
                Harmony.UnpatchAll(HarmonyId);
                installed = false;
                JusticeBossDiagnosticUtility.ResetRuntimeState();
                Log.Error(
                    "[MAP-机械族机械师] 正义 BOSS 诊断： 汇总诊断补丁安装失败："
                        + exception);
            }
        }

        private static void WriteInstall(string result)
        {
            JusticeBossDiagnosticUtility.Write(
                "Patch.Install",
                "module=summary result=" + result);
        }

        private static void Uninstall()
        {
            if (installed)
            {
                JusticeBossDiagnosticUtility.Write(
                    "Patch.Uninstall",
                    "module=summary result=pending");
                Harmony.UnpatchAll(HarmonyId);
                installed = false;
            }

            JusticeBossDiagnosticUtility.ResetRuntimeState();
        }

        private static void PatchRequired(
            string description,
            MethodBase? original,
            string? prefixName,
            string? postfixName)
        {
            if (original == null)
            {
                throw new MissingMethodException(description);
            }

            MethodInfo? prefix = ResolveHook(prefixName);
            MethodInfo? postfix = ResolveHook(postfixName);
            MethodInfo finalizer = ResolveHook(
                nameof(JusticeBossDiagnosticSummaryHooks.ScopeFinalizer))!;

            Harmony.Patch(
                original,
                prefix: prefix == null ? null : new HarmonyMethod(prefix),
                postfix: postfix == null ? null : new HarmonyMethod(postfix),
                transpiler: null,
                finalizer: new HarmonyMethod(finalizer));
        }

        private static MethodInfo? ResolveHook(string? name)
        {
            if (name == null)
            {
                return null;
            }

            MethodInfo? method = AccessTools.Method(
                typeof(JusticeBossDiagnosticSummaryHooks),
                name);
            if (method == null)
            {
                throw new MissingMethodException(
                    typeof(JusticeBossDiagnosticSummaryHooks).FullName,
                    name);
            }

            return method;
        }
    }

    internal static class JusticeBossDiagnosticSummaryHooks
    {
        public static void InfrastructurePrefix(out JusticeBossSummaryScopeState __state)
        {
            __state = JusticeBossDiagnosticUtility.BeginSummaryScope(
                "Infrastructure.Deploy");
        }

        public static void InfrastructurePostfix(
            CompJusticeBossController __instance,
            JusticeBossSummaryScopeState __state)
        {
            JusticeBossDiagnosticUtility.EndSummaryScope(
                __state,
                "event=" + (__instance?.JusticeEventId ?? -1)
                    + " deployed=" + (__instance?.DeployedInfrastructureCount ?? -1)
                    + " pendingGuards=" + (__instance?.PendingGuardDropCount ?? -1)
                    + " completed=" + (__instance?.InfrastructureDeployed ?? false),
                JusticeBossDiagnosticUtility.InfrastructureSlowMs);
        }

        public static void WavePrefix(out JusticeBossSummaryScopeState __state)
        {
            __state = JusticeBossDiagnosticUtility.BeginSummaryScope("Wave.Attempt");
        }

        public static void WavePostfix(
            CompJusticeBossController __instance,
            JusticeBossSummaryScopeState __state)
        {
            JusticeBossDiagnosticUtility.EndSummaryScope(
                __state,
                "event=" + (__instance?.JusticeEventId ?? -1)
                    + " waveCount=" + (__instance?.WaveCount ?? -1)
                    + " pendingKinds=" + (__instance?.PendingWaveDropCount ?? -1)
                    + " bossReplacements=" + (__instance?.BossReplacementCount ?? -1),
                JusticeBossDiagnosticUtility.WaveSlowMs);
        }

        public static void CompositionPrefix(out JusticeBossSummaryScopeState __state)
        {
            __state = JusticeBossDiagnosticUtility.BeginSummaryScope("Wave.Composition");
        }

        public static void CompositionPostfix(
            int waveIndex,
            List<PawnKindDef>? __result,
            JusticeBossSummaryScopeState __state)
        {
            JusticeBossDiagnosticUtility.EndSummaryScope(
                __state,
                "requestedWave=" + waveIndex
                    + " count=" + (__result?.Count ?? 0)
                    + " kindCounts=" + DescribeKindCounts(__result),
                JusticeBossDiagnosticUtility.WaveSlowMs);
        }

        public static void LaunchWavePrefix(out JusticeBossSummaryScopeState __state)
        {
            __state = JusticeBossDiagnosticUtility.BeginSummaryScope("Wave.Launch");
        }

        public static void LaunchGuardPrefix(out JusticeBossSummaryScopeState __state)
        {
            __state = JusticeBossDiagnosticUtility.BeginSummaryScope("Guard.Launch");
        }

        public static void LaunchWaveResultPostfix(
            int justiceEventId,
            JusticeBossDropLaunchResult? __result,
            JusticeBossSummaryScopeState __state)
        {
            JusticeBossDiagnosticUtility.EndSummaryScope(
                __state,
                "event=" + justiceEventId
                    + " requested=" + (__result?.RequestedPawnCount ?? -1)
                    + " launched=" + (__result?.LaunchedPawnCount ?? -1)
                    + " failed=" + (__result?.FailedKinds.Count ?? -1)
                    + " bossReplacements=" + (__result?.BossReplacementCount ?? -1)
                    + " fatal=" + (__result?.FatalFailure ?? false),
                JusticeBossDiagnosticUtility.WaveSlowMs);
        }

        public static void LaunchGuardResultPostfix(
            int justiceEventId,
            JusticeBossDropLaunchResult? __result,
            JusticeBossSummaryScopeState __state)
        {
            JusticeBossDiagnosticUtility.EndSummaryScope(
                __state,
                "event=" + justiceEventId
                    + " requested=" + (__result?.RequestedPawnCount ?? -1)
                    + " launched=" + (__result?.LaunchedPawnCount ?? -1)
                    + " failed=" + (__result?.FailedKinds.Count ?? -1)
                    + " bossReplacements=" + (__result?.BossReplacementCount ?? -1)
                    + " fatal=" + (__result?.FatalFailure ?? false),
                JusticeBossDiagnosticUtility.WaveSlowMs);
        }

        public static Exception? ScopeFinalizer(
            Exception? __exception,
            JusticeBossSummaryScopeState __state)
        {
            return JusticeBossDiagnosticUtility.FinalizeSummaryScope(
                __exception,
                __state);
        }

        private static string DescribeKindCounts(List<PawnKindDef>? kinds)
        {
            if (kinds == null || kinds.Count == 0)
            {
                return "none";
            }

            Dictionary<string, int> counts = new Dictionary<string, int>();
            for (int i = 0; i < kinds.Count; i++)
            {
                string name = kinds[i]?.defName ?? "null";
                counts.TryGetValue(name, out int current);
                counts[name] = current + 1;
            }

            return JusticeBossTraceFormatting.DescribeKindCounts(counts);
        }
    }
}
