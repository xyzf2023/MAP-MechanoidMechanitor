using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;
using Verse.AI;
using Verse.Profile;

namespace MAP_MechanoidMechanitor
{
    internal static class JusticeBossPostLandingTracePatchManager
    {
        private const string HarmonyId =
            "MAP_MechanoidMechanitor.JusticeBossPostLandingTrace";

        private static readonly Harmony Harmony = new Harmony(HarmonyId);
        private static bool installed;
        private static AccessTools.FieldRef<JobDriver, List<Toil>>? jobDriverToils;

        internal static bool Installed => installed;

        internal static List<Toil>? GetToils(JobDriver? driver)
        {
            if (driver == null || jobDriverToils == null)
            {
                return null;
            }

            try
            {
                return jobDriverToils(driver);
            }
            catch
            {
                return null;
            }
        }

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

            if (!JusticeBossTraceFileWriter.OpenSession())
            {
                return;
            }

            JusticeBossPostLandingTraceState.OnSessionOpened();

            try
            {
                jobDriverToils =
                    AccessTools.FieldRefAccess<JobDriver, List<Toil>>("toils");

                PatchRequired(
                    "Controller.InitializeOnArrival",
                    AccessTools.Method(
                        typeof(CompJusticeBossController),
                        nameof(CompJusticeBossController.InitializeOnArrival),
                        Type.EmptyTypes),
                    null,
                    nameof(JusticeBossPostLandingTraceHooks.ControllerInitializedPostfix),
                    null);

                PatchRequired(
                    "Controller.TrySpawnWave",
                    AccessTools.Method(
                        typeof(CompJusticeBossController),
                        "TrySpawnWave",
                        new[] { typeof(bool) }),
                    null,
                    nameof(JusticeBossPostLandingTraceHooks.WaveProgressPostfix),
                    null);

                PatchRequired(
                    "Controller.PostDestroy",
                    AccessTools.Method(
                        typeof(CompJusticeBossController),
                        nameof(CompJusticeBossController.PostDestroy),
                        new[] { typeof(DestroyMode), typeof(Map) }),
                    null,
                    nameof(JusticeBossPostLandingTraceHooks.ControllerDestroyedPostfix),
                    null);

                PatchRequired(
                    "Controller.Notify_Killed",
                    AccessTools.Method(
                        typeof(CompJusticeBossController),
                        nameof(CompJusticeBossController.Notify_Killed),
                        new[] { typeof(Map), typeof(DamageInfo?) }),
                    null,
                    nameof(JusticeBossPostLandingTraceHooks.ControllerKilledPostfix),
                    null);

                PatchRequired(
                    "Tracker.MapComponentTick",
                    AccessTools.Method(
                        typeof(MapComponent_JusticeBossDropTracker),
                        nameof(MapComponent_JusticeBossDropTracker.MapComponentTick),
                        Type.EmptyTypes),
                    nameof(JusticeBossPostLandingTraceHooks.LandingBatchPrefix),
                    null,
                    nameof(JusticeBossPostLandingTraceHooks.LandingBatchFinalizer));

                PatchRequired(
                    "Tracker.CompleteLanding",
                    AccessTools.Method(
                        typeof(MapComponent_JusticeBossDropTracker),
                        "CompleteLanding",
                        new[] { typeof(PendingJusticeBossDrop) }),
                    null,
                    nameof(JusticeBossPostLandingTraceHooks.CompleteLandingPostfix),
                    null);

                PatchRequired(
                    "TickManager.DoSingleTick",
                    AccessTools.Method(
                        typeof(TickManager),
                        nameof(TickManager.DoSingleTick),
                        Type.EmptyTypes),
                    nameof(JusticeBossPostLandingTraceHooks.GameTickPrefix),
                    null,
                    nameof(JusticeBossPostLandingTraceHooks.GameTickFinalizer));

                PatchRequired(
                    "Pawn.Tick",
                    AccessTools.Method(typeof(Pawn), "Tick", Type.EmptyTypes),
                    nameof(JusticeBossPostLandingTraceHooks.PawnTickPrefix),
                    null,
                    nameof(JusticeBossPostLandingTraceHooks.PawnTickFinalizer));

                PatchRequired(
                    "Pawn_JobTracker.JobTrackerTick",
                    AccessTools.Method(
                        typeof(Pawn_JobTracker),
                        nameof(Pawn_JobTracker.JobTrackerTick),
                        Type.EmptyTypes),
                    nameof(JusticeBossPostLandingTraceHooks.JobTrackerTickPrefix),
                    null,
                    nameof(JusticeBossPostLandingTraceHooks.JobTrackerTickFinalizer));

                PatchRequired(
                    "JobDriver.DriverTick",
                    AccessTools.Method(
                        typeof(JobDriver),
                        nameof(JobDriver.DriverTick),
                        Type.EmptyTypes),
                    nameof(JusticeBossPostLandingTraceHooks.DriverTickPrefix),
                    null,
                    nameof(JusticeBossPostLandingTraceHooks.DriverTickFinalizer));

                PatchRequired(
                    "Pawn_PathFollower.PatherTick",
                    AccessTools.Method(
                        typeof(Pawn_PathFollower),
                        nameof(Pawn_PathFollower.PatherTick),
                        Type.EmptyTypes),
                    nameof(JusticeBossPostLandingTraceHooks.PatherTickPrefix),
                    null,
                    nameof(JusticeBossPostLandingTraceHooks.PatherTickFinalizer));

                PatchRequired(
                    "MemoryUtility.ClearAllMapsAndWorld",
                    AccessTools.Method(
                        typeof(MemoryUtility),
                        nameof(MemoryUtility.ClearAllMapsAndWorld),
                        Type.EmptyTypes),
                    null,
                    nameof(JusticeBossPostLandingTraceHooks.ClearAllMapsPostfix),
                    null);

                installed = true;
                JusticeBossTraceFileWriter.Write(
                    "PATCH_INSTALL_END",
                    "module=postLandingTrace result=success file="
                        + JusticeBossTraceFormatting.Sanitize(
                            JusticeBossTraceFileWriter.TraceFilePath),
                    JusticeBossTraceWriteMode.Critical);
            }
            catch (Exception exception)
            {
                Harmony.UnpatchAll(HarmonyId);
                installed = false;
                jobDriverToils = null;
                JusticeBossTraceFileWriter.Write(
                    "PATCH_INSTALL_EXCEPTION",
                    "module=postLandingTrace "
                        + JusticeBossTraceFormatting.DescribeException(exception),
                    JusticeBossTraceWriteMode.Emergency);
                JusticeBossPostLandingTraceState.OnSessionClosing("install-failed");
                JusticeBossTraceFileWriter.CloseSession("install-failed");
                Log.Error(
                    "[MAP JusticeBoss Trace] 落地后关键轨迹补丁安装失败："
                        + exception);
            }
        }

        private static void Uninstall()
        {
            if (installed)
            {
                JusticeBossTraceFileWriter.Write(
                    "PATCH_UNINSTALL_BEGIN",
                    "module=postLandingTrace result=pending",
                    JusticeBossTraceWriteMode.Emergency);
                Harmony.UnpatchAll(HarmonyId);
                installed = false;
            }

            jobDriverToils = null;
            JusticeBossPostLandingTraceState.OnSessionClosing("disabled");
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
                typeof(JusticeBossPostLandingTraceHooks),
                name);
            if (method == null)
            {
                throw new MissingMethodException(
                    typeof(JusticeBossPostLandingTraceHooks).FullName,
                    name);
            }

            return method;
        }
    }

    internal static class JusticeBossPostLandingTraceHooks
    {
        public static void ControllerInitializedPostfix(
            CompJusticeBossController __instance)
        {
            JusticeBossPostLandingTraceState.NotifyEventRegistered(
                __instance?.JusticeEventId ?? 0,
                __instance?.parent?.Map?.uniqueID ?? -1);
        }

        public static void WaveProgressPostfix(
            int ___justiceEventId,
            int ___waveCount,
            int ___difficultyTotalWaves)
        {
            JusticeBossPostLandingTraceState.NotifyWaveProgress(
                ___justiceEventId,
                ___waveCount,
                ___difficultyTotalWaves);
        }

        public static void ControllerDestroyedPostfix(
            CompJusticeBossController __instance)
        {
            JusticeBossPostLandingTraceState.NotifyEventCleared(
                __instance?.JusticeEventId ?? 0,
                "controller-destroyed");
        }

        public static void ControllerKilledPostfix(
            CompJusticeBossController __instance)
        {
            JusticeBossPostLandingTraceState.NotifyEventCleared(
                __instance?.JusticeEventId ?? 0,
                "boss-killed");
        }

        public static void LandingBatchPrefix(
            MapComponent_JusticeBossDropTracker __instance,
            out JusticeBossLandingBatchState __state)
        {
            __state = default;
            if (!JusticeBossTraceFileWriter.CanWriteNormal
                || __instance == null
                || __instance.PendingCount <= 0
                || __instance.map == null
                || !__instance.map.IsHashIntervalTick(10))
            {
                return;
            }

            __state = JusticeBossPostLandingTraceState.BeginLandingBatch(
                __instance.map.uniqueID,
                __instance.PendingCount);
        }

        public static Exception? LandingBatchFinalizer(
            MapComponent_JusticeBossDropTracker __instance,
            Exception? __exception,
            JusticeBossLandingBatchState __state)
        {
            JusticeBossPostLandingTraceState.EndLandingBatch(
                __state,
                __instance?.PendingCount ?? 0,
                __exception);
            return __exception;
        }

        public static void CompleteLandingPostfix(
            MapComponent_JusticeBossDropTracker __instance,
            PendingJusticeBossDrop entry)
        {
            if (entry == null)
            {
                return;
            }

            JusticeBossPostLandingTraceState.NotifyPawnLanded(
                entry.pawn,
                entry.justiceEventId,
                entry.role,
                __instance?.map?.uniqueID ?? -1);
        }

        public static void ClearAllMapsPostfix()
        {
            JusticeBossPostLandingTraceState.ResetAll(
                "maps-and-world-cleared",
                writeReset: true);
            JusticeBossTraceFileWriter.FlushBuffered();
        }

        public static void GameTickPrefix(out JusticeBossGameTickState __state)
        {
            JusticeBossPostLandingTraceState.TryBeginGameTick(out __state);
        }

        public static Exception? GameTickFinalizer(
            Exception? __exception,
            JusticeBossGameTickState __state)
        {
            JusticeBossPostLandingTraceState.EndGameTick(__state, __exception);
            return __exception;
        }

        public static void PawnTickPrefix(
            Pawn __instance,
            out JusticeBossPawnTickState __state)
        {
            __state = default;
            if (!JusticeBossPostLandingTraceState.AnyWindowActive
                || !JusticeBossPostLandingTraceState.TryBeginPawnTick(
                    __instance,
                    out __state))
            {
                return;
            }

            JusticeBossPostLandingTraceState.WriteContextTrace(
                "PAWN_TICK_BEGIN",
                null,
                JusticeBossTraceWriteMode.Critical);
            if (JusticeBossTraceFileWriter.CanWriteNormal)
            {
                JusticeBossPostLandingTraceState.WriteContextTrace(
                    "PAWN_TICK_STATE",
                    JusticeBossTraceFormatting.DescribePawnState(__instance),
                    JusticeBossTraceWriteMode.Buffered);
            }
        }

        public static Exception? PawnTickFinalizer(
            Pawn __instance,
            Exception? __exception,
            JusticeBossPawnTickState __state)
        {
            if (!__state.active)
            {
                return __exception;
            }

            try
            {
                if (__exception != null)
                {
                    JusticeBossPostLandingTraceState.WriteContextTrace(
                        "PAWN_TICK_EXCEPTION",
                        JusticeBossTraceFormatting.DescribeException(__exception),
                        JusticeBossTraceWriteMode.Emergency);
                }
                else
                {
                    JusticeBossPostLandingTraceState.WriteContextTrace(
                        "PAWN_TICK_END",
                        "elapsedMs="
                            + JusticeBossTraceFormatting.FormatElapsed(
                                __state.context?.startedTimestamp ?? 0L),
                        JusticeBossTraceWriteMode.Buffered);
                    if (JusticeBossTraceFileWriter.CanWriteNormal)
                    {
                        JusticeBossPostLandingTraceState.WriteContextTrace(
                            "PAWN_TICK_STATE_AFTER",
                            JusticeBossTraceFormatting.DescribePawnState(__instance),
                            JusticeBossTraceWriteMode.Buffered);
                    }
                }
            }
            finally
            {
                JusticeBossPostLandingTraceState.EndPawnTick(__state);
            }

            return __exception;
        }

        public static void JobTrackerTickPrefix(
            Pawn_JobTracker __instance,
            Pawn ___pawn,
            out JusticeBossNestedTraceState __state)
        {
            __state = default;
            if (!JusticeBossPostLandingTraceState.HasPawnContext)
            {
                return;
            }

            JusticeBossPawnTraceContext? context =
                JusticeBossPostLandingTraceState.CurrentPawnContext;
            if (context == null || !ReferenceEquals(context.pawn, ___pawn))
            {
                return;
            }

            __state = JusticeBossPostLandingTraceState.BeginNestedStage();
            JusticeBossPostLandingTraceState.WriteContextTrace(
                "JOB_TRACKER_TICK_BEGIN",
                null,
                JusticeBossTraceWriteMode.Critical);
            if (JusticeBossTraceFileWriter.CanWriteNormal)
            {
                JusticeBossPostLandingTraceState.WriteContextTrace(
                    "JOB_TRACKER_TICK_STATE",
                    JusticeBossTraceFormatting.DescribeJobTrackerState(__instance),
                    JusticeBossTraceWriteMode.Buffered);
            }
        }

        public static Exception? JobTrackerTickFinalizer(
            Pawn_JobTracker __instance,
            Exception? __exception,
            JusticeBossNestedTraceState __state)
        {
            if (!__state.active)
            {
                return __exception;
            }

            if (__exception != null)
            {
                JusticeBossPostLandingTraceState.WriteContextTrace(
                    "JOB_TRACKER_TICK_EXCEPTION",
                    JusticeBossTraceFormatting.DescribeException(__exception),
                    JusticeBossTraceWriteMode.Emergency);
            }
            else
            {
                JusticeBossPostLandingTraceState.WriteContextTrace(
                    "JOB_TRACKER_TICK_END",
                    "elapsedMs="
                        + JusticeBossTraceFormatting.FormatElapsed(
                            __state.startedTimestamp),
                    JusticeBossTraceWriteMode.Buffered);
                if (JusticeBossTraceFileWriter.CanWriteNormal)
                {
                    JusticeBossPostLandingTraceState.WriteContextTrace(
                        "JOB_TRACKER_TICK_STATE_AFTER",
                        JusticeBossTraceFormatting.DescribeJobTrackerState(__instance),
                        JusticeBossTraceWriteMode.Buffered);
                }
            }

            return __exception;
        }

        public static void DriverTickPrefix(
            JobDriver __instance,
            out JusticeBossNestedTraceState __state)
        {
            __state = default;
            if (!JusticeBossPostLandingTraceState.HasPawnContext)
            {
                return;
            }

            JusticeBossPawnTraceContext? context =
                JusticeBossPostLandingTraceState.CurrentPawnContext;
            if (context == null
                || __instance == null
                || !ReferenceEquals(context.pawn, __instance.pawn))
            {
                return;
            }

            __state = JusticeBossPostLandingTraceState.BeginNestedStage();
            JusticeBossPostLandingTraceState.WriteContextTrace(
                "JOB_DRIVER_TICK_BEGIN",
                null,
                JusticeBossTraceWriteMode.Critical);
            if (JusticeBossTraceFileWriter.CanWriteNormal)
            {
                JusticeBossPostLandingTraceState.WriteContextTrace(
                    "JOB_DRIVER_TICK_STATE",
                    JusticeBossTraceFormatting.DescribeJobDriverState(
                        __instance,
                        JusticeBossPostLandingTracePatchManager.GetToils(__instance)),
                    JusticeBossTraceWriteMode.Buffered);
            }
        }

        public static Exception? DriverTickFinalizer(
            JobDriver __instance,
            Exception? __exception,
            JusticeBossNestedTraceState __state)
        {
            if (!__state.active)
            {
                return __exception;
            }

            if (__exception != null)
            {
                JusticeBossPostLandingTraceState.WriteContextTrace(
                    "JOB_DRIVER_TICK_EXCEPTION",
                    JusticeBossTraceFormatting.DescribeException(__exception),
                    JusticeBossTraceWriteMode.Emergency);
            }
            else
            {
                JusticeBossPostLandingTraceState.WriteContextTrace(
                    "JOB_DRIVER_TICK_END",
                    "elapsedMs="
                        + JusticeBossTraceFormatting.FormatElapsed(
                            __state.startedTimestamp),
                    JusticeBossTraceWriteMode.Buffered);
                if (JusticeBossTraceFileWriter.CanWriteNormal)
                {
                    JusticeBossPostLandingTraceState.WriteContextTrace(
                        "JOB_DRIVER_TICK_STATE_AFTER",
                        JusticeBossTraceFormatting.DescribeJobDriverState(
                            __instance,
                            JusticeBossPostLandingTracePatchManager.GetToils(__instance)),
                        JusticeBossTraceWriteMode.Buffered);
                }
            }

            return __exception;
        }

        public static void PatherTickPrefix(
            Pawn_PathFollower __instance,
            Pawn ___pawn,
            IntVec3 ___lastCell,
            out JusticeBossNestedTraceState __state)
        {
            __state = default;
            if (!JusticeBossPostLandingTraceState.HasPawnContext)
            {
                return;
            }

            JusticeBossPawnTraceContext? context =
                JusticeBossPostLandingTraceState.CurrentPawnContext;
            if (context == null || !ReferenceEquals(context.pawn, ___pawn))
            {
                return;
            }

            __state = JusticeBossPostLandingTraceState.BeginNestedStage();
            JusticeBossPostLandingTraceState.WriteContextTrace(
                "PATHER_TICK_BEGIN",
                null,
                JusticeBossTraceWriteMode.Critical);
            if (JusticeBossTraceFileWriter.CanWriteNormal)
            {
                JusticeBossPostLandingTraceState.WriteContextTrace(
                    "PATHER_TICK_STATE",
                    JusticeBossTraceFormatting.DescribePatherState(
                        __instance,
                        ___lastCell),
                    JusticeBossTraceWriteMode.Buffered);
            }
        }

        public static Exception? PatherTickFinalizer(
            Pawn_PathFollower __instance,
            IntVec3 ___lastCell,
            Exception? __exception,
            JusticeBossNestedTraceState __state)
        {
            if (!__state.active)
            {
                return __exception;
            }

            if (__exception != null)
            {
                JusticeBossPostLandingTraceState.WriteContextTrace(
                    "PATHER_TICK_EXCEPTION",
                    JusticeBossTraceFormatting.DescribeException(__exception),
                    JusticeBossTraceWriteMode.Emergency);
            }
            else
            {
                JusticeBossPostLandingTraceState.WriteContextTrace(
                    "PATHER_TICK_END",
                    "elapsedMs="
                        + JusticeBossTraceFormatting.FormatElapsed(
                            __state.startedTimestamp),
                    JusticeBossTraceWriteMode.Buffered);
                if (JusticeBossTraceFileWriter.CanWriteNormal)
                {
                    JusticeBossPostLandingTraceState.WriteContextTrace(
                        "PATHER_TICK_STATE_AFTER",
                        JusticeBossTraceFormatting.DescribePatherState(
                            __instance,
                            ___lastCell),
                        JusticeBossTraceWriteMode.Buffered);
                }
            }

            return __exception;
        }
    }
}
