using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 监管者负面数据流分发的最终安全闸门。
    ///
    /// 无论调用来自动态计划、手动加档、读档恢复还是清理流程，只有当该监管者
    /// 当前全部正数分配目标均已拥有与保存特化一致的指令聚焦 Hediff 时，才允许
    /// 新增或刷新负面数据流分发。任一目标正面缺失时先移除负面，并进入安全重试。
    /// </summary>
    [HarmonyPatch]
    internal static class DataProcessingAllocationNegativeSafetyPatch
    {
        private static readonly Type RegistryType =
            typeof(GameComponent_DataProcessingAllocationRegistry);

        private static readonly FieldInfo RecordsByOverseerField =
            AccessTools.Field(RegistryType, "recordsByOverseer");

        private static readonly MethodInfo QueueRetryMethod =
            AccessTools.Method(
                typeof(DataProcessingDynamicAllocationFinalFix),
                "QueueRetry");

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                RegistryType,
                "SyncHediffsForOverseer",
                new[] { typeof(Pawn) });
        }

        private static bool Prefix(
            GameComponent_DataProcessingAllocationRegistry __instance,
            Pawn? overseer)
        {
            if (overseer == null
                || overseer.Destroyed
                || overseer.health?.hediffSet == null)
            {
                return false;
            }

            int totalSteps =
                __instance.GetTotalStepsForOverseer(overseer);

            // 无正数分配时必须让原方法执行，以便移除残留负面。
            if (totalSteps <= 0)
            {
                return true;
            }

            Dictionary<Pawn, List<DataProcessingAllocationRecord>> recordsByOverseer =
                (Dictionary<Pawn, List<DataProcessingAllocationRecord>>)
                    RecordsByOverseerField.GetValue(__instance);

            if (!recordsByOverseer.TryGetValue(
                    overseer,
                    out List<DataProcessingAllocationRecord>? records)
                || records == null)
            {
                RemoveNegative(overseer);
                QueueRetry(__instance, overseer);
                return false;
            }

            for (int i = 0; i < records.Count; i++)
            {
                DataProcessingAllocationRecord? record = records[i];
                Pawn? target = record?.target;

                if (record == null
                    || record.steps <= 0
                    || target == null
                    || target.Destroyed
                    || target.health?.hediffSet == null)
                {
                    RemoveNegative(overseer);
                    QueueRetry(__instance, overseer);
                    return false;
                }

                DataProcessingSpecialization specialization =
                    __instance.GetSpecializationForTarget(target);
                HediffDef? desiredDef =
                    DataProcessingAllocationUtility
                        .GetCommandFocusDef(specialization);

                if (desiredDef == null
                    || !target.health.hediffSet.HasHediff(desiredDef))
                {
                    RemoveNegative(overseer);
                    QueueRetry(__instance, overseer);
                    return false;
                }
            }

            return true;
        }

        private static void QueueRetry(
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn overseer)
        {
            QueueRetryMethod.Invoke(
                null,
                new object[] { registry, overseer });
        }

        private static void RemoveNegative(Pawn overseer)
        {
            HediffDef? def =
                DataProcessingAllocationUtility
                    .DataStreamDistributionDef;
            if (def == null
                || overseer.health?.hediffSet == null)
            {
                return;
            }

            Hediff? hediff =
                overseer.health.hediffSet
                    .GetFirstHediffOfDef(def);
            if (hediff != null)
            {
                overseer.health.RemoveHediff(hediff);
            }
        }
    }
}
