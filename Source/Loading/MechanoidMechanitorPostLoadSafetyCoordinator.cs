using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 统一协调旧存档加载期间所有会触发健康重算的系统。
    ///
    /// 加载阶段先让数据处理注册表移除负面 DataStreamDistribution；
    /// 第一安全游戏刻恢复机械师身份、机械意识与并行思维阵列等正面来源；
    /// 数据处理完成安全预算校正后，最后才同步工作模式与清理孤立状态。
    /// </summary>
    internal static class MechanoidMechanitorPostLoadSafetyCoordinator
    {
        private const int RetryIntervalTicks = 60;
        private const int ReflectionFailureLogKeyBase = 0x4D41504F; // "MAPO"

        private static readonly Dictionary<Pawn, MechWorkModeDef?> PendingWorkModes =
            new Dictionary<Pawn, MechWorkModeDef?>(ReferencePawnComparer.Instance);

        private static readonly HashSet<Pawn> PendingDynamicConsciousnessRefresh =
            new HashSet<Pawn>(ReferencePawnComparer.Instance);

        private static readonly HashSet<Pawn> PendingDataTargets =
            new HashSet<Pawn>(ReferencePawnComparer.Instance);

        private static readonly HashSet<Pawn> PendingDataOverseers =
            new HashSet<Pawn>(ReferencePawnComparer.Instance);

        private static readonly MethodInfo? RestoreAcquiredRecordsMethod =
            AccessTools.Method(
                typeof(GameComponent_MechanoidMechanitorRegistry),
                "RestoreAcquiredRecordsAfterLoad");

        private static readonly MethodInfo? SynchronizeAcquiredHediffsMethod =
            AccessTools.Method(
                typeof(GameComponent_MechanoidMechanitorRegistry),
                "SynchronizeAcquiredMechanitorHediffs");

        private static readonly MethodInfo? SynchronizeNativeHediffsMethod =
            AccessTools.Method(
                typeof(GameComponent_MechanoidMechanitorRegistry),
                "SynchronizeNativeMechanitorHediffs");

        private static readonly MethodInfo? SynchronizeMechanicalConsciousnessMethod =
            AccessTools.Method(
                typeof(GameComponent_MechanoidMechanitorRegistry),
                "SynchronizeMechanicalConsciousnessHediff");

        private static readonly MethodInfo? SynchronizeSelfWorkModesMethod =
            AccessTools.Method(
                typeof(GameComponent_MechanoidMechanitorRegistry),
                "SynchronizeSelfWorkModeEffectsAfterLoad");

        private static readonly MethodInfo? FinalizeHostAssignmentMethod =
            AccessTools.Method(
                typeof(GameComponent_MechanoidMechanitorRegistry),
                "FinalizeHostAssignment",
                new[] { typeof(Pawn) });

        private static readonly FieldInfo? DataReconciliationPendingField =
            AccessTools.Field(
                typeof(GameComponent_DataProcessingAllocationRegistry),
                "pendingPostLoadDynamicReconciliation");

        private static readonly MethodInfo? ScrubOrphanCommandFocusMethod =
            AccessTools.Method(
                typeof(GameComponent_DataProcessingAllocationRegistry),
                "ScrubResidualOrphanCommandFocusHediffs");

        private static Game? activeGame;
        private static bool loadInProgress;
        private static bool loadedGamePassActive;
        private static bool loadedGamePassCompleted;
        private static bool positiveSourcesReady;
        private static bool dataProcessingReconciled;
        private static bool deferredEffectsApplied;
        private static bool invokingPositiveRestore;
        private static bool invokingDeferredEffects;
        private static int earliestCoordinatorTick;

        internal static bool LoadInProgress => loadInProgress;

        internal static bool ShouldDeferPositiveRestore =>
            loadInProgress
            && !positiveSourcesReady
            && !invokingPositiveRestore;

        internal static bool ShouldDeferDeferredEffects =>
            loadInProgress
            && !deferredEffectsApplied
            && !invokingDeferredEffects;

        internal static bool ShouldDeferDataSynchronization =>
            loadInProgress
            && !positiveSourcesReady
            && !invokingPositiveRestore;

        internal static void BeginLoad(Game? game)
        {
            Reset(game);
            activeGame = game;
            loadInProgress = true;
            MechanoidMechanitorLoadDeathGuard.BeginLoad(game);
        }

        internal static void BeginLoadedGamePass()
        {
            EnsureCurrentGame();
            if (!loadInProgress)
            {
                BeginLoad(Current.Game);
            }

            loadedGamePassActive = true;
        }

        internal static void CompleteLoadedGamePass()
        {
            loadedGamePassActive = false;
            loadedGamePassCompleted = true;

            TickManager? tickManager = Current.Game?.tickManager;
            earliestCoordinatorTick = (tickManager?.TicksGame ?? 0) + 1;
        }

        internal static void AbortLoad()
        {
            Reset(Current.Game);
            MechanoidMechanitorLoadDeathGuard.AbortLoad();
        }

        internal static void QueueWorkMode(Pawn? pawn, MechWorkModeDef? workMode)
        {
            if (pawn == null || pawn.Discarded)
            {
                return;
            }

            PendingWorkModes[pawn] = workMode;
        }

        internal static void QueueDynamicConsciousnessRefresh(Pawn? pawn)
        {
            if (pawn == null || pawn.Discarded)
            {
                return;
            }

            PendingDynamicConsciousnessRefresh.Add(pawn);
        }

        internal static void QueueDataTarget(Pawn? pawn)
        {
            if (pawn == null || pawn.Discarded)
            {
                return;
            }

            PendingDataTargets.Add(pawn);
        }

        internal static void QueueDataOverseer(Pawn? pawn)
        {
            if (pawn == null || pawn.Discarded)
            {
                return;
            }

            PendingDataOverseers.Add(pawn);
        }

        internal static void TickPrefix()
        {
            if (!loadInProgress || deferredEffectsApplied)
            {
                return;
            }

            EnsureCurrentGame();
            if (activeGame == null
                || !ReferenceEquals(activeGame, Current.Game)
                || !loadedGamePassCompleted
                || loadedGamePassActive
                || Current.ProgramState != ProgramState.Playing
                || LongEventHandler.AnyEventNowOrWaiting)
            {
                return;
            }

            TickManager? tickManager = Current.Game?.tickManager;
            if (tickManager == null || tickManager.TicksGame < earliestCoordinatorTick)
            {
                return;
            }

            if (!positiveSourcesReady)
            {
                if (TryRestorePositiveSources())
                {
                    positiveSourcesReady = true;
                }
                else
                {
                    earliestCoordinatorTick = tickManager.TicksGame + RetryIntervalTicks;
                }
            }
        }

        internal static void TickPostfix()
        {
            if (!loadInProgress || !positiveSourcesReady || deferredEffectsApplied)
            {
                return;
            }

            GameComponent_DataProcessingAllocationRegistry? registry =
                GameComponent_DataProcessingAllocationRegistry.CurrentRegistry;

            if (!dataProcessingReconciled)
            {
                if (!TryReadDataReconciliationPending(registry, out bool pending))
                {
                    return;
                }

                if (pending)
                {
                    return;
                }

                dataProcessingReconciled = true;
            }

            if (!TryApplyDeferredEffects(registry))
            {
                TickManager? tickManager = Current.Game?.tickManager;
                earliestCoordinatorTick =
                    (tickManager?.TicksGame ?? earliestCoordinatorTick) + RetryIntervalTicks;
                return;
            }

            deferredEffectsApplied = true;
            MechanoidMechanitorLoadDeathGuard.EndLoad();
            loadInProgress = false;
            ClearQueues();
        }

        private static bool TryRestorePositiveSources()
        {
            GameComponent_MechanoidMechanitorRegistry? registry =
                Current.Game?.GetComponent<GameComponent_MechanoidMechanitorRegistry>();

            if (registry == null)
            {
                return false;
            }

            invokingPositiveRestore = true;
            try
            {
                if (!InvokeVoid(RestoreAcquiredRecordsMethod, registry, "恢复后天机械师记录")
                    || !InvokeVoid(SynchronizeAcquiredHediffsMethod, registry, "同步后天机械师身份")
                    || !InvokeVoid(SynchronizeNativeHediffsMethod, registry, "同步先天机械师身份"))
                {
                    return false;
                }

                Pawn? host =
                    GameComponent_MechanoidMechanitorRegistry.CurrentMechanicalConsciousnessHost;
                if (host != null
                    && !host.Dead
                    && !host.Destroyed
                    && !host.Discarded
                    && host.health?.isBeingKilled != true
                    && MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(host)
                    && !InvokeVoid(
                        FinalizeHostAssignmentMethod,
                        null,
                        "完成机械意识宿主身份",
                        host))
                {
                    return false;
                }

                if (!InvokeVoid(
                    SynchronizeMechanicalConsciousnessMethod,
                    registry,
                    "同步机械意识健康状态"))
                {
                    return false;
                }

                IReadOnlyList<MechanoidMechanitorRegistrySnapshotEntry> mechanitors =
                    GameComponent_MechanoidMechanitorRegistry.GetPersistentRecordSnapshot();
                for (int i = 0; i < mechanitors.Count; i++)
                {
                    Pawn? pawn = mechanitors[i].Pawn;
                    if (pawn != null
                        && !pawn.Dead
                        && !pawn.Destroyed
                        && !pawn.Discarded
                        && pawn.health?.isBeingKilled != true)
                    {
                        // 读档安全协调阶段统一为所有已注册机械师补齐 timetable，
                        // 覆盖非宿主先天机械师等不会走 EnsureRoleState 的路径，
                        // 不建立每 Tick 轮询，仅在读档时一次性执行。
                        MechanoidMechanitorRoleUtility.EnsureTimetableState(pawn);
                        PendingDynamicConsciousnessRefresh.Add(pawn);
                    }
                }

                List<Pawn> refreshSnapshot =
                    new List<Pawn>(PendingDynamicConsciousnessRefresh);
                for (int i = 0; i < refreshSnapshot.Count; i++)
                {
                    Pawn? pawn = refreshSnapshot[i];
                    if (pawn == null)
                    {
                        continue;
                    }

                    if (pawn.Dead
                        || pawn.Destroyed
                        || pawn.Discarded
                        || pawn.health?.isBeingKilled == true)
                    {
                        PendingDynamicConsciousnessRefresh.Remove(pawn);
                        continue;
                    }

                    DynamicConsciousnessBonusUtility.RefreshForPawn(pawn);
                    PendingDynamicConsciousnessRefresh.Remove(pawn);
                }

                return PendingDynamicConsciousnessRefresh.Count == 0;
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 读档安全协调恢复正面意识来源失败：" + ex);
                return false;
            }
            finally
            {
                invokingPositiveRestore = false;
            }
        }

        private static bool TryApplyDeferredEffects(
            GameComponent_DataProcessingAllocationRegistry? dataRegistry)
        {
            GameComponent_MechanoidMechanitorRegistry? mechanitorRegistry =
                Current.Game?.GetComponent<GameComponent_MechanoidMechanitorRegistry>();
            if (mechanitorRegistry == null)
            {
                return false;
            }

            invokingDeferredEffects = true;
            try
            {
                if (!InvokeVoid(
                    SynchronizeSelfWorkModesMethod,
                    mechanitorRegistry,
                    "同步机械族机械师自身工作模式"))
                {
                    return false;
                }

                if (dataRegistry != null)
                {
                    List<Pawn> overseers = new List<Pawn>(PendingDataOverseers);
                    for (int i = 0; i < overseers.Count; i++)
                    {
                        Pawn? pawn = overseers[i];
                        if (pawn == null)
                        {
                            continue;
                        }

                        if (pawn.Dead || pawn.Destroyed || pawn.Discarded)
                        {
                            PendingDataOverseers.Remove(pawn);
                            continue;
                        }

                        dataRegistry.SyncHediffsForOverseer(pawn);
                        PendingDataOverseers.Remove(pawn);
                    }

                    List<Pawn> targets = new List<Pawn>(PendingDataTargets);
                    for (int i = 0; i < targets.Count; i++)
                    {
                        Pawn? pawn = targets[i];
                        if (pawn == null)
                        {
                            continue;
                        }

                        if (pawn.Dead || pawn.Destroyed || pawn.Discarded)
                        {
                            PendingDataTargets.Remove(pawn);
                            continue;
                        }

                        dataRegistry.SyncHediffForTarget(pawn);
                        PendingDataTargets.Remove(pawn);
                    }
                }

                List<KeyValuePair<Pawn, MechWorkModeDef?>> workModes =
                    new List<KeyValuePair<Pawn, MechWorkModeDef?>>(PendingWorkModes);
                for (int i = 0; i < workModes.Count; i++)
                {
                    Pawn? pawn = workModes[i].Key;
                    if (pawn == null)
                    {
                        continue;
                    }

                    if (pawn.Dead || pawn.Destroyed || pawn.Discarded)
                    {
                        PendingWorkModes.Remove(pawn);
                        continue;
                    }

                    MechanoidMechanitorWorkModeUtility.ApplyWorkModeHediff(
                        pawn,
                        workModes[i].Value!);
                    PendingWorkModes.Remove(pawn);
                }

                bool scrubSucceeded = true;
                if (dataRegistry != null
                    && !InvokeBool(
                        ScrubOrphanCommandFocusMethod,
                        dataRegistry,
                        "清理孤立指令聚焦健康状态",
                        out scrubSucceeded))
                {
                    return false;
                }

                if (dataRegistry != null && !scrubSucceeded)
                {
                    return false;
                }

                return PendingDataOverseers.Count == 0
                    && PendingDataTargets.Count == 0
                    && PendingWorkModes.Count == 0;
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 读档安全协调应用延后效果失败：" + ex);
                return false;
            }
            finally
            {
                invokingDeferredEffects = false;
            }
        }

        private static bool TryReadDataReconciliationPending(
            GameComponent_DataProcessingAllocationRegistry? registry,
            out bool pending)
        {
            pending = false;
            if (registry == null)
            {
                return true;
            }

            try
            {
                if (DataReconciliationPendingField == null)
                {
                    Log.ErrorOnce(
                        "[MAP-机械族机械师] 无法读取读档数据处理校正状态，" +
                        "已保持安全屏障并等待后续重试。",
                        ReflectionFailureLogKeyBase + 1);
                    return false;
                }

                pending = DataReconciliationPendingField.GetValue(registry) is bool value
                    && value;
                return true;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce(
                    "[MAP-机械族机械师] 读取读档数据处理校正状态失败，" +
                    "已保持安全屏障：" + ex,
                    ReflectionFailureLogKeyBase + 2);
                return false;
            }
        }

        private static bool InvokeVoid(
            MethodInfo? method,
            object? instance,
            string phase,
            params object?[] args)
        {
            if (method == null)
            {
                Log.ErrorOnce(
                    "[MAP-机械族机械师] 读档安全协调缺少方法：" + phase,
                    ReflectionFailureLogKeyBase + phase.GetHashCode());
                return false;
            }

            try
            {
                method.Invoke(instance, args);
                return true;
            }
            catch (TargetInvocationException ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 读档安全协调阶段失败：" +
                    phase + "：" + (ex.InnerException ?? ex));
                return false;
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 读档安全协调阶段失败：" +
                    phase + "：" + ex);
                return false;
            }
        }

        private static bool InvokeBool(
            MethodInfo? method,
            object? instance,
            string phase,
            out bool result,
            params object?[] args)
        {
            result = false;
            if (!InvokeRaw(method, instance, phase, out object? raw, args))
            {
                return false;
            }

            result = raw is bool value && value;
            return true;
        }

        private static bool InvokeRaw(
            MethodInfo? method,
            object? instance,
            string phase,
            out object? result,
            params object?[] args)
        {
            result = null;
            if (method == null)
            {
                Log.ErrorOnce(
                    "[MAP-机械族机械师] 读档安全协调缺少方法：" + phase,
                    ReflectionFailureLogKeyBase + phase.GetHashCode());
                return false;
            }

            try
            {
                result = method.Invoke(instance, args);
                return true;
            }
            catch (TargetInvocationException ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 读档安全协调阶段失败：" +
                    phase + "：" + (ex.InnerException ?? ex));
                return false;
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 读档安全协调阶段失败：" +
                    phase + "：" + ex);
                return false;
            }
        }

        private static void EnsureCurrentGame()
        {
            Game? current = Current.Game;
            if (current == null)
            {
                return;
            }

            if (activeGame != null && !ReferenceEquals(activeGame, current))
            {
                Reset(current);
            }

            activeGame ??= current;
        }

        private static void Reset(Game? game)
        {
            activeGame = game;
            loadInProgress = false;
            loadedGamePassActive = false;
            loadedGamePassCompleted = false;
            positiveSourcesReady = false;
            dataProcessingReconciled = false;
            deferredEffectsApplied = false;
            invokingPositiveRestore = false;
            invokingDeferredEffects = false;
            earliestCoordinatorTick = 0;
            ClearQueues();
            MechanoidMechanitorLoadDeathGuard.Reset(game);
        }

        private static void ClearQueues()
        {
            PendingWorkModes.Clear();
            PendingDynamicConsciousnessRefresh.Clear();
            PendingDataTargets.Clear();
            PendingDataOverseers.Clear();
        }

        private sealed class ReferencePawnComparer : IEqualityComparer<Pawn>
        {
            internal static readonly ReferencePawnComparer Instance =
                new ReferencePawnComparer();

            public bool Equals(Pawn? x, Pawn? y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(Pawn obj)
            {
                return obj == null ? 0 : obj.thingIDNumber;
            }
        }
    }

    [HarmonyPatch(typeof(Game), nameof(Game.LoadGame))]
    internal static class MechanoidMechanitorPostLoadGamePatch
    {
        [HarmonyPrefix]
        private static void Prefix(Game __instance)
        {
            MechanoidMechanitorPostLoadSafetyCoordinator.BeginLoad(__instance);
        }

        [HarmonyFinalizer]
        private static Exception? Finalizer(Exception? __exception)
        {
            if (__exception != null)
            {
                MechanoidMechanitorPostLoadSafetyCoordinator.AbortLoad();
            }

            return __exception;
        }
    }

    [HarmonyPatch(typeof(GameComponentUtility), nameof(GameComponentUtility.LoadedGame))]
    internal static class MechanoidMechanitorLoadedGamePassPatch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            MechanoidMechanitorPostLoadSafetyCoordinator.BeginLoadedGamePass();
        }

        [HarmonyPostfix]
        private static void Postfix()
        {
            MechanoidMechanitorPostLoadSafetyCoordinator.CompleteLoadedGamePass();
        }
    }

    [HarmonyPatch(typeof(GameComponentUtility), nameof(GameComponentUtility.StartedNewGame))]
    internal static class MechanoidMechanitorStartedNewGameResetPatch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            MechanoidMechanitorPostLoadSafetyCoordinator.AbortLoad();
        }
    }

    [HarmonyPatch(typeof(GameComponentUtility), nameof(GameComponentUtility.GameComponentTick))]
    internal static class MechanoidMechanitorPostLoadTickPatch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            MechanoidMechanitorPostLoadSafetyCoordinator.TickPrefix();
        }

        [HarmonyPostfix]
        private static void Postfix()
        {
            MechanoidMechanitorPostLoadSafetyCoordinator.TickPostfix();
        }
    }

    [HarmonyPatch(
        typeof(GameComponent_MechanoidMechanitorRegistry),
        "RestoreAcquiredRecordsAfterLoad")]
    internal static class DeferRestoreAcquiredRecordsPatch
    {
        [HarmonyPrefix]
        private static bool Prefix()
        {
            return !MechanoidMechanitorPostLoadSafetyCoordinator.ShouldDeferPositiveRestore;
        }
    }

    [HarmonyPatch(
        typeof(GameComponent_MechanoidMechanitorRegistry),
        "SynchronizeAcquiredMechanitorHediffs")]
    internal static class DeferSynchronizeAcquiredHediffsPatch
    {
        [HarmonyPrefix]
        private static bool Prefix()
        {
            return !MechanoidMechanitorPostLoadSafetyCoordinator.ShouldDeferPositiveRestore;
        }
    }

    [HarmonyPatch(
        typeof(GameComponent_MechanoidMechanitorRegistry),
        "SynchronizeNativeMechanitorHediffs")]
    internal static class DeferSynchronizeNativeHediffsPatch
    {
        [HarmonyPrefix]
        private static bool Prefix()
        {
            return !MechanoidMechanitorPostLoadSafetyCoordinator.ShouldDeferPositiveRestore;
        }
    }

    [HarmonyPatch(
        typeof(GameComponent_MechanoidMechanitorRegistry),
        "SynchronizeMechanicalConsciousnessHediff")]
    internal static class DeferSynchronizeMechanicalConsciousnessPatch
    {
        [HarmonyPrefix]
        private static bool Prefix()
        {
            return !MechanoidMechanitorPostLoadSafetyCoordinator.ShouldDeferPositiveRestore;
        }
    }

    [HarmonyPatch(
        typeof(GameComponent_MechanoidMechanitorRegistry),
        "SynchronizeSelfWorkModeEffectsAfterLoad")]
    internal static class DeferSynchronizeSelfWorkModesPatch
    {
        [HarmonyPrefix]
        private static bool Prefix()
        {
            return !MechanoidMechanitorPostLoadSafetyCoordinator.ShouldDeferDeferredEffects;
        }
    }

    [HarmonyPatch(
        typeof(GameComponent_MechanoidMechanitorRegistry),
        "FinalizeHostAssignment")]
    internal static class DeferFinalizeHostAssignmentPatch
    {
        [HarmonyPrefix]
        private static bool Prefix()
        {
            return !MechanoidMechanitorPostLoadSafetyCoordinator.ShouldDeferPositiveRestore;
        }
    }

    [HarmonyPatch(
        typeof(MechanoidMechanitorWorkModeUtility),
        nameof(MechanoidMechanitorWorkModeUtility.ApplyWorkModeHediff))]
    internal static class DeferMechanitorWorkModeHediffPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Pawn pawn, MechWorkModeDef workMode)
        {
            if (!MechanoidMechanitorPostLoadSafetyCoordinator.ShouldDeferDeferredEffects)
            {
                return true;
            }

            MechanoidMechanitorPostLoadSafetyCoordinator.QueueWorkMode(pawn, workMode);
            return false;
        }
    }

    [HarmonyPatch(
        typeof(ParallelThoughtArrayUtility),
        nameof(ParallelThoughtArrayUtility.RefreshTargetDynamicConsciousness))]
    internal static class DeferParallelArrayDynamicRefreshPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Pawn? target)
        {
            if (!MechanoidMechanitorPostLoadSafetyCoordinator.ShouldDeferPositiveRestore)
            {
                return true;
            }

            MechanoidMechanitorPostLoadSafetyCoordinator
                .QueueDynamicConsciousnessRefresh(target);
            return false;
        }
    }

    [HarmonyPatch(
        typeof(DynamicConsciousnessBonusUtility),
        nameof(DynamicConsciousnessBonusUtility.RefreshForPawn))]
    internal static class DeferDirectDynamicConsciousnessRefreshPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Pawn? pawn)
        {
            if (!MechanoidMechanitorPostLoadSafetyCoordinator.ShouldDeferPositiveRestore)
            {
                return true;
            }

            MechanoidMechanitorPostLoadSafetyCoordinator
                .QueueDynamicConsciousnessRefresh(pawn);
            return false;
        }
    }

    [HarmonyPatch(
        typeof(GameComponent_DataProcessingAllocationRegistry),
        "TryRunPostLoadDynamicReconciliation")]
    internal static class GateDataProcessingPostLoadReconciliationPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(ref bool __result)
        {
            if (!MechanoidMechanitorPostLoadSafetyCoordinator.LoadInProgress
                || !MechanoidMechanitorPostLoadSafetyCoordinator.ShouldDeferPositiveRestore)
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    [HarmonyPatch(
        typeof(GameComponent_DataProcessingAllocationRegistry),
        nameof(GameComponent_DataProcessingAllocationRegistry.SyncHediffForTarget))]
    internal static class DeferDataProcessingTargetSyncPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Pawn? target)
        {
            if (!MechanoidMechanitorPostLoadSafetyCoordinator.ShouldDeferDataSynchronization)
            {
                return true;
            }

            MechanoidMechanitorPostLoadSafetyCoordinator.QueueDataTarget(target);
            return false;
        }
    }

    [HarmonyPatch(
        typeof(GameComponent_DataProcessingAllocationRegistry),
        nameof(GameComponent_DataProcessingAllocationRegistry.SyncHediffsForOverseer))]
    internal static class DeferDataProcessingOverseerSyncPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Pawn? overseer)
        {
            if (!MechanoidMechanitorPostLoadSafetyCoordinator.ShouldDeferDataSynchronization)
            {
                return true;
            }

            MechanoidMechanitorPostLoadSafetyCoordinator.QueueDataOverseer(overseer);
            return false;
        }
    }

    [HarmonyPatch(
        typeof(GameComponent_DataProcessingAllocationRegistry),
        "ScrubResidualOrphanCommandFocusHediffs")]
    internal static class DeferOrphanCommandFocusScrubPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(ref bool __result)
        {
            if (!MechanoidMechanitorPostLoadSafetyCoordinator.ShouldDeferDeferredEffects)
            {
                return true;
            }

            __result = true;
            return false;
        }
    }
}
