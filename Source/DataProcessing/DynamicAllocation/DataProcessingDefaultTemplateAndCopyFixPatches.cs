using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 新目标配置创建完成后，恢复非机械族目标原有的初始化语义。
    /// “新加入机械族默认设置”只允许应用到机械族；人类目标仍以当前实际额度初始化。
    /// </summary>
    [HarmonyPatch(
        typeof(GameComponent_DataProcessingAllocationRegistry),
        nameof(GameComponent_DataProcessingAllocationRegistry.GetOrCreateDynamicTargetRecord))]
    internal static class DataProcessingNonMechanoidDefaultTemplateGuardPatch
    {
        private struct CreationState
        {
            public bool restoreLegacyDefaults;
            public int currentSteps;
        }

        private static void Prefix(
            GameComponent_DataProcessingAllocationRegistry __instance,
            Pawn overseer,
            Pawn target,
            out CreationState __state)
        {
            __state = default;
            if (__instance == null
                || overseer == null
                || target == null
                || target.RaceProps?.IsMechanoid == true)
            {
                return;
            }

            bool alreadyExists =
                __instance.GetDynamicTargetRecord(overseer, target) != null;
            bool hasGlobalRecord =
                __instance.FindDynamicAllocationRecordForUI(overseer) != null;

            if (alreadyExists || !hasGlobalRecord)
            {
                return;
            }

            __state.restoreLegacyDefaults = true;
            __state.currentSteps =
                Mathf.Max(
                    0,
                    __instance.GetStepsForOverseerTarget(overseer, target));
        }

        private static void Postfix(
            DataProcessingDynamicTargetRecord __result,
            CreationState __state)
        {
            if (!__state.restoreLegacyDefaults || __result == null)
            {
                return;
            }

            int steps = Mathf.Max(0, __state.currentSteps);

            __result.enabled = true;
            __result.normalSteps = steps;
            __result.commonMaxSteps = steps;
            __result.advancedMaxEnabled = false;
            __result.generalMaxSteps = steps;
            __result.productionMaxSteps = steps;
            __result.fireControlMaxSteps = steps;
            __result.assaultMaxSteps = steps;
            __result.priority = 3;
            __result.checkIntervalTicks = 600;
            __result.switchForWork = true;
            __result.switchForDraftedWeapon = true;
            __result.switchForCloseMelee = true;
            __result.applyUndraftedFallback = true;

            // defaultSpecialization保留原方法已经完成的自动判断结果。
            __result.Normalize();
        }
    }

    /// <summary>
    /// 完整替换本轮新增的批量重置入口：
    /// 1. 同时收集当前活动机械族和休眠配置；
    /// 2. 不把目标留在“当前tick到期”状态；
    /// 3. 整批只执行一次预算计划。
    /// </summary>
    [HarmonyPatch(
        typeof(GameComponent_DataProcessingAllocationRegistry),
        nameof(GameComponent_DataProcessingAllocationRegistry.ResetAllMechanoidDynamicSettingsToDefaults))]
    internal static class DataProcessingResetAllDefaultsFixPatch
    {
        private static bool Prefix(
            GameComponent_DataProcessingAllocationRegistry __instance,
            Pawn? overseer,
            ref int __result)
        {
            if (!DataProcessingDefaultTemplateAndCopyFixUtility.CanReplaceReset)
            {
                DataProcessingDefaultTemplateAndCopyFixUtility.LogReflectionFailure(
                    "批量重置所需的注册表成员不可用");
                return true;
            }

            __result =
                DataProcessingDefaultTemplateAndCopyFixUtility
                    .ResetAllMechanoidSettings(__instance, overseer);
            return false;
        }
    }

    /// <summary>
    /// 完整替换本轮新增的粘贴入口，避免
    /// TriggerDynamicStateReevaluation之后再次显式运行完整预算计划。
    /// </summary>
    [HarmonyPatch(
        typeof(GameComponent_DataProcessingAllocationRegistry),
        nameof(GameComponent_DataProcessingAllocationRegistry.ApplyDynamicTargetSettingsSnapshot))]
    internal static class DataProcessingApplySettingsSnapshotFixPatch
    {
        private static bool Prefix(
            GameComponent_DataProcessingAllocationRegistry __instance,
            Pawn? overseer,
            Pawn? target,
            DataProcessingDynamicTargetSettingsSnapshot? snapshot,
            DataProcessingTargetCopyMode mode,
            ref bool __result)
        {
            if (!DataProcessingDefaultTemplateAndCopyFixUtility.CanReplacePaste)
            {
                DataProcessingDefaultTemplateAndCopyFixUtility.LogReflectionFailure(
                    "设置粘贴所需的注册表成员不可用");
                return true;
            }

            __result =
                DataProcessingDefaultTemplateAndCopyFixUtility.ApplySnapshotOnce(
                    __instance,
                    overseer,
                    target,
                    snapshot,
                    mode);
            return false;
        }
    }

    internal static class DataProcessingDefaultTemplateAndCopyFixUtility
    {
        private const int ReflectionFailureLogKey = 0x4D415446; // "MATF"
        private const int ResetFailureLogKey = ReflectionFailureLogKey + 1;
        private const int PasteFailureLogKey = ReflectionFailureLogKey + 2;

        private static readonly FieldInfo? DynamicTargetRecordsField =
            AccessTools.Field(
                typeof(GameComponent_DataProcessingAllocationRegistry),
                "dynamicTargetRecords");

        private static readonly FieldInfo? NextDynamicCheckTickField =
            AccessTools.Field(
                typeof(GameComponent_DataProcessingAllocationRegistry),
                "nextDynamicCheckTickByTarget");

        private static readonly FieldInfo? CachedDynamicEvaluationField =
            AccessTools.Field(
                typeof(GameComponent_DataProcessingAllocationRegistry),
                "cachedDynamicEvaluationByTarget");

        private static readonly MethodInfo? SetStoredSpecializationMethod =
            AccessTools.Method(
                typeof(GameComponent_DataProcessingAllocationRegistry),
                "SetStoredSpecializationWithoutImmediateSync",
                new[]
                {
                    typeof(Pawn),
                    typeof(Pawn),
                    typeof(DataProcessingSpecialization)
                });

        private static readonly MethodInfo? RebuildSpecializationCachesMethod =
            AccessTools.Method(
                typeof(GameComponent_DataProcessingAllocationRegistry),
                "RebuildSpecializationCaches");

        private static readonly MethodInfo? RebuildDynamicTargetCachesMethod =
            AccessTools.Method(
                typeof(GameComponent_DataProcessingAllocationRegistry),
                "RebuildDynamicTargetCaches");

        private static readonly MethodInfo? RunDynamicPlanMethod =
            AccessTools.Method(
                typeof(GameComponent_DataProcessingAllocationRegistry),
                "RunDynamicPlanForOverseer",
                new[] { typeof(Pawn) });

        private static readonly MethodInfo? ReconcileNonDynamicMethod =
            AccessTools.Method(
                typeof(GameComponent_DataProcessingAllocationRegistry),
                "TryReconcileNonDynamicOverseerAfterLoad",
                new[] { typeof(Pawn) });

        private static readonly MethodInfo? TriggerDynamicStateReevaluationMethod =
            AccessTools.Method(
                typeof(GameComponent_DataProcessingAllocationRegistry),
                "TriggerDynamicStateReevaluation",
                new[] { typeof(Pawn), typeof(Pawn) });

        private static readonly MethodInfo? IsDynamicOverseerValidMethod =
            AccessTools.Method(
                typeof(GameComponent_DataProcessingAllocationRegistry),
                "IsDynamicAllocationOverseerValid",
                new[] { typeof(Pawn) });

        internal static bool CanReplaceReset =>
            DynamicTargetRecordsField != null
            && NextDynamicCheckTickField != null
            && CachedDynamicEvaluationField != null
            && SetStoredSpecializationMethod != null
            && RebuildSpecializationCachesMethod != null
            && RebuildDynamicTargetCachesMethod != null
            && RunDynamicPlanMethod != null
            && ReconcileNonDynamicMethod != null;

        internal static bool CanReplacePaste =>
            CachedDynamicEvaluationField != null
            && RebuildDynamicTargetCachesMethod != null
            && TriggerDynamicStateReevaluationMethod != null
            && IsDynamicOverseerValidMethod != null;

        internal static int ResetAllMechanoidSettings(
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn? overseer)
        {
            if (registry == null || overseer == null)
            {
                return 0;
            }

            DataProcessingDynamicTargetDefaults? defaults =
                registry.GetDynamicTargetDefaultsForUI(overseer);
            if (defaults == null)
            {
                return 0;
            }

            try
            {
                defaults.Normalize();

                HashSet<Pawn> targets =
                    new HashSet<Pawn>(PawnReferenceComparer.Instance);

                CollectActiveMechanoids(registry, overseer, targets);
                CollectDormantMechanoidConfigs(registry, overseer, targets);

                IDictionary nextChecks =
                    (IDictionary)NextDynamicCheckTickField!.GetValue(registry);
                IDictionary evaluations =
                    (IDictionary)CachedDynamicEvaluationField!.GetValue(registry);

                int count = 0;
                foreach (Pawn target in targets)
                {
                    if (target == null
                        || target.Destroyed
                        || target.Discarded
                        || target.RaceProps?.IsMechanoid != true)
                    {
                        continue;
                    }

                    DataProcessingDynamicTargetRecord config =
                        registry.GetDynamicTargetRecord(overseer, target)
                        ?? registry.GetOrCreateDynamicTargetRecord(overseer, target);

                    defaults.ApplyTo(config);

                    DataProcessingSpecialization automaticSpecialization =
                        DataProcessingDynamicAllocationUtility
                            .DetermineInitialDefaultSpecialization(target);
                    config.defaultSpecialization = automaticSpecialization;
                    config.Normalize();

                    SetStoredSpecializationMethod!.Invoke(
                        registry,
                        new object[]
                        {
                            overseer,
                            target,
                            automaticSpecialization
                        });

                    // 清除旧评估和旧到期时间。后续唯一的一次计划会自行评估活动目标，
                    // 并按各目标真实检查间隔安排下一次检查。
                    evaluations.Remove(target);
                    nextChecks.Remove(target);
                    count++;
                }

                RebuildSpecializationCachesMethod!.Invoke(registry, null);
                RebuildDynamicTargetCachesMethod!.Invoke(registry, null);

                if (count > 0)
                {
                    MethodInfo planMethod =
                        registry.IsDynamicAllocationEnabled(overseer)
                            ? RunDynamicPlanMethod!
                            : ReconcileNonDynamicMethod!;
                    planMethod.Invoke(registry, new object[] { overseer });
                }

                return count;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce(
                    "[MAP-机械族机械师] 重置机械族动态分配默认设置失败："
                    + Unwrap(ex),
                    ResetFailureLogKey);
                return 0;
            }
        }

        internal static bool ApplySnapshotOnce(
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn? overseer,
            Pawn? target,
            DataProcessingDynamicTargetSettingsSnapshot? snapshot,
            DataProcessingTargetCopyMode mode)
        {
            if (registry == null
                || overseer == null
                || target == null
                || snapshot == null
                || target.Dead
                || target.Destroyed
                || target.Discarded
                || target.RaceProps?.IsMechanoid != true)
            {
                return false;
            }

            try
            {
                bool overseerValid =
                    (bool)IsDynamicOverseerValidMethod!.Invoke(
                        registry,
                        new object[] { overseer });
                if (!overseerValid
                    || !registry.IsValidAllocationPairForList(overseer, target))
                {
                    return false;
                }

                DataProcessingDynamicTargetRecord config =
                    registry.GetOrCreateDynamicTargetRecord(overseer, target);
                DataProcessingSpecialization originalDefault =
                    config.defaultSpecialization;

                snapshot.ApplyTo(config, mode);
                config.defaultSpecialization = originalDefault;
                config.Normalize();

                RebuildDynamicTargetCachesMethod!.Invoke(registry, null);

                IDictionary evaluations =
                    (IDictionary)CachedDynamicEvaluationField!.GetValue(registry);
                evaluations.Remove(target);

                if (registry.IsDynamicAllocationEnabled(overseer))
                {
                    // 该私有入口会完成一次评估、写入真实下一检查时间，
                    // 并在内部只触发一次预算计划。
                    TriggerDynamicStateReevaluationMethod!.Invoke(
                        registry,
                        new object[] { overseer, target });
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce(
                    "[MAP-机械族机械师] 粘贴机械族动态分配设置失败："
                    + Unwrap(ex),
                    PasteFailureLogKey);
                return false;
            }
        }

        private static void CollectActiveMechanoids(
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn overseer,
            HashSet<Pawn> targets)
        {
            if (overseer.RaceProps?.IsMechanoid == true
                && registry.IsValidAllocationPairForList(overseer, overseer))
            {
                targets.Add(overseer);
            }

            if (overseer.mechanitor == null)
            {
                return;
            }

            List<Pawn> overseen = overseer.mechanitor.OverseenPawns;
            for (int i = 0; i < overseen.Count; i++)
            {
                Pawn target = overseen[i];
                if (target != null
                    && target.RaceProps?.IsMechanoid == true
                    && registry.IsValidAllocationPairForList(overseer, target))
                {
                    targets.Add(target);
                }
            }
        }

        private static void CollectDormantMechanoidConfigs(
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn overseer,
            HashSet<Pawn> targets)
        {
            List<DataProcessingDynamicTargetRecord>? records =
                DynamicTargetRecordsField!.GetValue(registry)
                    as List<DataProcessingDynamicTargetRecord>;
            if (records == null)
            {
                return;
            }

            for (int i = 0; i < records.Count; i++)
            {
                DataProcessingDynamicTargetRecord? record = records[i];
                Pawn? target = record?.target;
                if (target != null
                    && ReferenceEquals(record!.overseer, overseer)
                    && target.RaceProps?.IsMechanoid == true
                    && !target.Destroyed
                    && !target.Discarded)
                {
                    targets.Add(target);
                }
            }
        }

        internal static void LogReflectionFailure(string detail)
        {
            Log.ErrorOnce(
                "[MAP-机械族机械师] 动态默认设置修复层初始化失败："
                + detail,
                ReflectionFailureLogKey);
        }

        private static Exception Unwrap(Exception exception)
        {
            return exception is TargetInvocationException invocation
                && invocation.InnerException != null
                    ? invocation.InnerException
                    : exception;
        }

        private sealed class PawnReferenceComparer : IEqualityComparer<Pawn>
        {
            internal static readonly PawnReferenceComparer Instance =
                new PawnReferenceComparer();

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
