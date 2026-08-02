using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// Pawn 死亡完成后暂停数据处理运行态，但不删除玩家保存的动态分配配置。
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    internal static class DataProcessingPawnKilledLifecyclePatch
    {
        private static void Postfix(Pawn __instance)
        {
            if (__instance?.Dead == true)
            {
                DataProcessingPawnLifecycleCoordinator.NotifyPawnDied(__instance);
            }
        }
    }

    /// <summary>
    /// 原版 1.6 的 TryResurrect 返回 true 时，Pawn 的组件、健康和生成状态均已恢复。
    /// 此处只排队，实际恢复延迟到下一游戏刻执行。
    /// </summary>
    [HarmonyPatch(typeof(ResurrectionUtility), nameof(ResurrectionUtility.TryResurrect))]
    internal static class DataProcessingPawnResurrectedLifecyclePatch
    {
        private static void Postfix(Pawn pawn, ref bool __result)
        {
            if (__result && pawn != null && !pawn.Dead)
            {
                DataProcessingPawnLifecycleCoordinator.EnqueueReactivation(pawn);
            }
        }
    }

    /// <summary>
    /// 监管者处于死亡流程时，原 ClearOverseer 会连同全局与单体动态配置一起删除。
    /// 死亡场景改为只释放运行态；活体上的明确清除仍执行原逻辑。
    /// </summary>
    [HarmonyPatch(
        typeof(GameComponent_DataProcessingAllocationRegistry),
        nameof(GameComponent_DataProcessingAllocationRegistry.ClearOverseer))]
    internal static class DataProcessingDeadOverseerClearPatch
    {
        private static bool Prefix(
            GameComponent_DataProcessingAllocationRegistry __instance,
            Pawn? overseer)
        {
            bool dying = overseer?.health?.isBeingKilled == true;
            if (overseer == null
                || ((!overseer.Dead && !dying) || overseer.Discarded))
            {
                return true;
            }

            DataProcessingPawnLifecycleCoordinator.SuspendOverseerRuntime(
                __instance,
                overseer);
            return false;
        }
    }

    /// <summary>
    /// 替换单体配置清理：死亡、暂时无监管者或派系过渡均视为休眠；
    /// 只有引用为空、Pawn 已被 Discard，或重复配置才永久删除。
    /// </summary>
    [HarmonyPatch(
        typeof(GameComponent_DataProcessingAllocationRegistry),
        "CleanupInvalidDynamicTargetRecords")]
    internal static class DataProcessingDormantTargetCleanupPatch
    {
        private static bool Prefix(
            GameComponent_DataProcessingAllocationRegistry __instance,
            object[] __args)
        {
            HashSet<Pawn>? affectedOverseers =
                __args != null && __args.Length > 0
                    ? __args[0] as HashSet<Pawn>
                    : null;

            DataProcessingPawnLifecycleCoordinator.CleanupDynamicTargetConfigurations(
                __instance,
                affectedOverseers);
            return false;
        }
    }

    /// <summary>
    /// 替换监管者级配置清理：死亡机械师保留全局动态开关与最低处理阈值。
    /// </summary>
    [HarmonyPatch(
        typeof(GameComponent_DataProcessingAllocationRegistry),
        "CleanupInvalidDynamicAllocationRecords")]
    internal static class DataProcessingDormantOverseerCleanupPatch
    {
        private static bool Prefix(
            GameComponent_DataProcessingAllocationRegistry __instance)
        {
            DataProcessingPawnLifecycleCoordinator.CleanupDynamicOverseerConfigurations(
                __instance);
            return false;
        }
    }

    /// <summary>
    /// 死亡或正在死亡的监管者不能进入预算计划。
    /// </summary>
    [HarmonyPatch(
        typeof(GameComponent_DataProcessingAllocationRegistry),
        "RunDynamicPlanForOverseer")]
    internal static class DataProcessingDormantOverseerPlanGuardPatch
    {
        private static bool Prefix(object[] __args)
        {
            Pawn? overseer =
                __args != null && __args.Length > 0
                    ? __args[0] as Pawn
                    : null;

            return overseer != null
                && !overseer.Dead
                && !overseer.Destroyed
                && !overseer.Discarded
                && overseer.health?.isBeingKilled != true;
        }
    }

    /// <summary>
    /// 处理复活即时恢复，并每 600 tick 扫描一次缺少运行时调度的休眠配置，
    /// 兼容绕过原版复活入口的其他 MOD。
    /// </summary>
    [HarmonyPatch(
        typeof(GameComponent_DataProcessingAllocationRegistry),
        nameof(GameComponent_DataProcessingAllocationRegistry.GameComponentTick))]
    internal static class DataProcessingPawnLifecycleTickPatch
    {
        private static void Postfix(
            GameComponent_DataProcessingAllocationRegistry __instance)
        {
            DataProcessingPawnLifecycleCoordinator.Process(__instance);
        }
    }

    internal static class DataProcessingPawnLifecycleCoordinator
    {
        private const int DormantScanIntervalTicks = 600;
        private const int ReflectionFailureLogKey = 0x4D41504C; // "MAPL"

        private static readonly FieldInfo? AllocationRecordsField =
            AccessTools.Field(
                typeof(GameComponent_DataProcessingAllocationRegistry),
                "records");

        private static readonly FieldInfo? DynamicTargetRecordsField =
            AccessTools.Field(
                typeof(GameComponent_DataProcessingAllocationRegistry),
                "dynamicTargetRecords");

        private static readonly FieldInfo? DynamicAllocationRecordsField =
            AccessTools.Field(
                typeof(GameComponent_DataProcessingAllocationRegistry),
                "dynamicAllocationRecords");

        private static readonly FieldInfo? NextDynamicCheckTickField =
            AccessTools.Field(
                typeof(GameComponent_DataProcessingAllocationRegistry),
                "nextDynamicCheckTickByTarget");

        private static readonly FieldInfo? CachedDynamicEvaluationField =
            AccessTools.Field(
                typeof(GameComponent_DataProcessingAllocationRegistry),
                "cachedDynamicEvaluationByTarget");

        private static readonly MethodInfo? RebuildDynamicTargetCachesMethod =
            AccessTools.Method(
                typeof(GameComponent_DataProcessingAllocationRegistry),
                "RebuildDynamicTargetCaches");

        private static readonly MethodInfo? RebuildDynamicAllocationCachesMethod =
            AccessTools.Method(
                typeof(GameComponent_DataProcessingAllocationRegistry),
                "RebuildDynamicAllocationCaches");

        private static readonly MethodInfo? TickDynamicSchedulerMethod =
            AccessTools.Method(
                typeof(GameComponent_DataProcessingAllocationRegistry),
                "TickDynamicScheduler");

        private static readonly MethodInfo? RunDynamicPlanForOverseerMethod =
            AccessTools.Method(
                typeof(GameComponent_DataProcessingAllocationRegistry),
                "RunDynamicPlanForOverseer");

        private static readonly Dictionary<Pawn, int> PendingReactivation =
            new Dictionary<Pawn, int>(ReferencePawnComparer.Instance);

        private static Game? queuedGame;
        private static int lastDormantScanTick = -DormantScanIntervalTicks;

        public static void NotifyPawnDied(Pawn? pawn)
        {
            GameComponent_DataProcessingAllocationRegistry? registry =
                GameComponent_DataProcessingAllocationRegistry.CurrentRegistry;
            if (registry == null || pawn == null)
            {
                return;
            }

            SuspendOverseerRuntime(registry, pawn);
            SuspendTargetRuntime(registry, pawn);
            PendingReactivation.Remove(pawn);
        }

        public static void EnqueueReactivation(Pawn? pawn)
        {
            if (pawn == null
                || pawn.Dead
                || pawn.Discarded
                || Current.Game == null
                || Find.TickManager == null)
            {
                return;
            }

            EnsureCurrentGame();
            PendingReactivation[pawn] = Find.TickManager.TicksGame + 1;
        }

        public static void Process(
            GameComponent_DataProcessingAllocationRegistry? registry)
        {
            if (registry == null
                || Current.Game == null
                || Find.TickManager == null)
            {
                ClearRuntimeQueue();
                return;
            }

            EnsureCurrentGame();
            int now = Find.TickManager.TicksGame;
            bool periodicScanDue =
                now - lastDormantScanTick >= DormantScanIntervalTicks;

            List<Pawn> duePawns = new List<Pawn>();
            foreach (KeyValuePair<Pawn, int> pair in PendingReactivation)
            {
                if (pair.Value <= now)
                {
                    duePawns.Add(pair.Key);
                }
            }

            if (duePawns.Count == 0 && !periodicScanDue)
            {
                return;
            }

            for (int i = 0; i < duePawns.Count; i++)
            {
                PendingReactivation.Remove(duePawns[i]);
            }

            if (periodicScanDue)
            {
                lastDormantScanTick = now;
            }

            ReconcileDormantConfigurations(
                registry,
                now,
                duePawns,
                periodicScanDue);
        }

        public static void CleanupDynamicTargetConfigurations(
            GameComponent_DataProcessingAllocationRegistry registry,
            HashSet<Pawn>? affectedOverseers)
        {
            List<DataProcessingDynamicTargetRecord>? records =
                GetDynamicTargetRecords(registry);
            if (records == null)
            {
                LogReflectionFailure(
                    "无法访问单体动态配置列表；已跳过原清理以避免死亡配置丢失。",
                    0);
                return;
            }

            HashSet<Pawn> retainedTargets =
                new HashSet<Pawn>(ReferencePawnComparer.Instance);

            for (int i = records.Count - 1; i >= 0; i--)
            {
                DataProcessingDynamicTargetRecord? record = records[i];
                Pawn? target = record?.target;

                if (record == null || target == null || target.Discarded)
                {
                    if (record?.overseer != null)
                    {
                        affectedOverseers?.Add(record.overseer);
                    }

                    if (target != null)
                    {
                        ClearTargetRuntime(registry, target);
                    }

                    records.RemoveAt(i);
                    continue;
                }

                if (!retainedTargets.Add(target))
                {
                    if (record.overseer != null)
                    {
                        affectedOverseers?.Add(record.overseer);
                    }

                    records.RemoveAt(i);
                    continue;
                }

                record.Normalize();

                if (record.overseer?.Discarded == true)
                {
                    affectedOverseers?.Add(record.overseer);
                    record.overseer = null;
                }

                bool targetDormant =
                    target.Dead
                    || target.Destroyed
                    || target.health?.isBeingKilled == true;
                if (targetDormant)
                {
                    if (HasActualAllocation(registry, target))
                    {
                        registry.ClearTarget(target);
                    }
                    ClearTargetRuntime(registry, target);
                    continue;
                }

                Pawn? currentOverseer = ResolveCurrentOverseer(registry, target);
                if (currentOverseer == null)
                {
                    if (HasActualAllocation(registry, target))
                    {
                        registry.ClearTarget(target);
                    }
                    ClearTargetRuntime(registry, target);
                    continue;
                }

                if (!ReferenceEquals(record.overseer, currentOverseer))
                {
                    Pawn? oldOverseer = record.overseer;
                    if (HasActualAllocation(registry, target))
                    {
                        registry.ClearTarget(target);
                    }
                    record.overseer = currentOverseer;
                    ClearTargetRuntime(registry, target);

                    if (oldOverseer != null)
                    {
                        affectedOverseers?.Add(oldOverseer);
                    }
                    affectedOverseers?.Add(currentOverseer);
                }
            }

            InvokeNoArgs(RebuildDynamicTargetCachesMethod, registry, 1);
        }

        public static void CleanupDynamicOverseerConfigurations(
            GameComponent_DataProcessingAllocationRegistry registry)
        {
            List<DataProcessingDynamicAllocationRecord>? records =
                GetDynamicAllocationRecords(registry);
            if (records == null)
            {
                LogReflectionFailure(
                    "无法访问监管者动态配置列表；已跳过原清理以避免死亡配置丢失。",
                    2);
                return;
            }

            HashSet<Pawn> retainedOverseers =
                new HashSet<Pawn>(ReferencePawnComparer.Instance);

            for (int i = records.Count - 1; i >= 0; i--)
            {
                DataProcessingDynamicAllocationRecord? record = records[i];
                Pawn? overseer = record?.overseer;

                if (record == null
                    || overseer == null
                    || overseer.Discarded
                    || !retainedOverseers.Add(overseer))
                {
                    records.RemoveAt(i);
                    continue;
                }

                bool dormant =
                    overseer.Dead
                    || overseer.Destroyed
                    || overseer.health?.isBeingKilled == true;
                if (dormant)
                {
                    if (HasActualAllocationsForOverseer(registry, overseer))
                    {
                        SuspendOverseerRuntime(registry, overseer);
                    }
                    continue;
                }

                if (!registry.IsDynamicAllocationOverseerValid(overseer))
                {
                    records.RemoveAt(i);
                }
            }

            InvokeNoArgs(RebuildDynamicAllocationCachesMethod, registry, 3);
        }

        public static void SuspendOverseerRuntime(
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn? overseer)
        {
            if (registry == null || overseer == null)
            {
                return;
            }

            List<DataProcessingAllocationRecord>? allocations =
                GetAllocationRecords(registry);
            if (allocations != null)
            {
                List<Pawn> targets = new List<Pawn>();
                for (int i = 0; i < allocations.Count; i++)
                {
                    DataProcessingAllocationRecord? record = allocations[i];
                    if (record?.target != null
                        && ReferenceEquals(record.overseer, overseer))
                    {
                        targets.Add(record.target);
                    }
                }

                for (int i = 0; i < targets.Count; i++)
                {
                    registry.ClearTarget(targets[i]);
                }
            }

            List<DataProcessingDynamicTargetRecord>? configs =
                GetDynamicTargetRecords(registry);
            if (configs != null)
            {
                for (int i = 0; i < configs.Count; i++)
                {
                    DataProcessingDynamicTargetRecord? config = configs[i];
                    if (config?.target != null
                        && ReferenceEquals(config.overseer, overseer))
                    {
                        ClearTargetRuntime(registry, config.target);
                    }
                }
            }

            registry.SyncHediffsForOverseer(overseer);
        }

        private static void SuspendTargetRuntime(
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn target)
        {
            DataProcessingDynamicTargetRecord? config =
                registry.GetDynamicTargetRecord(
                    overseer: null,
                    target: target);
            Pawn? oldOverseer = config?.overseer;

            if (HasActualAllocation(registry, target))
            {
                registry.ClearTarget(target);
            }
            ClearTargetRuntime(registry, target);

            if (oldOverseer != null
                && !oldOverseer.Dead
                && !oldOverseer.Destroyed
                && !oldOverseer.Discarded
                && oldOverseer.health?.isBeingKilled != true)
            {
                InvokeRunPlan(registry, oldOverseer);
            }
        }

        private static void ReconcileDormantConfigurations(
            GameComponent_DataProcessingAllocationRegistry registry,
            int now,
            List<Pawn> duePawns,
            bool scanAll)
        {
            List<DataProcessingDynamicTargetRecord>? targetRecords =
                GetDynamicTargetRecords(registry);
            List<DataProcessingDynamicAllocationRecord>? overseerRecords =
                GetDynamicAllocationRecords(registry);
            Dictionary<Pawn, int>? nextChecks =
                GetNextDynamicChecks(registry);

            if (targetRecords == null
                || overseerRecords == null
                || nextChecks == null)
            {
                LogReflectionFailure(
                    "无法访问动态分配运行时字段，休眠配置恢复已跳过。",
                    4);
                return;
            }

            HashSet<Pawn> requested =
                new HashSet<Pawn>(duePawns, ReferencePawnComparer.Instance);
            HashSet<Pawn> oldOverseersToReplan =
                new HashSet<Pawn>(ReferencePawnComparer.Instance);
            bool cacheNeedsRebuild = false;
            bool anyScheduled = false;

            List<DataProcessingDynamicAllocationRecord> overseerSnapshot =
                new List<DataProcessingDynamicAllocationRecord>(overseerRecords);
            for (int i = 0; i < overseerSnapshot.Count; i++)
            {
                DataProcessingDynamicAllocationRecord? global =
                    overseerSnapshot[i];
                Pawn? overseer = global?.overseer;
                if (overseer == null || overseer.Discarded)
                {
                    continue;
                }

                bool overseerDormant =
                    overseer.Dead
                    || overseer.Destroyed
                    || overseer.health?.isBeingKilled == true;
                if (overseerDormant)
                {
                    if (HasActualAllocationsForOverseer(registry, overseer))
                    {
                        SuspendOverseerRuntime(registry, overseer);
                    }
                    continue;
                }

                bool explicitlyRequested = requested.Contains(overseer);
                if (!global!.enabled
                    || !registry.IsDynamicAllocationOverseerValid(overseer)
                    || (!scanAll && !explicitlyRequested))
                {
                    continue;
                }

                List<Pawn> currentTargets = CollectCurrentTargets(registry, overseer);
                for (int j = 0; j < currentTargets.Count; j++)
                {
                    Pawn target = currentTargets[j];
                    DataProcessingDynamicTargetRecord? config =
                        registry.GetDynamicTargetRecord(
                            overseer: null,
                            target: target);
                    bool created = config == null;
                    if (config == null)
                    {
                        config = registry.GetOrCreateDynamicTargetRecord(
                            overseer,
                            target);
                    }

                    if (!ReferenceEquals(config.overseer, overseer))
                    {
                        Pawn? oldOverseer = config.overseer;
                        if (HasActualAllocation(registry, target))
                        {
                            registry.ClearTarget(target);
                        }
                        config.overseer = overseer;
                        ClearTargetRuntime(registry, target);
                        cacheNeedsRebuild = true;

                        if (oldOverseer != null)
                        {
                            oldOverseersToReplan.Add(oldOverseer);
                        }
                    }

                    bool missingRuntimeSchedule =
                        !nextChecks.ContainsKey(target);
                    if (config.enabled
                        && (explicitlyRequested
                            || created
                            || missingRuntimeSchedule))
                    {
                        nextChecks[target] = now;
                        anyScheduled = true;
                    }
                }
            }

            List<DataProcessingDynamicTargetRecord> targetSnapshot =
                new List<DataProcessingDynamicTargetRecord>(targetRecords);
            for (int i = 0; i < targetSnapshot.Count; i++)
            {
                DataProcessingDynamicTargetRecord? config = targetSnapshot[i];
                Pawn? target = config?.target;
                if (config == null || target == null || target.Discarded)
                {
                    continue;
                }

                bool explicitlyRequested = requested.Contains(target);
                if (!scanAll && !explicitlyRequested)
                {
                    continue;
                }

                bool targetDormant =
                    target.Dead
                    || target.Destroyed
                    || target.health?.isBeingKilled == true;
                if (targetDormant)
                {
                    if (HasActualAllocation(registry, target))
                    {
                        registry.ClearTarget(target);
                    }
                    ClearTargetRuntime(registry, target);
                    continue;
                }

                Pawn? currentOverseer = ResolveCurrentOverseer(registry, target);
                if (currentOverseer == null)
                {
                    if (HasActualAllocation(registry, target))
                    {
                        registry.ClearTarget(target);
                    }
                    ClearTargetRuntime(registry, target);
                    continue;
                }

                if (!ReferenceEquals(config.overseer, currentOverseer))
                {
                    Pawn? oldOverseer = config.overseer;
                    if (HasActualAllocation(registry, target))
                    {
                        registry.ClearTarget(target);
                    }
                    config.overseer = currentOverseer;
                    ClearTargetRuntime(registry, target);
                    cacheNeedsRebuild = true;

                    if (oldOverseer != null)
                    {
                        oldOverseersToReplan.Add(oldOverseer);
                    }
                }

                bool canRun =
                    config.enabled
                    && registry.IsDynamicAllocationEnabled(currentOverseer)
                    && registry.IsDynamicAllocationOverseerValid(currentOverseer)
                    && registry.IsValidAllocationPairForList(
                        currentOverseer,
                        target);
                bool missingRuntimeSchedule =
                    !nextChecks.ContainsKey(target);

                if (canRun
                    && (explicitlyRequested || missingRuntimeSchedule))
                {
                    nextChecks[target] = now;
                    anyScheduled = true;
                }
                else if (!canRun)
                {
                    ClearTargetRuntime(registry, target);
                }
            }

            if (cacheNeedsRebuild)
            {
                InvokeNoArgs(RebuildDynamicTargetCachesMethod, registry, 5);
            }

            if (anyScheduled)
            {
                InvokeNoArgs(TickDynamicSchedulerMethod, registry, 6);
            }

            foreach (Pawn oldOverseer in oldOverseersToReplan)
            {
                if (oldOverseer != null
                    && !oldOverseer.Dead
                    && !oldOverseer.Destroyed
                    && !oldOverseer.Discarded
                    && oldOverseer.health?.isBeingKilled != true
                    && registry.IsDynamicAllocationEnabled(oldOverseer)
                    && registry.IsDynamicAllocationOverseerValid(oldOverseer))
                {
                    InvokeRunPlan(registry, oldOverseer);
                }
            }
        }

        private static List<Pawn> CollectCurrentTargets(
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn overseer)
        {
            List<Pawn> result = new List<Pawn>();
            if (registry.IsValidAllocationPairForList(overseer, overseer))
            {
                result.Add(overseer);
            }

            if (overseer.mechanitor == null)
            {
                return result;
            }

            List<Pawn> overseen = overseer.mechanitor.OverseenPawns;
            for (int i = 0; i < overseen.Count; i++)
            {
                Pawn target = overseen[i];
                if (target != null
                    && !ReferenceEquals(target, overseer)
                    && registry.IsValidAllocationPairForList(overseer, target))
                {
                    result.Add(target);
                }
            }

            return result;
        }

        private static Pawn? ResolveCurrentOverseer(
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn target)
        {
            Pawn? externalOverseer = target.GetOverseer();
            if (externalOverseer != null
                && !externalOverseer.Dead
                && !externalOverseer.Destroyed
                && !externalOverseer.Discarded
                && externalOverseer.health?.isBeingKilled != true
                && registry.IsValidAllocationPairForList(
                    externalOverseer,
                    target))
            {
                return externalOverseer;
            }

            if (!target.Dead
                && !target.Destroyed
                && !target.Discarded
                && target.health?.isBeingKilled != true
                && registry.IsValidAllocationPairForList(target, target))
            {
                return target;
            }

            return null;
        }

        private static bool HasActualAllocation(
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn target)
        {
            List<DataProcessingAllocationRecord>? records =
                GetAllocationRecords(registry);
            if (records == null)
            {
                return false;
            }

            for (int i = 0; i < records.Count; i++)
            {
                if (ReferenceEquals(records[i]?.target, target))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasActualAllocationsForOverseer(
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn overseer)
        {
            List<DataProcessingAllocationRecord>? records =
                GetAllocationRecords(registry);
            if (records == null)
            {
                return false;
            }

            for (int i = 0; i < records.Count; i++)
            {
                if (ReferenceEquals(records[i]?.overseer, overseer))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ClearTargetRuntime(
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn target)
        {
            IDictionary? nextChecks =
                NextDynamicCheckTickField?.GetValue(registry) as IDictionary;
            IDictionary? evaluations =
                CachedDynamicEvaluationField?.GetValue(registry) as IDictionary;

            nextChecks?.Remove(target);
            evaluations?.Remove(target);
        }

        private static List<DataProcessingAllocationRecord>? GetAllocationRecords(
            GameComponent_DataProcessingAllocationRegistry registry)
        {
            return AllocationRecordsField?.GetValue(registry)
                as List<DataProcessingAllocationRecord>;
        }

        private static List<DataProcessingDynamicTargetRecord>? GetDynamicTargetRecords(
            GameComponent_DataProcessingAllocationRegistry registry)
        {
            return DynamicTargetRecordsField?.GetValue(registry)
                as List<DataProcessingDynamicTargetRecord>;
        }

        private static List<DataProcessingDynamicAllocationRecord>? GetDynamicAllocationRecords(
            GameComponent_DataProcessingAllocationRegistry registry)
        {
            return DynamicAllocationRecordsField?.GetValue(registry)
                as List<DataProcessingDynamicAllocationRecord>;
        }

        private static Dictionary<Pawn, int>? GetNextDynamicChecks(
            GameComponent_DataProcessingAllocationRegistry registry)
        {
            return NextDynamicCheckTickField?.GetValue(registry)
                as Dictionary<Pawn, int>;
        }

        private static void InvokeRunPlan(
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn overseer)
        {
            if (RunDynamicPlanForOverseerMethod == null)
            {
                LogReflectionFailure(
                    "无法访问动态预算计划方法。",
                    7);
                return;
            }

            try
            {
                RunDynamicPlanForOverseerMethod.Invoke(
                    registry,
                    new object[] { overseer });
            }
            catch (Exception ex)
            {
                LogInvocationFailure(
                    "动态预算计划执行失败",
                    ex,
                    8);
            }
        }

        private static void InvokeNoArgs(
            MethodInfo? method,
            object instance,
            int keyOffset)
        {
            if (method == null)
            {
                LogReflectionFailure(
                    "无法访问动态分配内部方法。",
                    keyOffset);
                return;
            }

            try
            {
                method.Invoke(instance, null);
            }
            catch (Exception ex)
            {
                LogInvocationFailure(
                    "动态分配内部调用失败",
                    ex,
                    keyOffset + 20);
            }
        }

        private static void EnsureCurrentGame()
        {
            if (ReferenceEquals(queuedGame, Current.Game))
            {
                return;
            }

            ClearRuntimeQueue();
            queuedGame = Current.Game;
        }

        private static void ClearRuntimeQueue()
        {
            PendingReactivation.Clear();
            queuedGame = null;
            lastDormantScanTick = -DormantScanIntervalTicks;
        }

        private static void LogReflectionFailure(string message, int keyOffset)
        {
            Log.ErrorOnce(
                "[MAP-机械族机械师] " + message,
                ReflectionFailureLogKey + keyOffset);
        }

        private static void LogInvocationFailure(
            string message,
            Exception exception,
            int keyOffset)
        {
            Exception actual =
                exception is TargetInvocationException invocation
                && invocation.InnerException != null
                    ? invocation.InnerException
                    : exception;

            Log.ErrorOnce(
                "[MAP-机械族机械师] " + message + "：" + actual,
                ReflectionFailureLogKey + keyOffset);
        }

        private sealed class ReferencePawnComparer : IEqualityComparer<Pawn>
        {
            public static readonly ReferencePawnComparer Instance =
                new ReferencePawnComparer();

            public bool Equals(Pawn? left, Pawn? right)
            {
                return ReferenceEquals(left, right);
            }

            public int GetHashCode(Pawn pawn)
            {
                return pawn == null ? 0 : pawn.thingIDNumber;
            }
        }
    }
}
