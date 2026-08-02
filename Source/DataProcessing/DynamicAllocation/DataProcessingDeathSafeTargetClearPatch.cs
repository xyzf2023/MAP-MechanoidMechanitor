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

        private static readonly MethodInfo? RemoveAllCommandFocusHediffsMethod =
            AccessTools.Method(
                typeof(GameComponent_DataProcessingAllocationRegistry),
                "RemoveAllCommandFocusHediffs");

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

            if (RemoveRecordMethod == null
                || RemoveAllCommandFocusHediffsMethod == null)
            {
                Log.ErrorOnce(
                    "[MAP-机械族机械师] 无法访问死亡暂停所需的分配清理方法；已回退到原 ClearTarget。",
                    ReflectionFailureLogKey);
                return true;
            }

            try
            {
                if (record != null)
                {
                    RemoveRecordMethod.Invoke(
                        __instance,
                        new object[] { record });

                    if (overseer != null && !overseer.Destroyed)
                    {
                        __instance.SyncHediffsForOverseer(overseer);
                    }
                }

                RemoveAllCommandFocusHediffsMethod.Invoke(
                    null,
                    new object[] { target });
                return false;
            }
            catch (Exception ex)
            {
                Exception actual =
                    ex is TargetInvocationException invocation
                    && invocation.InnerException != null
                        ? invocation.InnerException
                        : ex;

                Log.ErrorOnce(
                    "[MAP-机械族机械师] 死亡暂停清理实际分配失败，已回退到原 ClearTarget：" + actual,
                    ReflectionFailureLogKey + 1);
                return true;
            }
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
    }
}
