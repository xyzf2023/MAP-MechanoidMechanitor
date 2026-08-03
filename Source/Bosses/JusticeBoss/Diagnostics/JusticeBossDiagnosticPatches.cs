using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor
{
    internal sealed class JusticeBossDiagnosticContext
    {
        public int eventId;
        public int waveIndex;
        public int retryCount;
        public string role = "Unknown";
        public int pawnSequence;
        public int dropCellSequence;
        public int podSequence;
        public int placementSequence;
        public int activePawnIndex = -1;
        public string? activePawnKind;
        public int activePodIndex = -1;
        public int landingPawnId = -1;
    }

    internal struct JusticeBossDiagnosticScopeState
    {
        public bool active;
        public long startedTimestamp;
        public string? phase;
        public JusticeBossDiagnosticContext? previousContext;
        public bool restoreContext;
        public bool restoreActivePawn;
        public int previousActivePawnIndex;
        public string? previousActivePawnKind;
        public bool restoreActivePod;
        public int previousActivePodIndex;
    }

    public static class JusticeBossDiagnosticUtility
    {
        private const string Prefix = "[MAP JusticeBoss Diagnostic]";

        public const double PawnGenerationSlowMs = 250d;
        public const double DropCellSlowMs = 250d;
        public const double DropPodSlowMs = 250d;
        public const double LandingSlowMs = 250d;
        public const double WaveSlowMs = 1000d;
        public const double InfrastructureSlowMs = 1000d;

        [ThreadStatic]
        private static JusticeBossDiagnosticContext? activeContext;

        private static readonly HashSet<int> transitLoggedPawnIds = new HashSet<int>();
        private static readonly List<string> pendingPatchFailures = new List<string>();
        private static bool patchFailuresReported;

        public static string LastPhase { get; private set; } = "none";
        public static int LastGameTick { get; private set; } = -1;
        public static string LastDetail { get; private set; } = string.Empty;
        public static double LastElapsedMs { get; private set; } = -1d;

        public static bool Enabled =>
            MAPMechanitorMod.Settings?.enableJusticeBossDiagnosticLogging == true;

        internal static JusticeBossDiagnosticContext? ActiveContext
        {
            get => activeContext;
            set => activeContext = value;
        }

        internal static long Timestamp()
        {
            return Stopwatch.GetTimestamp();
        }

        internal static double ElapsedMilliseconds(long startedTimestamp)
        {
            if (startedTimestamp <= 0L)
            {
                return -1d;
            }

            return (Stopwatch.GetTimestamp() - startedTimestamp)
                * 1000d
                / Stopwatch.Frequency;
        }

        internal static JusticeBossDiagnosticScopeState BeginScope(
            string phase,
            string detail,
            JusticeBossDiagnosticContext? replacementContext = null,
            bool restoreContext = false)
        {
            JusticeBossDiagnosticScopeState state = new JusticeBossDiagnosticScopeState
            {
                active = true,
                startedTimestamp = Timestamp(),
                phase = phase,
            };

            if (restoreContext)
            {
                state.previousContext = ActiveContext;
                state.restoreContext = true;
                ActiveContext = replacementContext;
            }

            Write(phase + ".Begin", detail);
            return state;
        }

        internal static void EndScope(
            JusticeBossDiagnosticScopeState state,
            string detail,
            double slowThresholdMs)
        {
            if (!state.active || state.phase == null)
            {
                return;
            }

            double elapsedMs = ElapsedMilliseconds(state.startedTimestamp);
            Write(state.phase + ".End", detail, elapsedMs);
            if (slowThresholdMs > 0d && elapsedMs >= slowThresholdMs)
            {
                Write(
                    state.phase + ".Slow",
                    detail + " thresholdMs=" + slowThresholdMs.ToString("0.###"),
                    elapsedMs,
                    warning: true);
            }
        }

        internal static Exception? FinalizeScope(
            Exception? exception,
            JusticeBossDiagnosticScopeState state)
        {
            if (state.active && exception != null && state.phase != null)
            {
                Write(
                    state.phase + ".Exception",
                    "exception=" + exception.GetType().FullName
                        + " message=" + Sanitize(exception.Message),
                    ElapsedMilliseconds(state.startedTimestamp),
                    warning: true);
            }

            if (state.restoreActivePawn && ActiveContext != null)
            {
                ActiveContext.activePawnIndex = state.previousActivePawnIndex;
                ActiveContext.activePawnKind = state.previousActivePawnKind;
            }

            if (state.restoreActivePod && ActiveContext != null)
            {
                ActiveContext.activePodIndex = state.previousActivePodIndex;
            }

            if (state.restoreContext)
            {
                ActiveContext = state.previousContext;
            }

            return exception;
        }

        internal static void RecordPatchFailure(string description, Exception? exception = null)
        {
            string text = description;
            if (exception != null)
            {
                text += " exception=" + exception.GetType().FullName
                    + " message=" + Sanitize(exception.Message);
            }

            pendingPatchFailures.Add(text);
        }

        private static void ReportPatchFailuresIfNeeded()
        {
            if (patchFailuresReported || pendingPatchFailures.Count == 0)
            {
                return;
            }

            patchFailuresReported = true;
            for (int i = 0; i < pendingPatchFailures.Count; i++)
            {
                Log.Warning(
                    Prefix
                    + " phase=Patch.InstallFailed "
                    + pendingPatchFailures[i]);
            }
        }

        internal static void Write(
            string phase,
            string detail,
            double elapsedMs = -1d,
            bool warning = false)
        {
            if (!Enabled)
            {
                return;
            }

            ReportPatchFailuresIfNeeded();

            StringBuilder builder = new StringBuilder(256);
            builder.Append(Prefix);
            builder.Append(" phase=").Append(phase);

            int tick = Find.TickManager?.TicksGame ?? -1;
            builder.Append(" tick=").Append(tick);

            JusticeBossDiagnosticContext? context = ActiveContext;
            if (context != null)
            {
                builder.Append(" event=").Append(context.eventId);
                builder.Append(" wave=").Append(context.waveIndex);
                builder.Append(" retry=").Append(context.retryCount);
                builder.Append(" role=").Append(context.role);

                if (context.activePawnIndex >= 0)
                {
                    builder.Append(" pawnIndex=").Append(context.activePawnIndex);
                }

                if (!string.IsNullOrEmpty(context.activePawnKind))
                {
                    builder.Append(" pawnKind=").Append(context.activePawnKind);
                }

                if (context.activePodIndex >= 0)
                {
                    builder.Append(" podIndex=").Append(context.activePodIndex);
                }

                if (context.landingPawnId >= 0)
                {
                    builder.Append(" landingPawnId=").Append(context.landingPawnId);
                }
            }

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

        internal static void MarkTransit(Pawn pawn)
        {
            if (!Enabled || pawn == null)
            {
                return;
            }

            if (transitLoggedPawnIds.Add(pawn.thingIDNumber))
            {
                Write(
                    "Tracker.InTransit",
                    "pawnId=" + pawn.thingIDNumber
                        + " pawn=" + Sanitize(pawn.LabelShort)
                        + " holder=" + Sanitize(pawn.ParentHolder?.GetType().FullName));
            }
        }

        internal static void ForgetPawn(Pawn? pawn)
        {
            if (!Enabled || pawn == null)
            {
                return;
            }

            transitLoggedPawnIds.Remove(pawn.thingIDNumber);
        }

        internal static string Sanitize(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "null";
            }

            return value.Replace('\n', ' ').Replace('\r', ' ');
        }

        public static string GetLastStatusText()
        {
            return "diagnostics=" + (Enabled ? "on" : "off")
                + " lastPhase=" + LastPhase
                + " lastTick=" + LastGameTick
                + " lastElapsedMs=" + LastElapsedMs.ToString("0.###")
                + " lastDetail=" + Sanitize(LastDetail);
        }
    }

    [StaticConstructorOnStartup]
    internal static class JusticeBossDiagnosticBootstrap
    {
        private const BindingFlags AllMethods =
            BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Static
            | BindingFlags.Instance;

        static JusticeBossDiagnosticBootstrap()
        {
            Harmony harmony = new Harmony(
                "MAP_MechanoidMechanitor.JusticeBossDiagnostics");

            Patch(harmony, "Controller.TrySpawnWave",
                FindMethod(typeof(CompJusticeBossController), "TrySpawnWave", 1),
                nameof(JusticeBossDiagnosticHooks.WavePrefix),
                nameof(JusticeBossDiagnosticHooks.WavePostfix));

            Patch(harmony, "Spawn.BuildWaveComposition",
                FindMethod(typeof(JusticeBossSpawnUtility), "BuildWaveComposition", 3),
                nameof(JusticeBossDiagnosticHooks.CompositionPrefix),
                nameof(JusticeBossDiagnosticHooks.CompositionPostfix));

            Patch(harmony, "Spawn.LaunchWaveDropPodsNear",
                FindMethod(typeof(JusticeBossSpawnUtility), "LaunchWaveDropPodsNear", 5),
                nameof(JusticeBossDiagnosticHooks.LaunchWavePrefix),
                nameof(JusticeBossDiagnosticHooks.LaunchResultPostfix));

            Patch(harmony, "Spawn.LaunchGuardDropPodsNear.List",
                FindMethod(
                    typeof(JusticeBossSpawnUtility),
                    "LaunchGuardDropPodsNear",
                    6,
                    parameters => parameters[4].ParameterType == typeof(List<PawnKindDef>)),
                nameof(JusticeBossDiagnosticHooks.LaunchGuardPrefix),
                nameof(JusticeBossDiagnosticHooks.LaunchResultPostfix));

            Patch(harmony, "Spawn.TryGeneratePawn",
                FindMethod(typeof(JusticeBossSpawnUtility), "TryGeneratePawn", 2),
                nameof(JusticeBossDiagnosticHooks.GeneratePawnPrefix),
                nameof(JusticeBossDiagnosticHooks.GeneratePawnPostfix));

            Patch(harmony, "PawnGenerator.GeneratePawn",
                FindMethod(
                    typeof(PawnGenerator),
                    nameof(PawnGenerator.GeneratePawn),
                    1,
                    parameters => parameters[0].ParameterType == typeof(PawnGenerationRequest)),
                nameof(JusticeBossDiagnosticHooks.PawnGeneratorPrefix),
                nameof(JusticeBossDiagnosticHooks.PawnGeneratorPostfix));

            Patch(harmony, "Pawn.SetFaction",
                FindMethod(
                    typeof(Pawn),
                    nameof(Pawn.SetFaction),
                    null,
                    parameters => parameters.Length > 0
                        && parameters[0].ParameterType == typeof(Faction)),
                nameof(JusticeBossDiagnosticHooks.SetFactionPrefix),
                nameof(JusticeBossDiagnosticHooks.SetFactionPostfix));

            Patch(harmony, "WorkMode.EnsureMobileCombatHediff",
                FindMethod(
                    typeof(MechanoidMechanitorWorkModeUtility),
                    nameof(MechanoidMechanitorWorkModeUtility.EnsureMobileCombatHediff),
                    1),
                nameof(JusticeBossDiagnosticHooks.HediffPrefix),
                nameof(JusticeBossDiagnosticHooks.HediffPostfix));

            Patch(harmony, "Spawn.LaunchPawnDropPods",
                FindMethod(typeof(JusticeBossSpawnUtility), "LaunchPawnDropPods", 9),
                nameof(JusticeBossDiagnosticHooks.LaunchPawnPodsPrefix),
                nameof(JusticeBossDiagnosticHooks.LaunchResultPostfix));

            Patch(harmony, "Spawn.TryFindDropCell",
                FindMethod(typeof(JusticeBossSpawnUtility), "TryFindDropCell", 5),
                nameof(JusticeBossDiagnosticHooks.DropCellPrefix),
                nameof(JusticeBossDiagnosticHooks.DropCellPostfix));

            Patch(harmony, "Spawn.TryMakeDropPod",
                FindMethod(typeof(JusticeBossSpawnUtility), "TryMakeDropPod", 4),
                nameof(JusticeBossDiagnosticHooks.DropPodPrefix),
                nameof(JusticeBossDiagnosticHooks.DropPodPostfix));

            Patch(harmony, "DropPodUtility.MakeDropPodAt",
                FindMethod(
                    typeof(DropPodUtility),
                    nameof(DropPodUtility.MakeDropPodAt),
                    4,
                    parameters => parameters[0].ParameterType == typeof(IntVec3)
                        && parameters[1].ParameterType == typeof(Map)
                        && parameters[2].ParameterType == typeof(ActiveTransporterInfo)),
                nameof(JusticeBossDiagnosticHooks.MakeDropPodAtPrefix),
                nameof(JusticeBossDiagnosticHooks.MakeDropPodAtPostfix));

            Patch(harmony, "Deployment.DeployInfrastructure",
                FindMethod(typeof(JusticeBossDeploymentUtility), "DeployInfrastructure", 11),
                nameof(JusticeBossDiagnosticHooks.InfrastructurePrefix),
                nameof(JusticeBossDiagnosticHooks.InfrastructurePostfix));

            Patch(harmony, "Deployment.TryFindPlacement",
                FindMethod(typeof(JusticeBossDeploymentUtility), "TryFindPlacement", 9),
                nameof(JusticeBossDiagnosticHooks.PlacementPrefix),
                nameof(JusticeBossDiagnosticHooks.PlacementPostfix));

            Patch(harmony, "Deployment.SpawnBuildingViaDropPod",
                FindMethod(typeof(JusticeBossDeploymentUtility), "SpawnBuildingViaDropPod", 5),
                nameof(JusticeBossDiagnosticHooks.InfrastructurePodPrefix),
                nameof(JusticeBossDiagnosticHooks.InfrastructurePodPostfix));

            Patch(harmony, "Tracker.Register",
                FindMethod(typeof(MapComponent_JusticeBossDropTracker), "Register", 1),
                nameof(JusticeBossDiagnosticHooks.RegisterPrefix),
                nameof(JusticeBossDiagnosticHooks.RegisterPostfix));

            Patch(harmony, "Tracker.Unregister",
                FindMethod(typeof(MapComponent_JusticeBossDropTracker), "Unregister", 1),
                nameof(JusticeBossDiagnosticHooks.UnregisterPrefix),
                null,
                finalizerName: null);

            Patch(harmony, "Tracker.IsInTransit",
                FindMethod(typeof(MapComponent_JusticeBossDropTracker), "IsInTransit", 1),
                null,
                nameof(JusticeBossDiagnosticHooks.TransitPostfix),
                finalizerName: null);

            Patch(harmony, "Tracker.CompleteLanding",
                FindMethod(typeof(MapComponent_JusticeBossDropTracker), "CompleteLanding", 1),
                nameof(JusticeBossDiagnosticHooks.LandingPrefix),
                nameof(JusticeBossDiagnosticHooks.LandingPostfix));

            Patch(harmony, "Lord.EnsureAssaultLord",
                FindMethod(typeof(JusticeBossLordUtility), "EnsureAssaultLord", 3),
                nameof(JusticeBossDiagnosticHooks.LordPrefix),
                nameof(JusticeBossDiagnosticHooks.LordPostfix));

            Patch(harmony, "Lord.EnsureGuardLord",
                FindMethod(typeof(JusticeBossLordUtility), "EnsureGuardLord", 4),
                nameof(JusticeBossDiagnosticHooks.LordPrefix),
                nameof(JusticeBossDiagnosticHooks.LordPostfix));

            Patch(harmony, "Lord.AddPawn",
                FindMethod(
                    typeof(Lord),
                    nameof(Lord.AddPawn),
                    1,
                    parameters => parameters[0].ParameterType == typeof(Pawn)),
                nameof(JusticeBossDiagnosticHooks.AddPawnPrefix),
                nameof(JusticeBossDiagnosticHooks.AddPawnPostfix));

            Patch(harmony, "PawnJobTracker.EndCurrentJob",
                FindMethod(
                    typeof(Pawn_JobTracker),
                    "EndCurrentJob",
                    null,
                    parameters => parameters.Length > 0
                        && parameters[0].ParameterType == typeof(JobCondition)),
                nameof(JusticeBossDiagnosticHooks.EndJobPrefix),
                nameof(JusticeBossDiagnosticHooks.EndJobPostfix));

            Patch(harmony, "LordToil.UpdateAllDuties",
                FindMethod(typeof(LordToil), "UpdateAllDuties", 0),
                nameof(JusticeBossDiagnosticHooks.UpdateDutiesPrefix),
                nameof(JusticeBossDiagnosticHooks.UpdateDutiesPostfix));
        }

        private static MethodInfo? FindMethod(
            Type type,
            string name,
            int? parameterCount,
            Func<ParameterInfo[], bool>? parameterFilter = null)
        {
            return type
                .GetMethods(AllMethods)
                .FirstOrDefault(method =>
                {
                    if (method.Name != name)
                    {
                        return false;
                    }

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameterCount.HasValue
                        && parameters.Length != parameterCount.Value)
                    {
                        return false;
                    }

                    return parameterFilter == null || parameterFilter(parameters);
                });
        }

        private static void Patch(
            Harmony harmony,
            string description,
            MethodBase? original,
            string? prefixName,
            string? postfixName,
            string? finalizerName = nameof(JusticeBossDiagnosticHooks.ScopeFinalizer))
        {
            if (original == null)
            {
                JusticeBossDiagnosticUtility.RecordPatchFailure(
                    description + " target method not found");
                return;
            }

            try
            {
                MethodInfo? prefixMethod = prefixName == null
                    ? null
                    : AccessTools.Method(typeof(JusticeBossDiagnosticHooks), prefixName);
                MethodInfo? postfixMethod = postfixName == null
                    ? null
                    : AccessTools.Method(typeof(JusticeBossDiagnosticHooks), postfixName);
                MethodInfo? finalizerMethod = finalizerName == null
                    ? null
                    : AccessTools.Method(typeof(JusticeBossDiagnosticHooks), finalizerName);

                harmony.Patch(
                    original,
                    prefix: prefixMethod == null ? null : new HarmonyMethod(prefixMethod),
                    postfix: postfixMethod == null ? null : new HarmonyMethod(postfixMethod),
                    transpiler: null,
                    finalizer: finalizerMethod == null ? null : new HarmonyMethod(finalizerMethod));
            }
            catch (Exception exception)
            {
                JusticeBossDiagnosticUtility.RecordPatchFailure(
                    description,
                    exception);
            }
        }
    }

    internal static class JusticeBossDiagnosticHooks
    {
        private static bool HasActiveFlow =>
            JusticeBossDiagnosticUtility.Enabled
            && JusticeBossDiagnosticUtility.ActiveContext != null;

        public static void WavePrefix(
            CompJusticeBossController __instance,
            bool forceBossReplace,
            out JusticeBossDiagnosticScopeState __state)
        {
            if (!JusticeBossDiagnosticUtility.Enabled)
            {
                __state = default;
                return;
            }

            int retry = Traverse.Create(__instance)
                .Field("waveDropRetryCount")
                .GetValue<int>();
            JusticeBossDiagnosticContext context = new JusticeBossDiagnosticContext
            {
                eventId = __instance.JusticeEventId,
                waveIndex = __instance.WaveCount + 1,
                retryCount = retry,
                role = JusticeBossDropRole.Assault.ToString(),
            };

            __state = JusticeBossDiagnosticUtility.BeginScope(
                "Wave.Attempt",
                "forceBossReplace=" + forceBossReplace
                    + " pendingKinds=" + __instance.PendingWaveDropCount
                    + " anchor=" + __instance.AnchorCell,
                context,
                restoreContext: true);
        }

        public static void WavePostfix(
            CompJusticeBossController __instance,
            JusticeBossDiagnosticScopeState __state)
        {
            if (!__state.active)
            {
                return;
            }

            JusticeBossDiagnosticUtility.EndScope(
                __state,
                "waveCount=" + __instance.WaveCount
                    + " pendingKinds=" + __instance.PendingWaveDropCount
                    + " bossReplacements=" + __instance.BossReplacementCount,
                JusticeBossDiagnosticUtility.WaveSlowMs);
        }

        public static void CompositionPrefix(
            int waveIndex,
            int mechsPerWave,
            bool applyBossReplace,
            out JusticeBossDiagnosticScopeState __state)
        {
            if (!JusticeBossDiagnosticUtility.Enabled)
            {
                __state = default;
                return;
            }

            __state = JusticeBossDiagnosticUtility.BeginScope(
                "Wave.Composition",
                "requestedWave=" + waveIndex
                    + " mechsPerWave=" + mechsPerWave
                    + " applyBossReplace=" + applyBossReplace);
        }

        public static void CompositionPostfix(
            List<PawnKindDef>? __result,
            JusticeBossDiagnosticScopeState __state)
        {
            if (!__state.active)
            {
                return;
            }

            string kinds = __result == null
                ? "null"
                : string.Join(",", __result.Select(kind => kind?.defName ?? "null"));
            JusticeBossDiagnosticUtility.EndScope(
                __state,
                "count=" + (__result?.Count ?? 0) + " kinds=" + kinds,
                JusticeBossDiagnosticUtility.WaveSlowMs);
        }

        public static void LaunchWavePrefix(
            Pawn justice,
            IntVec3 fallbackAnchor,
            int justiceEventId,
            List<PawnKindDef>? kinds,
            out JusticeBossDiagnosticScopeState __state)
        {
            if (!JusticeBossDiagnosticUtility.Enabled)
            {
                __state = default;
                return;
            }

            JusticeBossDiagnosticContext? context =
                JusticeBossDiagnosticUtility.ActiveContext;
            if (context == null)
            {
                context = new JusticeBossDiagnosticContext
                {
                    eventId = justiceEventId,
                    role = JusticeBossDropRole.Assault.ToString(),
                };
                __state = JusticeBossDiagnosticUtility.BeginScope(
                    "Wave.Launch",
                    "justiceId=" + (justice?.thingIDNumber ?? -1)
                        + " fallbackAnchor=" + fallbackAnchor
                        + " requested=" + (kinds?.Count ?? 0),
                    context,
                    restoreContext: true);
                return;
            }

            context.role = JusticeBossDropRole.Assault.ToString();
            __state = JusticeBossDiagnosticUtility.BeginScope(
                "Wave.Launch",
                "justiceId=" + (justice?.thingIDNumber ?? -1)
                    + " fallbackAnchor=" + fallbackAnchor
                    + " requested=" + (kinds?.Count ?? 0));
        }

        public static void LaunchGuardPrefix(
            Map map,
            Faction faction,
            IntVec3 anchor,
            int justiceEventId,
            List<PawnKindDef>? kinds,
            out JusticeBossDiagnosticScopeState __state)
        {
            if (!JusticeBossDiagnosticUtility.Enabled)
            {
                __state = default;
                return;
            }

            JusticeBossDiagnosticContext context = new JusticeBossDiagnosticContext
            {
                eventId = justiceEventId,
                role = JusticeBossDropRole.Guard.ToString(),
            };
            __state = JusticeBossDiagnosticUtility.BeginScope(
                "Guard.Launch",
                "map=" + (map?.uniqueID ?? -1)
                    + " faction=" + (faction?.def?.defName ?? "null")
                    + " anchor=" + anchor
                    + " requested=" + (kinds?.Count ?? 0),
                context,
                restoreContext: true);
        }

        public static void LaunchResultPostfix(
            JusticeBossDropLaunchResult? __result,
            JusticeBossDiagnosticScopeState __state)
        {
            if (!__state.active)
            {
                return;
            }

            JusticeBossDiagnosticUtility.EndScope(
                __state,
                "requested=" + (__result?.RequestedPawnCount ?? -1)
                    + " launched=" + (__result?.LaunchedPawnCount ?? -1)
                    + " failed=" + (__result?.FailedKinds.Count ?? -1)
                    + " bossReplacements=" + (__result?.BossReplacementCount ?? -1)
                    + " fatal=" + (__result?.FatalFailure ?? false),
                JusticeBossDiagnosticUtility.WaveSlowMs);
        }

        public static void GeneratePawnPrefix(
            PawnKindDef kind,
            Faction? faction,
            out JusticeBossDiagnosticScopeState __state)
        {
            if (!HasActiveFlow)
            {
                __state = default;
                return;
            }

            JusticeBossDiagnosticContext context = JusticeBossDiagnosticUtility.ActiveContext!;
            int previousIndex = context.activePawnIndex;
            string? previousKind = context.activePawnKind;
            context.pawnSequence++;
            context.activePawnIndex = context.pawnSequence;
            context.activePawnKind = kind?.defName;

            __state = JusticeBossDiagnosticUtility.BeginScope(
                "GeneratePawn",
                "targetFaction=" + (faction?.def?.defName ?? "null")
                    + " isBoss=" + (kind?.isBoss ?? false));
            __state.restoreActivePawn = true;
            __state.previousActivePawnIndex = previousIndex;
            __state.previousActivePawnKind = previousKind;
        }

        public static void GeneratePawnPostfix(
            Pawn? __result,
            JusticeBossDiagnosticScopeState __state)
        {
            if (!__state.active)
            {
                return;
            }

            JusticeBossDiagnosticUtility.EndScope(
                __state,
                "resultPawnId=" + (__result?.thingIDNumber ?? -1)
                    + " race=" + (__result?.def?.defName ?? "null")
                    + " faction=" + (__result?.Faction?.def?.defName ?? "null"),
                JusticeBossDiagnosticUtility.PawnGenerationSlowMs);
        }

        public static void PawnGeneratorPrefix(
            out JusticeBossDiagnosticScopeState __state)
        {
            if (!HasActiveFlow
                || JusticeBossDiagnosticUtility.ActiveContext!.activePawnIndex < 0)
            {
                __state = default;
                return;
            }

            __state = JusticeBossDiagnosticUtility.BeginScope(
                "GeneratePawn.Core",
                string.Empty);
        }

        public static void PawnGeneratorPostfix(
            Pawn? __result,
            JusticeBossDiagnosticScopeState __state)
        {
            if (!__state.active)
            {
                return;
            }

            JusticeBossDiagnosticUtility.EndScope(
                __state,
                "resultPawnId=" + (__result?.thingIDNumber ?? -1),
                JusticeBossDiagnosticUtility.PawnGenerationSlowMs);
        }

        public static void SetFactionPrefix(
            Pawn __instance,
            object[] __args,
            out JusticeBossDiagnosticScopeState __state)
        {
            if (!HasActiveFlow)
            {
                __state = default;
                return;
            }

            Faction? target = __args.Length > 0 ? __args[0] as Faction : null;
            __state = JusticeBossDiagnosticUtility.BeginScope(
                "Pawn.SetFaction",
                "pawnId=" + (__instance?.thingIDNumber ?? -1)
                    + " from=" + (__instance?.Faction?.def?.defName ?? "null")
                    + " to=" + (target?.def?.defName ?? "null"));
        }

        public static void SetFactionPostfix(
            Pawn __instance,
            JusticeBossDiagnosticScopeState __state)
        {
            if (!__state.active)
            {
                return;
            }

            JusticeBossDiagnosticUtility.EndScope(
                __state,
                "pawnId=" + (__instance?.thingIDNumber ?? -1)
                    + " actual=" + (__instance?.Faction?.def?.defName ?? "null"),
                JusticeBossDiagnosticUtility.PawnGenerationSlowMs);
        }

        public static void HediffPrefix(
            Pawn? pawn,
            out JusticeBossDiagnosticScopeState __state)
        {
            if (!HasActiveFlow)
            {
                __state = default;
                return;
            }

            __state = JusticeBossDiagnosticUtility.BeginScope(
                "Pawn.MobileCombatHediff",
                "pawnId=" + (pawn?.thingIDNumber ?? -1));
        }

        public static void HediffPostfix(
            Pawn? pawn,
            JusticeBossDiagnosticScopeState __state)
        {
            if (!__state.active)
            {
                return;
            }

            JusticeBossDiagnosticUtility.EndScope(
                __state,
                "pawnId=" + (pawn?.thingIDNumber ?? -1),
                JusticeBossDiagnosticUtility.PawnGenerationSlowMs);
        }

        public static void LaunchPawnPodsPrefix(
            object[] __args,
            out JusticeBossDiagnosticScopeState __state)
        {
            if (!HasActiveFlow)
            {
                __state = default;
                return;
            }

            JusticeBossDropRole role = __args.Length > 5
                && __args[5] is JusticeBossDropRole parsedRole
                    ? parsedRole
                    : JusticeBossDropRole.Assault;
            List<Pawn>? pawns = __args.Length > 6 ? __args[6] as List<Pawn> : null;
            JusticeBossDiagnosticUtility.ActiveContext!.role = role.ToString();
            __state = JusticeBossDiagnosticUtility.BeginScope(
                "DropLaunch",
                "pawns=" + (pawns?.Count ?? 0)
                    + " center=" + (__args.Length > 2 ? __args[2] : null)
                    + " anchor=" + (__args.Length > 3 ? __args[3] : null));
        }

        public static void DropCellPrefix(
            object[] __args,
            out JusticeBossDiagnosticScopeState __state)
        {
            if (!HasActiveFlow)
            {
                __state = default;
                return;
            }

            JusticeBossDiagnosticContext context = JusticeBossDiagnosticUtility.ActiveContext!;
            context.dropCellSequence++;
            List<IntVec3>? reserved = __args.Length > 3 ? __args[3] as List<IntVec3> : null;
            Map? map = __args.Length > 0 ? __args[0] as Map : null;
            __state = JusticeBossDiagnosticUtility.BeginScope(
                "DropCell.Search",
                "searchIndex=" + context.dropCellSequence
                    + " map=" + (map?.uniqueID ?? -1)
                    + " center=" + (__args.Length > 1 ? __args[1] : null)
                    + " reserved=" + (reserved?.Count ?? 0));
        }

        public static void DropCellPostfix(
            bool __result,
            object[] __args,
            JusticeBossDiagnosticScopeState __state)
        {
            if (!__state.active)
            {
                return;
            }

            JusticeBossDiagnosticUtility.EndScope(
                __state,
                "success=" + __result
                    + " cell=" + (__args.Length > 4 ? __args[4] : null),
                JusticeBossDiagnosticUtility.DropCellSlowMs);
        }

        public static void DropPodPrefix(
            object[] __args,
            out JusticeBossDiagnosticScopeState __state)
        {
            if (!HasActiveFlow)
            {
                __state = default;
                return;
            }

            JusticeBossDiagnosticContext context = JusticeBossDiagnosticUtility.ActiveContext!;
            int previousPodIndex = context.activePodIndex;
            context.podSequence++;
            context.activePodIndex = context.podSequence;
            List<Pawn>? group = __args.Length > 3 ? __args[3] as List<Pawn> : null;
            __state = JusticeBossDiagnosticUtility.BeginScope(
                "DropPod.Create",
                "cell=" + (__args.Length > 2 ? __args[2] : null)
                    + " pawnCount=" + (group?.Count ?? 0));
            __state.restoreActivePod = true;
            __state.previousActivePodIndex = previousPodIndex;
        }

        public static void DropPodPostfix(
            bool __result,
            JusticeBossDiagnosticScopeState __state)
        {
            if (!__state.active)
            {
                return;
            }

            JusticeBossDiagnosticUtility.EndScope(
                __state,
                "success=" + __result,
                JusticeBossDiagnosticUtility.DropPodSlowMs);
        }

        public static void MakeDropPodAtPrefix(
            object[] __args,
            out JusticeBossDiagnosticScopeState __state)
        {
            if (!HasActiveFlow)
            {
                __state = default;
                return;
            }

            ActiveTransporterInfo? info = __args.Length > 2
                ? __args[2] as ActiveTransporterInfo
                : null;
            Map? map = __args.Length > 1 ? __args[1] as Map : null;
            __state = JusticeBossDiagnosticUtility.BeginScope(
                "DropPod.MakeDropPodAt",
                "cell=" + (__args.Length > 0 ? __args[0] : null)
                    + " map=" + (map?.uniqueID ?? -1)
                    + " contents=" + (info?.innerContainer?.Count ?? -1));
        }

        public static void MakeDropPodAtPostfix(
            JusticeBossDiagnosticScopeState __state)
        {
            if (!__state.active)
            {
                return;
            }

            JusticeBossDiagnosticUtility.EndScope(
                __state,
                "returned=true",
                JusticeBossDiagnosticUtility.DropPodSlowMs);
        }

        public static void InfrastructurePrefix(
            object[] __args,
            out JusticeBossDiagnosticScopeState __state)
        {
            if (!JusticeBossDiagnosticUtility.Enabled)
            {
                __state = default;
                return;
            }

            Pawn? justice = __args.Length > 0 ? __args[0] as Pawn : null;
            int eventId = __args.Length > 2 && __args[2] is int parsedEventId
                ? parsedEventId
                : justice?.thingIDNumber ?? 0;
            JusticeBossDiagnosticContext context = new JusticeBossDiagnosticContext
            {
                eventId = eventId,
                role = "Infrastructure",
            };
            __state = JusticeBossDiagnosticUtility.BeginScope(
                "Infrastructure.Deploy",
                "justiceId=" + (justice?.thingIDNumber ?? -1)
                    + " anchor=" + (__args.Length > 1 ? __args[1] : null),
                context,
                restoreContext: true);
        }

        public static void InfrastructurePostfix(
            object[] __args,
            JusticeBossDiagnosticScopeState __state)
        {
            if (!__state.active)
            {
                return;
            }

            List<Thing>? deployed = __args.Length > 8 ? __args[8] as List<Thing> : null;
            List<PawnKindDef>? failedGuards = __args.Length > 9
                ? __args[9] as List<PawnKindDef>
                : null;
            JusticeBossDiagnosticUtility.EndScope(
                __state,
                "deployed=" + (deployed?.Count ?? -1)
                    + " failedGuards=" + (failedGuards?.Count ?? -1),
                JusticeBossDiagnosticUtility.InfrastructureSlowMs);
        }

        public static void PlacementPrefix(
            object[] __args,
            out JusticeBossDiagnosticScopeState __state)
        {
            if (!HasActiveFlow)
            {
                __state = default;
                return;
            }

            JusticeBossDiagnosticContext context = JusticeBossDiagnosticUtility.ActiveContext!;
            context.placementSequence++;
            ThingDef? def = __args.Length > 2 ? __args[2] as ThingDef : null;
            __state = JusticeBossDiagnosticUtility.BeginScope(
                "Infrastructure.Placement",
                "placementIndex=" + context.placementSequence
                    + " def=" + (def?.defName ?? "null")
                    + " anchor=" + (__args.Length > 1 ? __args[1] : null));
        }

        public static void PlacementPostfix(
            bool __result,
            object[] __args,
            JusticeBossDiagnosticScopeState __state)
        {
            if (!__state.active)
            {
                return;
            }

            JusticeBossDiagnosticUtility.EndScope(
                __state,
                "success=" + __result
                    + " cell=" + (__args.Length > 7 ? __args[7] : null)
                    + " rot=" + (__args.Length > 8 ? __args[8] : null),
                JusticeBossDiagnosticUtility.DropCellSlowMs);
        }

        public static void InfrastructurePodPrefix(
            object[] __args,
            out JusticeBossDiagnosticScopeState __state)
        {
            if (!HasActiveFlow)
            {
                __state = default;
                return;
            }

            JusticeBossDiagnosticContext context = JusticeBossDiagnosticUtility.ActiveContext!;
            int previousPodIndex = context.activePodIndex;
            context.podSequence++;
            context.activePodIndex = context.podSequence;
            ThingDef? def = __args.Length > 2 ? __args[2] as ThingDef : null;
            __state = JusticeBossDiagnosticUtility.BeginScope(
                "Infrastructure.DropPod",
                "def=" + (def?.defName ?? "null")
                    + " cell=" + (__args.Length > 3 ? __args[3] : null)
                    + " rot=" + (__args.Length > 4 ? __args[4] : null));
            __state.restoreActivePod = true;
            __state.previousActivePodIndex = previousPodIndex;
        }

        public static void InfrastructurePodPostfix(
            Thing? __result,
            JusticeBossDiagnosticScopeState __state)
        {
            if (!__state.active)
            {
                return;
            }

            JusticeBossDiagnosticUtility.EndScope(
                __state,
                "resultId=" + (__result?.thingIDNumber ?? -1)
                    + " resultDef=" + (__result?.def?.defName ?? "null"),
                JusticeBossDiagnosticUtility.DropPodSlowMs);
        }

        public static void RegisterPrefix(
            PendingJusticeBossDrop? drop,
            out JusticeBossDiagnosticScopeState __state)
        {
            if (!HasActiveFlow)
            {
                __state = default;
                return;
            }

            __state = JusticeBossDiagnosticUtility.BeginScope(
                "Tracker.Register",
                "pawnId=" + (drop?.pawn?.thingIDNumber ?? -1)
                    + " dropRole=" + (drop?.role.ToString() ?? "null"));
        }

        public static void RegisterPostfix(
            PendingJusticeBossDrop? drop,
            JusticeBossDiagnosticScopeState __state)
        {
            if (!__state.active)
            {
                return;
            }

            JusticeBossDiagnosticUtility.EndScope(
                __state,
                "pawnId=" + (drop?.pawn?.thingIDNumber ?? -1),
                JusticeBossDiagnosticUtility.DropPodSlowMs);
        }

        public static void UnregisterPrefix(Pawn? pawn)
        {
            if (!JusticeBossDiagnosticUtility.Enabled)
            {
                return;
            }

            JusticeBossDiagnosticUtility.ForgetPawn(pawn);
            JusticeBossDiagnosticUtility.Write(
                "Tracker.Unregister",
                "pawnId=" + (pawn?.thingIDNumber ?? -1));
        }

        public static void TransitPostfix(Pawn? pawn, bool __result)
        {
            if (!JusticeBossDiagnosticUtility.Enabled || !__result || pawn == null)
            {
                return;
            }

            JusticeBossDiagnosticUtility.MarkTransit(pawn);
        }

        public static void LandingPrefix(
            PendingJusticeBossDrop entry,
            out JusticeBossDiagnosticScopeState __state)
        {
            if (!JusticeBossDiagnosticUtility.Enabled)
            {
                __state = default;
                return;
            }

            JusticeBossDiagnosticContext context = new JusticeBossDiagnosticContext
            {
                eventId = entry?.justiceEventId ?? 0,
                role = entry?.role.ToString() ?? "Unknown",
                landingPawnId = entry?.pawn?.thingIDNumber ?? -1,
                activePawnIndex = 0,
                activePawnKind = entry?.pawn?.kindDef?.defName,
            };
            __state = JusticeBossDiagnosticUtility.BeginScope(
                "Tracker.Landing",
                "pawnId=" + (entry?.pawn?.thingIDNumber ?? -1)
                    + " map=" + (entry?.pawn?.Map?.uniqueID ?? -1)
                    + " position=" + (entry?.pawn?.Position.ToString() ?? "null"),
                context,
                restoreContext: true);
        }

        public static void LandingPostfix(
            PendingJusticeBossDrop entry,
            JusticeBossDiagnosticScopeState __state)
        {
            if (!__state.active)
            {
                return;
            }

            JusticeBossDiagnosticUtility.EndScope(
                __state,
                "pawnId=" + (entry?.pawn?.thingIDNumber ?? -1)
                    + " spawned=" + (entry?.pawn?.Spawned ?? false)
                    + " faction=" + (entry?.pawn?.Faction?.def?.defName ?? "null"),
                JusticeBossDiagnosticUtility.LandingSlowMs);
            JusticeBossDiagnosticUtility.ForgetPawn(entry?.pawn);
        }

        public static void LordPrefix(
            MethodBase __originalMethod,
            object[] __args,
            out JusticeBossDiagnosticScopeState __state)
        {
            if (!HasActiveFlow)
            {
                __state = default;
                return;
            }

            Map? map = __args.Length > 0 ? __args[0] as Map : null;
            __state = JusticeBossDiagnosticUtility.BeginScope(
                "Lord." + __originalMethod.Name,
                "map=" + (map?.uniqueID ?? -1)
                    + " lordCount=" + (map?.lordManager?.lords?.Count ?? -1));
        }

        public static void LordPostfix(
            Lord? __result,
            JusticeBossDiagnosticScopeState __state)
        {
            if (!__state.active)
            {
                return;
            }

            JusticeBossDiagnosticUtility.EndScope(
                __state,
                "lord=" + (__result?.GetHashCode() ?? -1)
                    + " ownedPawns=" + (__result?.ownedPawns?.Count ?? -1),
                JusticeBossDiagnosticUtility.LandingSlowMs);
        }

        public static void AddPawnPrefix(
            Lord __instance,
            Pawn pawn,
            out JusticeBossDiagnosticScopeState __state)
        {
            if (!HasActiveFlow)
            {
                __state = default;
                return;
            }

            __state = JusticeBossDiagnosticUtility.BeginScope(
                "Lord.AddPawn",
                "pawnId=" + (pawn?.thingIDNumber ?? -1)
                    + " beforeCount=" + (__instance?.ownedPawns?.Count ?? -1));
        }

        public static void AddPawnPostfix(
            Lord __instance,
            Pawn pawn,
            JusticeBossDiagnosticScopeState __state)
        {
            if (!__state.active)
            {
                return;
            }

            JusticeBossDiagnosticUtility.EndScope(
                __state,
                "pawnId=" + (pawn?.thingIDNumber ?? -1)
                    + " afterCount=" + (__instance?.ownedPawns?.Count ?? -1),
                JusticeBossDiagnosticUtility.LandingSlowMs);
        }

        public static void EndJobPrefix(
            object[] __args,
            out JusticeBossDiagnosticScopeState __state)
        {
            if (!HasActiveFlow
                || JusticeBossDiagnosticUtility.ActiveContext!.landingPawnId < 0)
            {
                __state = default;
                return;
            }

            __state = JusticeBossDiagnosticUtility.BeginScope(
                "Pawn.EndCurrentJob",
                "condition=" + (__args.Length > 0 ? __args[0] : null));
        }

        public static void EndJobPostfix(
            JusticeBossDiagnosticScopeState __state)
        {
            if (!__state.active)
            {
                return;
            }

            JusticeBossDiagnosticUtility.EndScope(
                __state,
                "returned=true",
                JusticeBossDiagnosticUtility.LandingSlowMs);
        }

        public static void UpdateDutiesPrefix(
            out JusticeBossDiagnosticScopeState __state)
        {
            if (!HasActiveFlow
                || JusticeBossDiagnosticUtility.ActiveContext!.landingPawnId < 0)
            {
                __state = default;
                return;
            }

            __state = JusticeBossDiagnosticUtility.BeginScope(
                "LordToil.UpdateAllDuties",
                string.Empty);
        }

        public static void UpdateDutiesPostfix(
            JusticeBossDiagnosticScopeState __state)
        {
            if (!__state.active)
            {
                return;
            }

            JusticeBossDiagnosticUtility.EndScope(
                __state,
                "returned=true",
                JusticeBossDiagnosticUtility.LandingSlowMs);
        }

        public static Exception? ScopeFinalizer(
            Exception? __exception,
            JusticeBossDiagnosticScopeState __state)
        {
            return JusticeBossDiagnosticUtility.FinalizeScope(
                __exception,
                __state);
        }
    }
}
