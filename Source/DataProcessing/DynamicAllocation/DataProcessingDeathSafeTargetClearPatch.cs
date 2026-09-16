using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// ClearTarget 的正常语义会同时删除特化记录。死亡、暂时无监管者和监管者迁移
    /// 都只应释放实际分配，因此这些生命周期场景改用窄清理并跳过原方法。
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

            DataProcessingDynamicTargetRecord? config =
                __instance.GetDynamicTargetRecord(
                    overseer: null,
                    target: target);
            if (config == null)
            {
                return true;
            }

            // PostLoadInit 只整理引用与缓存；运行效果统一在 LoadedGame 后处理。
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                return false;
            }

            DataProcessingAllocationRecord? record =
                FindAllocationRecord(__instance, target);
            Pawn? recordedOverseer = record?.overseer ?? config.overseer;
            Pawn? currentOverseer = ResolveCurrentOverseer(__instance, target);

            bool targetDormant =
                target.Dead
                || target.Destroyed
                || target.health?.isBeingKilled == true;
            bool recordedOverseerDormant =
                recordedOverseer != null
                && (recordedOverseer.Dead
                    || recordedOverseer.Destroyed
                    || recordedOverseer.health?.isBeingKilled == true);
            bool relationDormant =
                !targetDormant
                && currentOverseer == null;
            bool overseerMigrating =
                !targetDormant
                && currentOverseer != null
                && recordedOverseer != null
                && !ReferenceEquals(currentOverseer, recordedOverseer);

            if (!targetDormant
                && !recordedOverseerDormant
                && !relationDormant
                && !overseerMigrating)
            {
                return true;
            }

            if (record != null && RemoveRecordMethod == null)
            {
                Log.ErrorOnce(
                    "[MAP-机械族机械师] 无法访问生命周期暂停所需的分配记录清理方法；为避免配置丢失，本次未执行 ClearTarget。",
                    ReflectionFailureLogKey);
                return false;
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
                        "生命周期暂停移除实际分配记录失败；为避免配置丢失，本次未执行原 ClearTarget",
                        ex,
                        ReflectionFailureLogKey + 1);
                    return false;
                }

                if (recordedOverseer != null && !recordedOverseer.Destroyed)
                {
                    try
                    {
                        __instance.SyncHediffsForOverseer(recordedOverseer);
                    }
                    catch (Exception ex)
                    {
                        LogFailure(
                            "生命周期暂停同步监管者数据流分发失败",
                            ex,
                            ReflectionFailureLogKey + 2);
                    }
                }
            }

            try
            {
                if (RemoveAllCommandFocusHediffsMethod != null)
                {
                    // 私有静态清理可处理位于尸体中的 Destroyed Pawn；不删除特化记录。
                    RemoveAllCommandFocusHediffsMethod.Invoke(
                        null,
                        new object[] { target });
                }
                else
                {
                    // 活体休眠或迁移时可安全使用公开同步入口。
                    __instance.SyncHediffForTarget(target);
                }
            }
            catch (Exception ex)
            {
                LogFailure(
                    "生命周期暂停清除目标指令聚焦失败",
                    ex,
                    ReflectionFailureLogKey + 3);
            }

            return false;
        }

        private static Pawn? ResolveCurrentOverseer(
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn target)
        {
            if (target.Dead
                || target.Destroyed
                || target.Discarded
                || target.health?.isBeingKilled == true)
            {
                return null;
            }

            Pawn? externalOverseer = DataProcessingOverseerResolver.GetAllocationOverseer(target);
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

            if (registry.IsValidAllocationPairForList(target, target))
            {
                return target;
            }

            return null;
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
