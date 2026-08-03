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

        internal static JusticeBossDiagnosticScopeState BeginScope(
            string phase,
            string detail,
            JusticeBossDiagnosticContext? replacementContext = null,
            bool restoreContext = false)
        {
            JusticeBossDiagnosticScopeState state = new JusticeBossDiagnosticScopeState
            {
                active = true,
                startedTimestamp = Stopwatch.GetTimestamp(),
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

        internal static void RecordPatchFailure(
            string description,
            Exception? exception = null)
        {
            string detail = description;
            if (exception != null)
            {
                detail += " exception=" + exception.GetType().FullName
                    + " message=" + Sanitize(exception.Message);
            }

            pendingPatchFailures.Add(detail);
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
    }

    [StaticConstructorOnStartup]
    internal static class JusticeBossDiagnosticBootstrap
    {
        static JusticeBossDiagnosticBootstrap()
        {
            Harmony harmony = new Harmony(
                "MAP_MechanoidMechanitor.JusticeBossDiagnostics");

            Patch(
                harmony,
                "Controller.TrySpawnWave",
                AccessTools.Method(
                    typeof(CompJusticeBossController),
                    "TrySpawnWave",
                    new[] { typeof(bool) }),
                nameof(JusticeBossDiagnosticHooks.WavePrefix),
                nameof(JusticeBossDiagnosticHooks.WavePostfix));

            Patch(
                harmony,
                "Spawn.BuildWaveComposition",
                AccessTools.Method(
                    typeof(JusticeBossSpawnUtility),
                    "BuildWaveComposition",
                    new[] { typeof(int), typeof(int), typeof(bool) }),
                nameof(JusticeBossDiagnosticHooks.CompositionPrefix),
                nameof(JusticeBossDiagnosticHooks.CompositionPostfix));

            Patch(
                harmony,
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
                nameof(JusticeBossDiagnosticHooks.LaunchWavePrefix),
                nameof(JusticeBossDiagnosticHooks.LaunchResultPostfix));

            Patch(
                harmony,
                "Spawn.LaunchGuardDropPodsNear.List",
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
                nameof(JusticeBossDiagnosticHooks.LaunchGuardPrefix),
                nameof(JusticeBossDiagnosticHooks.LaunchResultPostfix));

            Patch(
                harmony,
                "Spawn.TryGeneratePawn",
                AccessTools.Method(
                    typeof(JusticeBossSpawnUtility),
                    "TryGeneratePawn",
                    new[] { typeof(PawnKindDef), typeof(Faction) }),
                nameof(JusticeBossDiagnosticHooks.GeneratePawnPrefix),
                nameof(JusticeBossDiagnosticHooks.GeneratePawnPostfix));

            Patch(
                harmony,
                "PawnGenerator.GeneratePawn",
                AccessTools.Method(
                    typeof(PawnGenerator),
                    nameof(PawnGenerator.GeneratePawn),
                    new[] { typeof(PawnGenerationRequest) }),
                nameof(JusticeBossDiagnosticHooks.PawnGeneratorPrefix),
                nameof(JusticeBossDiagnosticHooks.PawnGeneratorPostfix));

            Patch(
                harmony,
                "Pawn.SetFaction",
                AccessTools.Method(
                    typeof(Pawn),
                    nameof(Pawn.SetFaction),
                    new[] { typeof(Faction), typeof(Pawn) }),
                nameof(JusticeBossDiagnosticHooks.SetFactionPrefix),
                nameof(JusticeBossDiagnosticHooks.SetFactionPostfix));

            Patch(
                harmony,
                "WorkMode.EnsureMobileCombatHediff",
                AccessTools.Method(
                    typeof(MechanoidMechanitorWorkModeUtility),
                    nameof(MechanoidMechanitorWorkModeUtility.EnsureMobileCombatHediff),
                    new[] { typeof(Pawn) }),
                nameof(JusticeBossDiagnosticHooks.HediffPrefix),
                nameof(JusticeBossDiagnosticHooks.HediffPostfix));

            Patch(
                harmony,
                "Spawn.LaunchPawnDropPods",
                AccessTools.Method(
                    typeof(JusticeBossSpawnUtility),
                    "LaunchPawnDropPods",
                    new[]
                    {
                        typeof(Map),
                        typeof(Faction),
                        typeof(IntVec3),
                        typeof(IntVec3),
                        typeof(int),
                        typeof(JusticeBossDropRole),
                        typeof(List<Pawn>),
                        typeof(Dictionary<Pawn, PawnKindDef>),
                        typeof(HashSet<Pawn>),
                    }),
                nameof(JusticeBossDiagnosticHooks.LaunchPawnPodsPrefix),
                nameof(JusticeBossDiagnosticHooks.LaunchResultPostfix));

            Patch(
                harmony,
                "Spawn.TryFindDropCell",
                AccessTools.Method(
                    typeof(JusticeBossSpawnUtility),
                    "TryFindDropCell",
                    new[]
                    {
                        typeof(Map),
                        typeof(IntVec3),
                        typeof(Faction),
                        typeof(List<IntVec3>),
                        typeof(IntVec3).MakeByRefType(),
                    }),
                nameof(JusticeBossDiagnosticHooks.DropCellPrefix),
                nameof(JusticeBossDiagnosticHooks.DropCellPostfix));

            Patch(
                harmony,
                "Spawn.TryMakeDropPod",
                AccessTools.Method(
                    typeof(JusticeBossSpawnUtility),
                    "TryMakeDropPod",
                    new[]
                    {
                        typeof(Map),
                        typeof(Faction),
                        typeof(IntVec3),
                        typeof(List<Pawn>),
                    }),
                nameof(JusticeBossDiagnosticHooks.DropPodPrefix),
                nameof(JusticeBossDiagnosticHooks.DropPodPostfix));

            Patch(
                harmony,
                "DropPodUtility.MakeDropPodAt",
                AccessTools.Method(
                    typeof(DropPodUtility),
                    nameof(DropPodUtility.MakeDropPodAt),
                    new[]
                    {
                        typeof(IntVec3),
                        typeof(Map),
                        typeof(ActiveTransporterInfo),
                        typeof(Faction),
                    }),
                nameof(JusticeBossDiagnosticHooks.MakeDropPodAtPrefix),
                nameof(JusticeBossDiagnosticHooks.MakeDropPodAtPostfix));

            Patch(
                harmony,
                "Deployment.DeployInfrastructure",
                AccessTools.Method(
                    typeof(JusticeBossDeploymentUtility),
                    "DeployInfrastructure",
                    new[]
                    {
                        typeof(Pawn),
                        typeof(IntVec3),
                        typeof(int),
                        typeof(int),
                        typeof(int),
                        typeof(int),
                        typeof(bool),
                        typeof(bool),
                        typeof(List<Thing>).MakeByRefType(),
                        typeof(List<PawnKindDef>).MakeByRefType(),
                        typeof(Lord).MakeByRefType(),
                    }),
                nameof(JusticeBossDiagnosticHooks.InfrastructurePrefix),
                nameof(JusticeBossDiagnosticHooks.InfrastructurePostfix));

            Patch(
                harmony,
                "Deployment.TryFindPlacement",
                AccessTools.Method(
                    typeof(JusticeBossDeploymentUtility),
                    "TryFindPlacement",
                    new[]
                    {
                        typeof(Map),
                        typeof(IntVec3),
                        typeof(ThingDef),
                        typeof(float),
                        typeof(float),
                        typeof(Rot4?),
                        typeof(List<IntVec3>),
                        typeof(IntVec3).MakeByRefType(),
                        typeof(Rot4).MakeByRefType(),
                    }),
                nameof(JusticeBossDiagnosticHooks.PlacementPrefix),
                nameof(JusticeBossDiagnosticHooks.PlacementPostfix));

            Patch(
                harmony,
                "Deployment.SpawnBuildingViaDropPod",
                AccessTools.Method(
                    typeof(JusticeBossDeploymentUtility),
                    "SpawnBuildingViaDropPod",
                    new[]
                    {
                        typeof(Map),
                        typeof(Faction),
                        typeof(ThingDef),
                        typeof(IntVec3),
                        typeof(Rot4),
                    }),
                nameof(JusticeBossDiagnosticHooks.InfrastructurePodPrefix),
                nameof(JusticeBossDiagnosticHooks.InfrastructurePodPostfix));

            Patch(
                harmony,
                "Tracker.Register",
                AccessTools.Method(
                    typeof(MapComponent_JusticeBossDropTracker),
                    "Register",
                    new[] { typeof(PendingJusticeBossDrop) }),
                nameof(JusticeBossDiagnosticHooks.RegisterPrefix),
                nameof(JusticeBossDiagnosticHooks.RegisterPostfix));

            Patch(
                harmony,
                "Tracker.Unregister",
                AccessTools.Method(
                    typeof(MapComponent_JusticeBossDropTracker),
                    "Unregister",
                    new[] { typeof(Pawn) }),
                nameof(JusticeBossDiagnosticHooks.UnregisterPrefix),
                null,
                finalizerName: null);

            Patch(
                harmony,
                "Tracker.IsInTransit",
                AccessTools.Method(
                    typeof(MapComponent_JusticeBossDropTracker),
                    "IsInTransit",
                    new[] { typeof(Pawn) }),
                null,
                nameof(JusticeBossDiagnosticHooks.TransitPostfix),
                finalizerName: null);

            Patch(
                harmony,
                "Tracker.CompleteLanding",
                AccessTools.Method(
                    typeof(MapComponent_JusticeBossDropTracker),
                    "CompleteLanding",
                    new[] { typeof(PendingJusticeBossDrop) }),
                nameof(JusticeBossDiagnosticHooks.LandingPrefix),
                nameof(JusticeBossDiagnosticHooks.LandingPostfix));

            Patch(
                harmony,
                "Lord.EnsureAssaultLord",
                AccessTools.Method(
                    typeof(JusticeBossLordUtility),
                    nameof(JusticeBossLordUtility.EnsureAssaultLord),
                    new[] { typeof(Map), typeof(Faction), typeof(int) }),
                nameof(JusticeBossDiagnosticHooks.LordPrefix),
                nameof(JusticeBossDiagnosticHooks.LordPostfix));

            Patch(
                harmony,
                "Lord.EnsureGuardLord",
                AccessTools.Method(
                    typeof(JusticeBossLordUtility),
                    nameof(JusticeBossLordUtility.EnsureGuardLord),
                    new[] { typeof(Map), typeof(Faction), typeof(int), typeof(IntVec3) }),
                nameof(JusticeBossDiagnosticHooks.LordPrefix),
                nameof(JusticeBossDiagnosticHooks.LordPostfix));

            Patch(
                harmony,
                "Lord.AddPawn",
                AccessTools.Method(
                    typeof(Lord),
                    nameof(Lord.AddPawn),
                    new[] { typeof(Pawn) }),
                nameof(JusticeBossDiagnosticHooks.AddPawnPrefix),
                nameof(JusticeBossDiagnosticHooks.AddPawnPostfix));

            Patch(
                harmony,
                "PawnJobTracker.EndCurrentJob",
                AccessTools.Method(
                    typeof(Pawn_JobTracker),
                    nameof(Pawn_JobTracker.EndCurrentJob),
                    new[] { typeof(JobCondition), typeof(bool), typeof(bool) }),
                nameof(JusticeBossDiagnosticHooks.EndJobPrefix),
                nameof(JusticeBossDiagnosticHooks.EndJobPostfix));
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

            int retryCount = Traverse.Create(__instance)
                .Field("waveDropRetryCount")
                .GetValue<int>();
            JusticeBossDiagnosticContext context = new JusticeBossDiagnosticContext
            {
                eventId = __instance.JusticeEventId,
                waveIndex = __instance.WaveCount + 1,
                retryCount = retryCount,
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

            JusticeBossDiagnosticContext context =
                JusticeBossDiagnosticUtility.ActiveContext!;
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
            PawnGenerationRequest request,
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
                "requestKind=" + (request.KindDef?.defName ?? "null"));
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
            Faction? newFaction,
            out JusticeBossDiagnosticScopeState __state)
        {
            if (!HasActiveFlow)
            {
                __state = default;
                return;
            }

            __state = JusticeBossDiagnosticUtility.BeginScope(
                "Pawn.SetFaction",
                "pawnId=" + (__instance?.thingIDNumber ?? -1)
                    + " from=" + (__instance?.Faction?.def?.defName ?? "null")
                    + " to=" + (newFaction?.def?.defName ?? "null"));
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
            IntVec3 center,
            IntVec3 anchor,
            JusticeBossDropRole role,
            List<Pawn>? pawns,
            out JusticeBossDiagnosticScopeState __state)
        {
            if (!HasActiveFlow)
            {
                __state = default;
                return;
            }

            JusticeBossDiagnosticUtility.ActiveContext!.role = role.ToString();
            __state = JusticeBossDiagnosticUtility.BeginScope(
                "DropLaunch",
                "pawns=" + (pawns?.Count ?? 0)
                    + " center=" + center
                    + " anchor=" + anchor);
        }

        public static void DropCellPrefix(
            Map map,
            IntVec3 center,
            List<IntVec3>? reserved,
            out JusticeBossDiagnosticScopeState __state)
        {
            if (!HasActiveFlow)
            {
                __state = default;
                return;
            }

            JusticeBossDiagnosticContext context =
                JusticeBossDiagnosticUtility.ActiveContext!;
            context.dropCellSequence++;
            __state = JusticeBossDiagnosticUtility.BeginScope(
                "DropCell.Search",
                "searchIndex=" + context.dropCellSequence
                    + " map=" + (map?.uniqueID ?? -1)
                    + " center=" + center
                    + " reserved=" + (reserved?.Count ?? 0));
        }

        public static void DropCellPostfix(
            bool __result,
            ref IntVec3 cell,
            JusticeBossDiagnosticScopeState __state)
        {
            if (!__state.active)
            {
                return;
            }

            JusticeBossDiagnosticUtility.EndScope(
                __state,
                "success=" + __result + " cell=" + cell,
                JusticeBossDiagnosticUtility.DropCellSlowMs);
        }

        public static void DropPodPrefix(
            IntVec3 cell,
            List<Pawn>? group,
            out JusticeBossDiagnosticScopeState __state)
        {
            if (!HasActiveFlow)
            {
                __state = default;
                return;
            }

            JusticeBossDiagnosticContext context =
                JusticeBossDiagnosticUtility.ActiveContext!;
            int previousPodIndex = context.activePodIndex;
            context.podSequence++;
            context.activePodIndex = context.podSequence;

            __state = JusticeBossDiagnosticUtility.BeginScope(
                "DropPod.Create",
                "cell=" + cell + " pawnCount=" + (group?.Count ?? 0));
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
            IntVec3 c,
            Map map,
            ActiveTransporterInfo? info,
            out JusticeBossDiagnosticScopeState __state)
        {
            if (!HasActiveFlow)
            {
                __state = default;
                return;
            }

            __state = JusticeBossDiagnosticUtility.BeginScope(
                "DropPod.MakeDropPodAt",
                "cell=" + c
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
            Pawn? justice,
            IntVec3 anchorCell,
            int justiceEventId,
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
                role = "Infrastructure",
            };
            __state = JusticeBossDiagnosticUtility.BeginScope(
                "Infrastructure.Deploy",
                "justiceId=" + (justice?.thingIDNumber ?? -1)
                    + " anchor=" + anchorCell,
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

            List<Thing>? deployed = __args.Length > 8
                ? __args[8] as List<Thing>
                : null;
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
            IntVec3 anchor,
            ThingDef? def,
            out JusticeBossDiagnosticScopeState __state)
        {
            if (!HasActiveFlow)
            {
                __state = default;
                return;
            }

            JusticeBossDiagnosticContext context =
                JusticeBossDiagnosticUtility.ActiveContext!;
            context.placementSequence++;
            __state = JusticeBossDiagnosticUtility.BeginScope(
                "Infrastructure.Placement",
                "placementIndex=" + context.placementSequence
                    + " def=" + (def?.defName ?? "null")
                    + " anchor=" + anchor);
        }

        public static void PlacementPostfix(
            bool __result,
            ref IntVec3 cell,
            ref Rot4 rot,
            JusticeBossDiagnosticScopeState __state)
        {
            if (!__state.active)
            {
                return;
            }

            JusticeBossDiagnosticUtility.EndScope(
                __state,
                "success=" + __result
                    + " cell=" + cell
                    + " rot=" + rot,
                JusticeBossDiagnosticUtility.DropCellSlowMs);
        }

        public static void InfrastructurePodPrefix(
            ThingDef? def,
            IntVec3 cell,
            Rot4 rot,
            out JusticeBossDiagnosticScopeState __state)
        {
            if (!HasActiveFlow)
            {
                __state = default;
                return;
            }

            JusticeBossDiagnosticContext context =
                JusticeBossDiagnosticUtility.ActiveContext!;
            int previousPodIndex = context.activePodIndex;
            context.podSequence++;
            context.activePodIndex = context.podSequence;

            __state = JusticeBossDiagnosticUtility.BeginScope(
                "Infrastructure.DropPod",
                "def=" + (def?.defName ?? "null")
                    + " cell=" + cell
                    + " rot=" + rot);
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
            Map map,
            out JusticeBossDiagnosticScopeState __state)
        {
            if (!HasActiveFlow)
            {
                __state = default;
                return;
            }

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
            Pawn? p,
            out JusticeBossDiagnosticScopeState __state)
        {
            if (!HasActiveFlow)
            {
                __state = default;
                return;
            }

            __state = JusticeBossDiagnosticUtility.BeginScope(
                "Lord.AddPawn",
                "pawnId=" + (p?.thingIDNumber ?? -1)
                    + " beforeCount=" + (__instance?.ownedPawns?.Count ?? -1));
        }

        public static void AddPawnPostfix(
            Lord __instance,
            Pawn? p,
            JusticeBossDiagnosticScopeState __state)
        {
            if (!__state.active)
            {
                return;
            }

            JusticeBossDiagnosticUtility.EndScope(
                __state,
                "pawnId=" + (p?.thingIDNumber ?? -1)
                    + " afterCount=" + (__instance?.ownedPawns?.Count ?? -1),
                JusticeBossDiagnosticUtility.LandingSlowMs);
        }

        public static void EndJobPrefix(
            JobCondition condition,
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
                "condition=" + condition);
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
