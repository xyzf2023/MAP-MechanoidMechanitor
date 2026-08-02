using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 特化记录是动态配置之外的当前模式记录。死亡、暂时无监管者和监管者迁移时保留并迁移，
    /// 只有永久失效、重复或活体上的真正无效关系才删除。
    /// </summary>
    [HarmonyPatch(
        typeof(GameComponent_DataProcessingAllocationRegistry),
        "CleanupInvalidSpecializationRecords")]
    internal static class DataProcessingDormantSpecializationCleanupPatch
    {
        private const int ReflectionFailureLogKey = 0x4D415045; // "MAPE"

        private static readonly FieldInfo? SpecializationRecordsField =
            AccessTools.Field(
                typeof(GameComponent_DataProcessingAllocationRegistry),
                "specializationRecords");

        private static readonly MethodInfo? RebuildSpecializationCachesMethod =
            AccessTools.Method(
                typeof(GameComponent_DataProcessingAllocationRegistry),
                "RebuildSpecializationCaches");

        private static bool Prefix(
            GameComponent_DataProcessingAllocationRegistry __instance)
        {
            List<DataProcessingSpecializationRecord>? records =
                SpecializationRecordsField?.GetValue(__instance)
                    as List<DataProcessingSpecializationRecord>;
            if (records == null || RebuildSpecializationCachesMethod == null)
            {
                Log.ErrorOnce(
                    "[MAP-机械族机械师] 无法访问特化记录清理字段；已回退到原清理逻辑。",
                    ReflectionFailureLogKey);
                return true;
            }

            HashSet<Pawn> retainedTargets =
                new HashSet<Pawn>(ReferencePawnComparer.Instance);

            for (int i = records.Count - 1; i >= 0; i--)
            {
                DataProcessingSpecializationRecord? record = records[i];
                Pawn? overseer = record?.overseer;
                Pawn? target = record?.target;

                if (record == null
                    || overseer == null
                    || target == null
                    || overseer.Discarded
                    || target.Discarded)
                {
                    records.RemoveAt(i);
                    continue;
                }

                record.specialization =
                    DataProcessingAllocationUtility.NormalizeSpecialization(
                        record.specialization);

                if (!retainedTargets.Add(target))
                {
                    records.RemoveAt(i);
                    continue;
                }

                bool targetDormant = IsDormant(target);
                Pawn? currentOverseer = targetDormant
                    ? null
                    : ResolveCurrentOverseer(__instance, target);

                if (currentOverseer != null
                    && !ReferenceEquals(currentOverseer, overseer))
                {
                    record.overseer = currentOverseer;
                    overseer = currentOverseer;
                }

                bool relationDormant =
                    !targetDormant
                    && currentOverseer == null
                    && __instance.GetDynamicTargetRecord(
                        overseer: null,
                        target: target) != null;
                bool dormant =
                    targetDormant
                    || IsDormant(overseer)
                    || relationDormant;

                if (!dormant
                    && !__instance.IsValidAllocationPairForList(
                        overseer,
                        target))
                {
                    records.RemoveAt(i);
                }
            }

            try
            {
                RebuildSpecializationCachesMethod.Invoke(__instance, null);
            }
            catch (Exception ex)
            {
                Exception actual =
                    ex is TargetInvocationException invocation
                    && invocation.InnerException != null
                        ? invocation.InnerException
                        : ex;

                Log.ErrorOnce(
                    "[MAP-机械族机械师] 重建特化记录缓存失败：" + actual,
                    ReflectionFailureLogKey + 1);
            }

            return false;
        }

        private static Pawn? ResolveCurrentOverseer(
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn target)
        {
            Pawn? external = target.GetOverseer();
            if (external != null
                && !ReferenceEquals(external, target)
                && !external.Discarded
                && !IsDormant(external)
                && registry.IsValidAllocationPairForList(
                    external,
                    target))
            {
                return external;
            }

            if (!IsDormant(target)
                && registry.IsValidAllocationPairForList(target, target))
            {
                return target;
            }

            return null;
        }

        private static bool IsDormant(Pawn pawn)
        {
            return pawn.Dead
                || pawn.Destroyed
                || pawn.health?.isBeingKilled == true;
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
