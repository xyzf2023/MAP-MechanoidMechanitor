using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 动态分配系统最终收口层。
    ///
    /// 该层只接管上一轮实现中仍存在竞态或失败结果被忽略的方法：
    /// - 全局开关的即时应用与失败重试；
    /// - 读档后正负 Hediff 的安全恢复；
    /// - 成员刷新与独立检查间隔；
    /// - 目标正面 Hediff 的安全类型校正。
    ///
    /// 预算计算、状态判定、优先级分配和批量安全提交仍复用注册表中的正式实现。
    /// </summary>
    internal static class DataProcessingDynamicAllocationFinalFix
    {
        private sealed class RuntimeState
        {
            public readonly HashSet<Pawn> pendingOverseers =
                new HashSet<Pawn>(ReferencePawnComparer.Instance);

            public int retryEarliestTick;
        }

        private sealed class ReferencePawnComparer : IEqualityComparer<Pawn>
        {
            public static readonly ReferencePawnComparer Instance =
                new ReferencePawnComparer();

            public bool Equals(Pawn? x, Pawn? y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(Pawn obj)
            {
                return RuntimeHelpers.GetHashCode(obj);
            }
        }

        private static readonly Type RegistryType =
            typeof(GameComponent_DataProcessingAllocationRegistry);

        private static readonly ConditionalWeakTable<
            GameComponent_DataProcessingAllocationRegistry,
            RuntimeState> RuntimeStates =
                new ConditionalWeakTable<
                    GameComponent_DataProcessingAllocationRegistry,
                    RuntimeState>();

        private static readonly FieldInfo DynamicAllocationRecordsField =
            AccessTools.Field(RegistryType, "dynamicAllocationRecords");

        private static readonly FieldInfo DynamicTargetRecordsField =
            AccessTools.Field(RegistryType, "dynamicTargetRecords");

        private static readonly FieldInfo CachedEvaluationField =
            AccessTools.Field(RegistryType, "cachedDynamicEvaluationByTarget");

        private static readonly FieldInfo NextCheckTickField =
            AccessTools.Field(RegistryType, "nextDynamicCheckTickByTarget");

        private static readonly MethodInfo RebuildDynamicAllocationCachesMethod =
            AccessTools.Method(RegistryType, "RebuildDynamicAllocationCaches");

        private static readonly MethodInfo CollectTargetsMethod =
            AccessTools.Method(
                RegistryType,
                "CollectDynamicAllocationTargets",
                new[] { typeof(Pawn), typeof(List<Pawn>) });

        private static readonly MethodInfo EvaluateTargetMethod =
            AccessTools.Method(RegistryType, "EvaluateTarget");

        private static readonly MethodInfo RunDynamicPlanMethod =
            AccessTools.Method(
                RegistryType,
                "RunDynamicPlanForOverseer",
                new[] { typeof(Pawn) });

        private static readonly MethodInfo RestoreDefaultsMethod =
            AccessTools.Method(
                RegistryType,
                "RestoreDefaultsForOverseer",
                new[] { typeof(Pawn) });

        private static readonly MethodInfo CleanupDynamicTargetsMethod =
            AccessTools.Method(RegistryType, "CleanupInvalidDynamicTargetRecords");

        private static readonly MethodInfo RestoreAfterLoadMethod =
            AccessTools.Method(RegistryType, "RestoreCommandFocusHediffsAfterLoad");

        private static readonly MethodInfo CollectPostLoadOverseersMethod =
            AccessTools.Method(RegistryType, "CollectPostLoadReconciliationOverseers");

        private static readonly MethodInfo ReconcileNonDynamicMethod =
            AccessTools.Method(
                RegistryType,
                "TryReconcileNonDynamicOverseerAfterLoad",
                new[] { typeof(Pawn) });

        private static readonly MethodInfo CollectOverseersNeedingResyncMethod =
            AccessTools.Method(
                RegistryType,
                "CollectOverseersNeedingDataStreamResync");

        private static readonly MethodInfo TryApplyCommandFocusMethod =
            AccessTools.Method(RegistryType, "TryApplyCommandFocusHediffForTarget");

        private static RuntimeState GetRuntimeState(
            GameComponent_DataProcessingAllocationRegistry registry)
        {
            return RuntimeStates.GetValue(
                registry,
                _ => new RuntimeState());
        }

        private static List<DataProcessingDynamicAllocationRecord> GetGlobalRecords(
            GameComponent_DataProcessingAllocationRegistry registry)
        {
            return (List<DataProcessingDynamicAllocationRecord>)
                DynamicAllocationRecordsField.GetValue(registry);
        }

        private static List<DataProcessingDynamicTargetRecord> GetTargetRecords(
            GameComponent_DataProcessingAllocationRegistry registry)
        {
            return (List<DataProcessingDynamicTargetRecord>)
                DynamicTargetRecordsField.GetValue(registry);
        }

        private static IDictionary GetEvaluationCache(
            GameComponent_DataProcessingAllocationRegistry registry)
        {
            return (IDictionary)CachedEvaluationField.GetValue(registry);
        }

        private static Dictionary<Pawn, int> GetNextCheckTicks(
            GameComponent_DataProcessingAllocationRegistry registry)
        {
            return (Dictionary<Pawn, int>)NextCheckTickField.GetValue(registry);
        }

        private static List<Pawn> CollectTargets(
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn overseer)
        {
            List<Pawn> targets = new List<Pawn>();
            CollectTargetsMethod.Invoke(
                registry,
                new object[] { overseer, targets });
            return targets;
        }

        private static object EvaluateTarget(
            GameComponent_DataProcessingAllocationRegistry registry,
            DataProcessingDynamicTargetRecord config,
            Pawn target)
        {
            return EvaluateTargetMethod.Invoke(
                registry,
                new object[] { config, target });
        }

        private static bool RunDynamicPlan(
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn overseer)
        {
            return (bool)(RunDynamicPlanMethod.Invoke(
                registry,
                new object[] { overseer }) ?? false);
        }

        private static bool RestoreDefaults(
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn overseer)
        {
            return (bool)(RestoreDefaultsMethod.Invoke(
                registry,
                new object[] { overseer }) ?? false);
        }

        private static void ClearRuntimeStateForOverseer(
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn overseer)
        {
            if (overseer == null)
            {
                return;
            }

            IDictionary cache = GetEvaluationCache(registry);
            Dictionary<Pawn, int> nextChecks = GetNextCheckTicks(registry);
            List<DataProcessingDynamicTargetRecord> targetRecords =
                GetTargetRecords(registry);

            for (int i = 0; i < targetRecords.Count; i++)
            {
                DataProcessingDynamicTargetRecord? config = targetRecords[i];
                if (config?.target == null
                    || !ReferenceEquals(config.overseer, overseer))
                {
                    continue;
                }

                cache.Remove(config.target);
                nextChecks.Remove(config.target);
            }
        }

        private static bool ForceEvaluateAndApply(
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn overseer)
        {
            if (overseer == null
                || overseer.Destroyed
                || !registry.IsDynamicAllocationEnabled(overseer)
                || !registry.IsDynamicAllocationOverseerValid(overseer))
            {
                return false;
            }

            IDictionary cache = GetEvaluationCache(registry);
            Dictionary<Pawn, int> nextChecks = GetNextCheckTicks(registry);
            List<Pawn> targets = CollectTargets(registry, overseer);
            int now = Find.TickManager.TicksGame;

            for (int i = 0; i < targets.Count; i++)
            {
                Pawn target = targets[i];
                if (target == null || target.Destroyed || DataProcessingOverseerResolver.IsFrozenSelf(overseer, target))
                {
                    continue;
                }

                DataProcessingDynamicTargetRecord config =
                    registry.GetOrCreateDynamicTargetRecord(overseer, target);

                if (!config.enabled)
                {
                    cache.Remove(target);
                    nextChecks.Remove(target);
                    continue;
                }

                cache[target] = EvaluateTarget(registry, config, target);
                nextChecks[target] =
                    now + Mathf.Max(60, config.checkIntervalTicks);
            }

            bool succeeded = RunDynamicPlan(registry, overseer);
            if (succeeded
                && registry.GetTotalStepsForOverseer(overseer) <= 0)
            {
                RemoveDataStreamDistribution(overseer);
            }

            return succeeded;
        }

        private static void QueueRetry(
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn overseer)
        {
            if (overseer == null || overseer.Destroyed)
            {
                return;
            }

            RuntimeState state = GetRuntimeState(registry);
            state.pendingOverseers.Add(overseer);

            int requestedTick = Find.TickManager.TicksGame + 60;
            if (state.retryEarliestTick <= 0
                || requestedTick < state.retryEarliestTick)
            {
                state.retryEarliestTick = requestedTick;
            }
        }

        private static void RemoveRetry(
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn overseer)
        {
            RuntimeState state = GetRuntimeState(registry);
            state.pendingOverseers.Remove(overseer);
            if (state.pendingOverseers.Count == 0)
            {
                state.retryEarliestTick = 0;
            }
        }

        private static void ClearRetries(
            GameComponent_DataProcessingAllocationRegistry registry)
        {
            RuntimeState state = GetRuntimeState(registry);
            state.pendingOverseers.Clear();
            state.retryEarliestTick = 0;
        }

        private static void ProcessRetries(
            GameComponent_DataProcessingAllocationRegistry registry)
        {
            RuntimeState state = GetRuntimeState(registry);
            if (state.pendingOverseers.Count == 0
                || Find.TickManager == null
                || Find.TickManager.TicksGame < state.retryEarliestTick)
            {
                return;
            }

            List<Pawn> snapshot =
                new List<Pawn>(state.pendingOverseers);
            state.pendingOverseers.Clear();
            state.retryEarliestTick = 0;

            for (int i = 0; i < snapshot.Count; i++)
            {
                Pawn overseer = snapshot[i];
                if (overseer == null
                    || overseer.Destroyed
                    || !ResearchFeatureUnlockUtility
                        .IsDataProcessingAllocationUnlocked()
                    || !registry.IsDynamicAllocationOverseerValid(overseer))
                {
                    continue;
                }

                bool succeeded = registry.IsDynamicAllocationEnabled(overseer)
                    ? ForceEvaluateAndApply(registry, overseer)
                    : RestoreDefaults(registry, overseer);

                if (!succeeded)
                {
                    QueueRetry(registry, overseer);
                }
            }
        }

        private static void RemoveDataStreamDistribution(Pawn? pawn)
        {
            if (pawn?.health?.hediffSet == null)
            {
                return;
            }

            HediffDef? def =
                DataProcessingAllocationUtility.DataStreamDistributionDef;
            if (def == null)
            {
                return;
            }

            Hediff? hediff =
                pawn.health.hediffSet.GetFirstHediffOfDef(def);
            if (hediff != null)
            {
                pawn.health.RemoveHediff(hediff);
            }
        }

        private static void RemoveAllCommandFocusHediffs(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null)
            {
                return;
            }

            List<Hediff> snapshot =
                new List<Hediff>(pawn.health.hediffSet.hediffs);
            for (int i = 0; i < snapshot.Count; i++)
            {
                Hediff hediff = snapshot[i];
                if (hediff != null
                    && DataProcessingAllocationUtility
                        .IsAnyCommandFocusDef(hediff.def))
                {
                    pawn.health.RemoveHediff(hediff);
                }
            }
        }

        private static bool ApplyCorrectCommandFocus(
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn target)
        {
            if (target == null
                || target.Destroyed
                || target.health?.hediffSet == null)
            {
                return false;
            }

            int steps = registry.GetStepsForTarget(target);
            if (steps <= 0)
            {
                RemoveAllCommandFocusHediffs(target);
                return true;
            }

            DataProcessingSpecialization specialization =
                registry.GetSpecializationForTarget(target);

            return (bool)(TryApplyCommandFocusMethod.Invoke(
                registry,
                new object[]
                {
                    target,
                    specialization,
                    steps,
                    true
                }) ?? false);
        }

        private static void RemoveAllRelevantNegativesBeforeLoadRestore(
            GameComponent_DataProcessingAllocationRegistry registry)
        {
            HashSet<Pawn> overseers =
                new HashSet<Pawn>(ReferencePawnComparer.Instance);

            CollectOverseersNeedingResyncMethod.Invoke(
                registry,
                new object[] { overseers });

            for (int i = 0; i < GetGlobalRecords(registry).Count; i++)
            {
                Pawn? overseer = GetGlobalRecords(registry)[i]?.overseer;
                if (overseer != null && !overseer.Destroyed)
                {
                    overseers.Add(overseer);
                }
            }

            foreach (Pawn overseer in overseers)
            {
                RemoveDataStreamDistribution(overseer);
            }
        }

        private static void RefreshMembership(
            GameComponent_DataProcessingAllocationRegistry registry)
        {
            IDictionary cache = GetEvaluationCache(registry);
            Dictionary<Pawn, int> nextChecks = GetNextCheckTicks(registry);
            HashSet<Pawn> affectedOverseers =
                new HashSet<Pawn>(ReferencePawnComparer.Instance);

            List<DataProcessingDynamicAllocationRecord> globalRecords =
                GetGlobalRecords(registry);

            for (int i = 0; i < globalRecords.Count; i++)
            {
                DataProcessingDynamicAllocationRecord? global =
                    globalRecords[i];
                Pawn? overseer = global?.overseer;

                if (global?.enabled != true
                    || overseer == null
                    || overseer.Destroyed)
                {
                    continue;
                }

                List<Pawn> targets = CollectTargets(registry, overseer);
                for (int j = 0; j < targets.Count; j++)
                {
                    Pawn target = targets[j];
                    if (DataProcessingOverseerResolver.IsFrozenSelf(overseer, target)) continue;
                    DataProcessingDynamicTargetRecord? existing =
                        registry.GetDynamicTargetRecord(null, target);

                    bool isNew = false;
                    bool overseerChanged = false;
                    Pawn? oldOverseer = null;

                    if (existing == null)
                    {
                        existing = registry.GetOrCreateDynamicTargetRecord(
                            overseer,
                            target);
                        isNew = true;
                    }
                    else if (!ReferenceEquals(existing.overseer, overseer))
                    {
                        oldOverseer = existing.overseer;
                        existing.overseer = overseer;
                        existing.Normalize();
                        cache.Remove(target);
                        nextChecks.Remove(target);
                        overseerChanged = true;

                        if (oldOverseer != null)
                        {
                            affectedOverseers.Add(oldOverseer);
                        }
                    }

                    bool membershipChanged = isNew || overseerChanged;
                    if (!existing.enabled)
                    {
                        cache.Remove(target);
                        nextChecks.Remove(target);
                        if (membershipChanged)
                        {
                            affectedOverseers.Add(overseer);
                        }

                        continue;
                    }

                    bool runtimeMissing =
                        !cache.Contains(target)
                        || !nextChecks.ContainsKey(target);

                    if (membershipChanged || runtimeMissing)
                    {
                        cache[target] =
                            EvaluateTarget(registry, existing, target);
                        nextChecks[target] =
                            Find.TickManager.TicksGame
                            + Mathf.Max(60, existing.checkIntervalTicks);
                        affectedOverseers.Add(overseer);
                    }
                }
            }

            CleanupDynamicTargetsMethod.Invoke(
                registry,
                new object?[] { affectedOverseers });

            foreach (Pawn overseer in affectedOverseers)
            {
                if (overseer == null
                    || overseer.Destroyed
                    || !registry.IsDynamicAllocationEnabled(overseer))
                {
                    continue;
                }

                if (!RunDynamicPlan(registry, overseer))
                {
                    QueueRetry(registry, overseer);
                }
            }
        }

        private static bool ReconcileAfterLoad(
            GameComponent_DataProcessingAllocationRegistry registry)
        {
            RemoveAllRelevantNegativesBeforeLoadRestore(registry);

            bool positiveReady =
                (bool)(RestoreAfterLoadMethod.Invoke(
                    registry,
                    Array.Empty<object>()) ?? false);
            if (!positiveReady)
            {
                return false;
            }

            HashSet<Pawn> overseers =
                (HashSet<Pawn>)CollectPostLoadOverseersMethod.Invoke(
                    registry,
                    Array.Empty<object>());

            bool allSucceeded = true;
            foreach (Pawn overseer in overseers)
            {
                if (overseer == null || overseer.Destroyed)
                {
                    continue;
                }

                if (!DataProcessingAllocationUtility
                        .TryGetCurrentConsciousness(overseer, out _))
                {
                    allSucceeded = false;
                    continue;
                }

                try
                {
                    bool succeeded =
                        registry.IsDynamicAllocationEnabled(overseer)
                            ? ForceEvaluateAndApply(registry, overseer)
                            : (bool)(ReconcileNonDynamicMethod.Invoke(
                                registry,
                                new object[] { overseer }) ?? false);

                    if (!succeeded)
                    {
                        allSucceeded = false;
                    }
                }
                catch (Exception ex)
                {
                    allSucceeded = false;
                    Log.ErrorOnce(
                        "[MAP-机械族机械师] 读档后数据处理最终校正失败：" +
                        $"overseer={overseer.LabelShort}" +
                        $"（{overseer.ThingID}）：{ex}",
                        unchecked(0x4D415044 + overseer.thingIDNumber));
                }
            }

            return allSucceeded;
        }

        private static void ValidateRetainedCacheOwnership(
            GameComponent_DataProcessingAllocationRegistry registry,
            HashSet<Pawn>? affectedOverseers)
        {
            IDictionary cache = GetEvaluationCache(registry);
            Dictionary<Pawn, int> nextChecks = GetNextCheckTicks(registry);
            List<DataProcessingDynamicTargetRecord> records =
                GetTargetRecords(registry);

            for (int i = 0; i < records.Count; i++)
            {
                DataProcessingDynamicTargetRecord? config = records[i];
                if (config?.target == null || config.overseer == null)
                {
                    continue;
                }

                object? evaluation = cache[config.target];
                if (evaluation == null)
                {
                    continue;
                }

                FieldInfo? overseerField =
                    AccessTools.Field(evaluation.GetType(), "overseer");
                Pawn? cachedOverseer =
                    overseerField?.GetValue(evaluation) as Pawn;

                if (!ReferenceEquals(cachedOverseer, config.overseer))
                {
                    cache.Remove(config.target);
                    nextChecks.Remove(config.target);
                    affectedOverseers?.Add(config.overseer);
                }
            }
        }

        [HarmonyPatch]
        private static class GlobalTogglePatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    RegistryType,
                    "TrySetDynamicAllocationEnabled",
                    new[] { typeof(Pawn), typeof(bool) });
            }

            private static bool Prefix(
                GameComponent_DataProcessingAllocationRegistry __instance,
                Pawn? overseer,
                bool enabled,
                ref bool __result)
            {
                if (overseer == null
                    || !ResearchFeatureUnlockUtility
                        .IsDataProcessingAllocationUnlocked()
                    || !__instance.IsDynamicAllocationOverseerValid(overseer)
                    || overseer.mechanitor == null)
                {
                    __result = false;
                    return false;
                }

                // 此前缀替代原总开关方法，必须在创建全局默认模板前保留固定额度。
                __instance.EnsureQuotaMappingBeforeGlobalToggle(overseer);
                List<DataProcessingDynamicAllocationRecord> records =
                    GetGlobalRecords(__instance);
                DataProcessingDynamicAllocationRecord? existing = null;
                for (int i = 0; i < records.Count; i++)
                {
                    if (records[i] != null
                        && ReferenceEquals(records[i].overseer, overseer))
                    {
                        existing = records[i];
                        break;
                    }
                }

                if (existing == null)
                {
                    records.Add(
                        new DataProcessingDynamicAllocationRecord(
                            overseer,
                            enabled));
                }
                else
                {
                    existing.enabled = enabled;
                }

                RebuildDynamicAllocationCachesMethod.Invoke(
                    __instance,
                    Array.Empty<object>());

                bool succeeded;
                if (enabled)
                {
                    succeeded = ForceEvaluateAndApply(__instance, overseer);
                }
                else
                {
                    ClearRuntimeStateForOverseer(__instance, overseer);
                    succeeded = RestoreDefaults(__instance, overseer);
                }

                if (succeeded)
                {
                    RemoveRetry(__instance, overseer);
                }
                else
                {
                    QueueRetry(__instance, overseer);
                }

                __result = true;
                return false;
            }
        }

        [HarmonyPatch]
        private static class OriginalRetryQueuePatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    RegistryType,
                    "QueueGlobalTransitionRetry",
                    new[] { typeof(Pawn) });
            }

            private static bool Prefix(
                GameComponent_DataProcessingAllocationRegistry __instance,
                Pawn overseer)
            {
                QueueRetry(__instance, overseer);
                return false;
            }
        }

        [HarmonyPatch]
        private static class StateReevaluationPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    RegistryType,
                    "TriggerDynamicStateReevaluation",
                    new[] { typeof(Pawn), typeof(Pawn) });
            }

            private static bool Prefix(
                GameComponent_DataProcessingAllocationRegistry __instance,
                Pawn overseer,
                Pawn target)
            {
                if (DataProcessingOverseerResolver.IsFrozenSelf(overseer, target)) return false;
                DataProcessingDynamicTargetRecord? config =
                    __instance.GetDynamicTargetRecord(overseer, target);
                if (config == null)
                {
                    return false;
                }

                IDictionary cache = GetEvaluationCache(__instance);
                Dictionary<Pawn, int> nextChecks =
                    GetNextCheckTicks(__instance);

                if (!__instance.IsDynamicAllocationEnabled(overseer)
                    || !config.enabled)
                {
                    cache.Remove(target);
                    nextChecks.Remove(target);

                    if (__instance.IsDynamicAllocationEnabled(overseer)
                        && !RunDynamicPlan(__instance, overseer))
                    {
                        QueueRetry(__instance, overseer);
                    }

                    return false;
                }

                cache[target] = EvaluateTarget(__instance, config, target);
                nextChecks[target] =
                    Find.TickManager.TicksGame
                    + Mathf.Max(60, config.checkIntervalTicks);

                if (!RunDynamicPlan(__instance, overseer))
                {
                    QueueRetry(__instance, overseer);
                }

                return false;
            }
        }

        [HarmonyPatch]
        private static class SyncTargetHediffPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    RegistryType,
                    "SyncHediffForTarget",
                    new[] { typeof(Pawn) });
            }

            private static bool Prefix(
                GameComponent_DataProcessingAllocationRegistry __instance,
                Pawn? target)
            {
                if (target != null)
                {
                    ApplyCorrectCommandFocus(__instance, target);
                }

                return false;
            }
        }

        [HarmonyPatch]
        private static class MembershipRefreshPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    RegistryType,
                    "RefreshDynamicTargetMembership");
            }

            private static bool Prefix(
                GameComponent_DataProcessingAllocationRegistry __instance)
            {
                RefreshMembership(__instance);
                return false;
            }
        }

        [HarmonyPatch]
        private static class PostLoadReconciliationPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    RegistryType,
                    "TryRunPostLoadDynamicReconciliation");
            }

            private static bool Prefix(
                GameComponent_DataProcessingAllocationRegistry __instance,
                ref bool __result)
            {
                __result = ReconcileAfterLoad(__instance);
                return false;
            }
        }

        [HarmonyPatch]
        private static class RestoreAfterLoadSafetyPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    RegistryType,
                    "RestoreCommandFocusHediffsAfterLoad");
            }

            private static void Prefix(
                GameComponent_DataProcessingAllocationRegistry __instance)
            {
                RemoveAllRelevantNegativesBeforeLoadRestore(__instance);
            }
        }

        [HarmonyPatch]
        private static class RuntimeRetryTickPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(RegistryType, "GameComponentTick");
            }

            private static void Postfix(
                GameComponent_DataProcessingAllocationRegistry __instance)
            {
                ProcessRetries(__instance);
            }
        }

        [HarmonyPatch]
        private static class PlanFailureRetryPatch
        {
            private static MethodBase TargetMethod()
            {
                return RunDynamicPlanMethod;
            }

            private static void Postfix(
                GameComponent_DataProcessingAllocationRegistry __instance,
                Pawn overseer,
                bool __result)
            {
                if (!__result
                    && overseer != null
                    && !overseer.Destroyed
                    && __instance.IsDynamicAllocationEnabled(overseer)
                    && __instance.IsDynamicAllocationOverseerValid(overseer))
                {
                    QueueRetry(__instance, overseer);
                }
                else if (__result
                    && __instance.GetTotalStepsForOverseer(overseer) <= 0)
                {
                    RemoveDataStreamDistribution(overseer);
                }
            }
        }

        [HarmonyPatch]
        private static class RestoreDefaultsFailureRetryPatch
        {
            private static MethodBase TargetMethod()
            {
                return RestoreDefaultsMethod;
            }

            private static void Postfix(
                GameComponent_DataProcessingAllocationRegistry __instance,
                Pawn overseer,
                bool __result)
            {
                if (!__result
                    && overseer != null
                    && !overseer.Destroyed
                    && !__instance.IsDynamicAllocationEnabled(overseer)
                    && __instance.IsDynamicAllocationOverseerValid(overseer))
                {
                    QueueRetry(__instance, overseer);
                }
                else if (__result
                    && __instance.GetTotalStepsForOverseer(overseer) <= 0)
                {
                    RemoveDataStreamDistribution(overseer);
                }
            }
        }

        [HarmonyPatch]
        private static class DynamicTargetCleanupPatch
        {
            private static MethodBase TargetMethod()
            {
                return CleanupDynamicTargetsMethod;
            }

            private static void Postfix(
                GameComponent_DataProcessingAllocationRegistry __instance,
                HashSet<Pawn>? affectedOverseers = null)
            {
                ValidateRetainedCacheOwnership(
                    __instance,
                    affectedOverseers);
            }
        }

        [HarmonyPatch]
        private static class LoadedGameRuntimeCleanupPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(RegistryType, "LoadedGame");
            }

            private static void Prefix(
                GameComponent_DataProcessingAllocationRegistry __instance)
            {
                ClearRetries(__instance);
            }
        }

        [HarmonyPatch]
        private static class RemoveDynamicOverseerRuntimeCleanupPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    RegistryType,
                    "RemoveDynamicAllocationRecordForOverseer",
                    new[] { typeof(Pawn) });
            }

            private static void Postfix(
                GameComponent_DataProcessingAllocationRegistry __instance,
                Pawn? overseer)
            {
                if (overseer != null)
                {
                    RemoveRetry(__instance, overseer);
                }
            }
        }

        [HarmonyPatch]
        private static class ClearAllRuntimeCleanupPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    RegistryType,
                    "ClearAllAllocationsAndEffects");
            }

            private static void Postfix(
                GameComponent_DataProcessingAllocationRegistry __instance)
            {
                ClearRetries(__instance);
            }
        }
    }
}
