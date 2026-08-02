using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 顶置状态与动态目标配置相互独立。目标或监管者死亡时保留顶置记录；
    /// 目标复活并改由其他监管者控制时，将顶置记录迁移到当前监管者。
    /// </summary>
    [HarmonyPatch(
        typeof(GameComponent_DataProcessingAllocationRegistry),
        nameof(GameComponent_DataProcessingAllocationRegistry.CleanupInvalidPinRecords))]
    internal static class DataProcessingDormantPinCleanupPatch
    {
        private const int ReflectionFailureLogKey = 0x4D415050; // "MAPP"

        private static readonly FieldInfo? PinRecordsField =
            AccessTools.Field(
                typeof(GameComponent_DataProcessingAllocationRegistry),
                "pinRecords");

        private static readonly MethodInfo? EnsureNextPinOrderValidMethod =
            AccessTools.Method(
                typeof(GameComponent_DataProcessingAllocationRegistry),
                "EnsureNextPinOrderValid");

        private static bool Prefix(
            GameComponent_DataProcessingAllocationRegistry __instance)
        {
            List<DataProcessingAllocationPinRecord>? records =
                PinRecordsField?.GetValue(__instance)
                    as List<DataProcessingAllocationPinRecord>;
            if (records == null || EnsureNextPinOrderValidMethod == null)
            {
                Log.ErrorOnce(
                    "[MAP-机械族机械师] 无法访问顶置记录清理字段；已回退到原清理逻辑。",
                    ReflectionFailureLogKey);
                return true;
            }

            HashSet<(int overseerId, int targetId)> seenPairs =
                new HashSet<(int, int)>();

            for (int i = records.Count - 1; i >= 0; i--)
            {
                DataProcessingAllocationPinRecord? pin = records[i];
                Pawn? overseer = pin?.overseer;
                Pawn? target = pin?.target;

                if (pin == null
                    || overseer == null
                    || target == null
                    || ReferenceEquals(overseer, target)
                    || overseer.Discarded
                    || target.Discarded)
                {
                    records.RemoveAt(i);
                    continue;
                }

                bool targetDormant = IsDormant(target);
                Pawn? currentOverseer = targetDormant
                    ? null
                    : ResolveCurrentExternalOverseer(__instance, target);

                // 即使旧监管者仍处于死亡状态，只要目标已经被新监管者接管，
                // 顶置状态就应立即随目标迁移，而不是永远停留在旧监管者名下。
                if (currentOverseer != null
                    && !ReferenceEquals(currentOverseer, overseer))
                {
                    pin.overseer = currentOverseer;
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
                    continue;
                }

                int overseerId = overseer.thingIDNumber;
                int targetId = target.thingIDNumber;
                if (!seenPairs.Add((overseerId, targetId)))
                {
                    records.RemoveAt(i);
                }
            }

            try
            {
                EnsureNextPinOrderValidMethod.Invoke(__instance, null);
            }
            catch (Exception ex)
            {
                Exception actual =
                    ex is TargetInvocationException invocation
                    && invocation.InnerException != null
                        ? invocation.InnerException
                        : ex;

                Log.ErrorOnce(
                    "[MAP-机械族机械师] 重新计算顶置顺序失败：" + actual,
                    ReflectionFailureLogKey + 1);
            }

            return false;
        }

        private static Pawn? ResolveCurrentExternalOverseer(
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn target)
        {
            Pawn? current = target.GetOverseer();
            if (current == null
                || ReferenceEquals(current, target)
                || IsDormant(current)
                || current.Discarded
                || !registry.IsValidAllocationPairForList(current, target))
            {
                return null;
            }

            return current;
        }

        private static bool IsDormant(Pawn pawn)
        {
            return pawn.Dead
                || pawn.Destroyed
                || pawn.health?.isBeingKilled == true;
        }
    }
}
