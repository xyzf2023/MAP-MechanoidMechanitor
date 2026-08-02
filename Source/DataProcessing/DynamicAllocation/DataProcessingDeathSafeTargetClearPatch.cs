using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// ClearTarget 的正常语义会同时删除特化记录。死亡暂停只应释放实际分配，
    /// 因此当目标或其监管者处于死亡流程时，改用窄清理并跳过原方法。
    /// </summary>
    [HarmonyPatch(
        typeof(GameComponent_DataProcessingAllocationRegistry),
        nameof(GameComponent_DataProcessingAllocationRegistry.ClearTarget))]
    internal static class DataProcessingDeathSafeTargetClearPatch
    {
        private const int ReflectionFailureLogKey = 0x4D415043; // "MAPC"

        private static readonly FieldInfo? AllocationRecordsField =
            AccessTools.Field(
                typeof(GameComponent_DataProcessingAllocationRegistry),
                "records");

        private static readonly MethodInfo? RemoveRecordMethod =
            AccessTools.Method(
                typeof(GameComponent_DataProcessingAllocationRegistry),
                "RemoveRecord");

        private static bool Prefix(
            GameComponent_DataProcessingAllocationRegistry __instance,
            Pawn? target)
        {
            if (__instance == null || target == null || target.Discarded)
            {
                return true;
            }

            DataProcessingAllocationRecord? record =
                FindAllocationRecord(__instance, target);
            Pawn? overseer = record?.overseer;

            bool targetDying =
                target.Dead
                || target.Destroyed
                || target.health?.isBeingKilled == true;
            bool overseerDying =
                overseer != null
                && (overseer.Dead
                    || overseer.Destroyed
                    || overseer.health?.isBeingKilled == true);

            if (!targetDying && !overseerDying)
            {
                return true;
            }

            if (record != null && RemoveRecordMethod == null)
            {
                Log.ErrorOnce(
                    "[MAP-机械族机械师] 无法访问死亡暂停所需的分配记录清理方法；已回退到原 ClearTarget。",
                    ReflectionFailureLogKey);
                return true;
            }

            if (record != null)
            {
                try
                {
                    RemoveRecordMethod!.Invoke(
                        __instance,
                        new object[] { record });
                }
                catch (Exception ex)
                {
                    LogFailure(
                        "死亡暂停移除实际分配记录失败，已回退到原 ClearTarget",
                        ex,
                        ReflectionFailureLogKey + 1);
                    return true;
                }

                // 从这里开始实际记录已经被修改，不能再回退原 ClearTarget，
                // 否则会把需要保留的特化配置一并删除。
                if (overseer != null && !overseer.Destroyed)
                {
                    try
                    {
                        __instance.SyncHediffsForOverseer(overseer);
                    }
                    catch (Exception ex)
                    {
                        LogFailure(
                            "死亡暂停同步监管者数据流分发失败",
                            ex,
                            ReflectionFailureLogKey + 2);
                    }
                }
            }

            try
            {
                // 当前实际档数已经为 0；公共同步入口会清除指令聚焦 Hediff，
                // 并且明确保留特化配置记录。
                __instance.SyncHediffForTarget(target);
            }
            catch (Exception ex)
            {
                LogFailure(
                    "死亡暂停清除目标指令聚焦失败",
                    ex,
                    ReflectionFailureLogKey + 3);
            }

            return false;
        }

        private static DataProcessingAllocationRecord? FindAllocationRecord(
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn target)
        {
            List<DataProcessingAllocationRecord>? records =
                AllocationRecordsField?.GetValue(registry)
                    as List<DataProcessingAllocationRecord>;
            if (records == null)
            {
                return null;
            }

            for (int i = 0; i < records.Count; i++)
            {
                DataProcessingAllocationRecord? record = records[i];
                if (record != null && ReferenceEquals(record.target, target))
                {
                    return record;
                }
            }

            return null;
        }

        private static void LogFailure(
            string message,
            Exception exception,
            int key)
        {
            Exception actual =
                exception is TargetInvocationException invocation
                && invocation.InnerException != null
                    ? invocation.InnerException
                    : exception;

            Log.ErrorOnce(
                "[MAP-机械族机械师] " + message + "：" + actual,
                key);
        }
    }
}
