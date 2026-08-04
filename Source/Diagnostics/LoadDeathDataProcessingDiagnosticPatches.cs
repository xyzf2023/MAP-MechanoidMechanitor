using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 数据处理注册表及生命周期诊断。只读取持久化列表与运行时标记，
    /// 不调用任何同步、清理或规划方法。
    /// </summary>
    internal static class LoadDeathDataProcessingDiagnosticPatches
    {
        [HarmonyPatch(
            typeof(GameComponent_DataProcessingAllocationRegistry),
            nameof(GameComponent_DataProcessingAllocationRegistry.ExposeData))]
        internal static class RegistryExposeDataPatch
        {
            private static void Prefix(GameComponent_DataProcessingAllocationRegistry __instance)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Active
                        || (Scribe.mode != LoadSaveMode.LoadingVars
                            && Scribe.mode != LoadSaveMode.ResolvingCrossRefs
                            && Scribe.mode != LoadSaveMode.PostLoadInit))
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "DataRegistry.ExposeData.Enter",
                        null,
                        "MODE=" + Scribe.mode
                        + " RAW={" + LoadDeathDiagnosticUtility.BuildRawRegistrySnapshot(__instance, null) + "}",
                        false);
                }
                catch
                {
                    // 忽略。
                }
            }

            private static void Postfix(GameComponent_DataProcessingAllocationRegistry __instance)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Active
                        || (Scribe.mode != LoadSaveMode.LoadingVars
                            && Scribe.mode != LoadSaveMode.ResolvingCrossRefs
                            && Scribe.mode != LoadSaveMode.PostLoadInit))
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "DataRegistry.ExposeData.Exit",
                        null,
                        "MODE=" + Scribe.mode
                        + " RAW={" + LoadDeathDiagnosticUtility.BuildRawRegistrySnapshot(__instance, null) + "}",
                        false);
                }
                catch
                {
                    // 忽略。
                }
            }
        }

        [HarmonyPatch(
            typeof(GameComponent_DataProcessingAllocationRegistry),
            nameof(GameComponent_DataProcessingAllocationRegistry.LoadedGame))]
        internal static class RegistryLoadedGamePatch
        {
            private static void Prefix(GameComponent_DataProcessingAllocationRegistry __instance)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Active)
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "DataRegistry.LoadedGame.Enter",
                        null,
                        "RAW={" + LoadDeathDiagnosticUtility.BuildRawRegistrySnapshot(__instance, null) + "}",
                        true);
                }
                catch
                {
                    // 忽略。
                }
            }

            private static void Postfix(GameComponent_DataProcessingAllocationRegistry __instance)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Active)
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "DataRegistry.LoadedGame.Exit",
                        null,
                        "RAW={" + LoadDeathDiagnosticUtility.BuildRawRegistrySnapshot(__instance, null) + "}",
                        false);
                }
                catch
                {
                    // 忽略。
                }
            }
        }

        [HarmonyPatch(
            typeof(GameComponent_DataProcessingAllocationRegistry),
            nameof(GameComponent_DataProcessingAllocationRegistry.ClearTarget),
            new[] { typeof(Pawn) })]
        [HarmonyPriority(Priority.First)]
        internal static class RegistryClearTargetPatch
        {
            private static void Prefix(
                GameComponent_DataProcessingAllocationRegistry __instance,
                Pawn? target,
                ref string? __state)
            {
                __state = null;
                try
                {
                    if (!LoadDeathDiagnosticUtility.Active
                        || !LoadDeathDiagnosticUtility.ShouldTracePawn(target))
                    {
                        return;
                    }

                    __state = LoadDeathDiagnosticUtility.BuildRawRegistrySnapshot(__instance, target);
                    LoadDeathDiagnosticUtility.Write(
                        "DataRegistry.ClearTarget.Enter",
                        target,
                        "RAW_BEFORE={" + __state + "}"
                        + " PAWN_BEFORE={" + LoadDeathDiagnosticUtility.BuildPassivePawnSnapshot(target) + "}"
                        + " HEDIFFS_BEFORE={" + LoadDeathDiagnosticUtility.BuildPassiveHediffList(target) + "}",
                        true);
                }
                catch
                {
                    // 忽略。
                }
            }

            private static void Postfix(
                GameComponent_DataProcessingAllocationRegistry __instance,
                Pawn? target,
                bool __runOriginal,
                string? __state)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Active || __state == null)
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "DataRegistry.ClearTarget.Exit",
                        target,
                        "RUN_ORIGINAL=" + __runOriginal
                        + " RAW_BEFORE={" + __state + "}"
                        + " RAW_AFTER={" + LoadDeathDiagnosticUtility.BuildRawRegistrySnapshot(__instance, target) + "}"
                        + " PAWN_AFTER={" + LoadDeathDiagnosticUtility.BuildPassivePawnSnapshot(target) + "}"
                        + " HEDIFFS_AFTER={" + LoadDeathDiagnosticUtility.BuildPassiveHediffList(target) + "}",
                        false);
                }
                catch
                {
                    // 忽略。
                }
            }
        }

        [HarmonyPatch(
            typeof(DataProcessingPawnLifecycleCoordinator),
            nameof(DataProcessingPawnLifecycleCoordinator.SuspendOverseerRuntime),
            new[] { typeof(GameComponent_DataProcessingAllocationRegistry), typeof(Pawn) })]
        [HarmonyPriority(Priority.First)]
        internal static class SuspendOverseerRuntimePatch
        {
            private static void Prefix(
                GameComponent_DataProcessingAllocationRegistry registry,
                Pawn? overseer,
                ref string? __state)
            {
                __state = null;
                try
                {
                    if (!LoadDeathDiagnosticUtility.Active
                        || !LoadDeathDiagnosticUtility.ShouldTracePawn(overseer))
                    {
                        return;
                    }

                    __state = LoadDeathDiagnosticUtility.BuildRawRegistrySnapshot(registry, overseer);
                    LoadDeathDiagnosticUtility.Write(
                        "Lifecycle.SuspendOverseerRuntime.Enter",
                        overseer,
                        "RAW_BEFORE={" + __state + "}"
                        + " PAWN_BEFORE={" + LoadDeathDiagnosticUtility.BuildPassivePawnSnapshot(overseer) + "}"
                        + " HEDIFFS_BEFORE={" + LoadDeathDiagnosticUtility.BuildPassiveHediffList(overseer) + "}",
                        true);
                }
                catch
                {
                    // 忽略。
                }
            }

            private static void Postfix(
                GameComponent_DataProcessingAllocationRegistry registry,
                Pawn? overseer,
                bool __runOriginal,
                string? __state)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Active || __state == null)
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "Lifecycle.SuspendOverseerRuntime.Exit",
                        overseer,
                        "RUN_ORIGINAL=" + __runOriginal
                        + " RAW_BEFORE={" + __state + "}"
                        + " RAW_AFTER={" + LoadDeathDiagnosticUtility.BuildRawRegistrySnapshot(registry, overseer) + "}"
                        + " PAWN_AFTER={" + LoadDeathDiagnosticUtility.BuildPassivePawnSnapshot(overseer) + "}"
                        + " HEDIFFS_AFTER={" + LoadDeathDiagnosticUtility.BuildPassiveHediffList(overseer) + "}",
                        false);
                }
                catch
                {
                    // 忽略。
                }
            }
        }

        [HarmonyPatch(
            typeof(GameComponent_DataProcessingAllocationRegistry),
            nameof(GameComponent_DataProcessingAllocationRegistry.SyncHediffForTarget),
            new[] { typeof(Pawn) })]
        internal static class SyncHediffForTargetPatch
        {
            private static void Prefix(
                GameComponent_DataProcessingAllocationRegistry __instance,
                Pawn? target,
                ref LoadDeathPawnState? __state)
            {
                CaptureRegistryPawnOperation(
                    __instance,
                    target,
                    "DataRegistry.SyncHediffForTarget.Enter",
                    "GameComponent_DataProcessingAllocationRegistry.SyncHediffForTarget",
                    ref __state);
            }

            private static void Postfix(
                GameComponent_DataProcessingAllocationRegistry __instance,
                Pawn? target,
                LoadDeathPawnState? __state)
            {
                CompleteRegistryPawnOperation(
                    __instance,
                    target,
                    "DataRegistry.SyncHediffForTarget.Exit",
                    "GameComponent_DataProcessingAllocationRegistry.SyncHediffForTarget",
                    __state);
            }
        }

        [HarmonyPatch(
            typeof(GameComponent_DataProcessingAllocationRegistry),
            nameof(GameComponent_DataProcessingAllocationRegistry.SyncHediffsForOverseer),
            new[] { typeof(Pawn) })]
        internal static class SyncHediffsForOverseerPatch
        {
            private static void Prefix(
                GameComponent_DataProcessingAllocationRegistry __instance,
                Pawn? overseer,
                ref LoadDeathPawnState? __state)
            {
                CaptureRegistryPawnOperation(
                    __instance,
                    overseer,
                    "DataRegistry.SyncHediffsForOverseer.Enter",
                    "GameComponent_DataProcessingAllocationRegistry.SyncHediffsForOverseer",
                    ref __state);
            }

            private static void Postfix(
                GameComponent_DataProcessingAllocationRegistry __instance,
                Pawn? overseer,
                LoadDeathPawnState? __state)
            {
                CompleteRegistryPawnOperation(
                    __instance,
                    overseer,
                    "DataRegistry.SyncHediffsForOverseer.Exit",
                    "GameComponent_DataProcessingAllocationRegistry.SyncHediffsForOverseer",
                    __state);
            }
        }

        [HarmonyPatch(
            typeof(GameComponent_DataProcessingAllocationRegistry),
            nameof(GameComponent_DataProcessingAllocationRegistry.ClearOverseer),
            new[] { typeof(Pawn) })]
        [HarmonyPriority(Priority.First)]
        internal static class ClearOverseerPatch
        {
            private static void Prefix(
                GameComponent_DataProcessingAllocationRegistry __instance,
                Pawn? overseer,
                ref string? __state)
            {
                __state = null;
                try
                {
                    if (!LoadDeathDiagnosticUtility.Active
                        || !LoadDeathDiagnosticUtility.ShouldTracePawn(overseer))
                    {
                        return;
                    }

                    __state = LoadDeathDiagnosticUtility.BuildRawRegistrySnapshot(__instance, overseer);
                    LoadDeathDiagnosticUtility.Write(
                        "DataRegistry.ClearOverseer.Enter",
                        overseer,
                        "RAW_BEFORE={" + __state + "}"
                        + " PAWN_BEFORE={" + LoadDeathDiagnosticUtility.BuildPassivePawnSnapshot(overseer) + "}",
                        true);
                }
                catch
                {
                    // 忽略。
                }
            }

            private static void Postfix(
                GameComponent_DataProcessingAllocationRegistry __instance,
                Pawn? overseer,
                bool __runOriginal,
                string? __state)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Active || __state == null)
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "DataRegistry.ClearOverseer.Exit",
                        overseer,
                        "RUN_ORIGINAL=" + __runOriginal
                        + " RAW_BEFORE={" + __state + "}"
                        + " RAW_AFTER={" + LoadDeathDiagnosticUtility.BuildRawRegistrySnapshot(__instance, overseer) + "}"
                        + " PAWN_AFTER={" + LoadDeathDiagnosticUtility.BuildPassivePawnSnapshot(overseer) + "}",
                        false);
                }
                catch
                {
                    // 忽略。
                }
            }
        }

        [HarmonyPatch(
            typeof(GameComponent_DataProcessingAllocationRegistry),
            nameof(GameComponent_DataProcessingAllocationRegistry.PrepareForExternalConsciousnessLoss),
            new[] { typeof(Pawn), typeof(float) })]
        internal static class PrepareForExternalConsciousnessLossPatch
        {
            private static void Prefix(
                GameComponent_DataProcessingAllocationRegistry __instance,
                Pawn? overseer,
                float consciousnessOffsetLoss,
                ref LoadDeathPawnState? __state)
            {
                CaptureRegistryPawnOperation(
                    __instance,
                    overseer,
                    "DataRegistry.PrepareForExternalConsciousnessLoss.Enter",
                    "GameComponent_DataProcessingAllocationRegistry.PrepareForExternalConsciousnessLoss"
                    + " OFFSET_LOSS=" + consciousnessOffsetLoss.ToString("F4"),
                    ref __state);
            }

            private static void Postfix(
                GameComponent_DataProcessingAllocationRegistry __instance,
                Pawn? overseer,
                float consciousnessOffsetLoss,
                LoadDeathPawnState? __state)
            {
                CompleteRegistryPawnOperation(
                    __instance,
                    overseer,
                    "DataRegistry.PrepareForExternalConsciousnessLoss.Exit",
                    "GameComponent_DataProcessingAllocationRegistry.PrepareForExternalConsciousnessLoss"
                    + " OFFSET_LOSS=" + consciousnessOffsetLoss.ToString("F4"),
                    __state);
            }
        }

        private static void CaptureRegistryPawnOperation(
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn? pawn,
            string eventName,
            string methodName,
            ref LoadDeathPawnState? state)
        {
            state = null;
            try
            {
                if (!LoadDeathDiagnosticUtility.Active
                    || !LoadDeathDiagnosticUtility.ShouldTracePawn(pawn))
                {
                    return;
                }

                state = LoadDeathDiagnosticUtility.CaptureState(
                    pawn,
                    methodName,
                    null,
                    true);
                LoadDeathDiagnosticUtility.WriteWithStack(
                    eventName,
                    pawn,
                    "RAW_BEFORE={" + LoadDeathDiagnosticUtility.BuildRawRegistrySnapshot(registry, pawn) + "}"
                    + " PAWN_BEFORE={" + state.passivePawnBefore + "}"
                    + " HEDIFFS_BEFORE={" + state.hediffsBefore + "}",
                    state.stackTrace);
            }
            catch
            {
                // 忽略。
            }
        }

        private static void CompleteRegistryPawnOperation(
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn? pawn,
            string eventName,
            string methodName,
            LoadDeathPawnState? state)
        {
            try
            {
                if (!LoadDeathDiagnosticUtility.Active || state == null)
                {
                    return;
                }

                LoadDeathDiagnosticUtility.Write(
                    eventName,
                    pawn,
                    "DEAD_BEFORE=" + state.deadBefore
                    + " DEAD_AFTER=" + LoadDeathDiagnosticUtility.SafeDead(pawn)
                    + " RAW_BEFORE={" + state.dataProcessingBefore + "}"
                    + " RAW_AFTER={" + LoadDeathDiagnosticUtility.BuildRawRegistrySnapshot(registry, pawn) + "}"
                    + " PAWN_AFTER={" + LoadDeathDiagnosticUtility.BuildPassivePawnSnapshot(pawn) + "}"
                    + " HEDIFFS_AFTER={" + LoadDeathDiagnosticUtility.BuildPassiveHediffList(pawn) + "}",
                    false);
                LoadDeathDiagnosticUtility.ReportFirstDeadTransition(
                    state,
                    pawn,
                    methodName,
                    null);
            }
            catch
            {
                // 忽略。
            }
        }
    }
}
