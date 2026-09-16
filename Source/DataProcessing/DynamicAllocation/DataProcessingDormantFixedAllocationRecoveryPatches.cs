using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 单体动态关闭或全局动态关闭时，目标不进入动态检查队列；复活后需要单独恢复常态额度。
    /// </summary>
    [HarmonyPatch(
        typeof(DataProcessingPawnLifecycleCoordinator),
        nameof(DataProcessingPawnLifecycleCoordinator.EnqueueReactivation))]
    internal static class DataProcessingFixedReactivationEnqueuePatch
    {
        private static void Postfix(Pawn? pawn)
        {
            DataProcessingDormantFixedAllocationRecoveryQueue.Enqueue(pawn);
        }
    }

    [HarmonyPatch(
        typeof(GameComponent_DataProcessingAllocationRegistry),
        nameof(GameComponent_DataProcessingAllocationRegistry.GameComponentTick))]
    internal static class DataProcessingFixedReactivationTickPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(
            GameComponent_DataProcessingAllocationRegistry __instance)
        {
            DataProcessingDormantFixedAllocationRecoveryQueue.Process(__instance);
        }
    }

    internal static class DataProcessingDormantFixedAllocationRecoveryQueue
    {
        private const int FallbackScanIntervalTicks = 600;
        private const int ReflectionFailureLogKey = 0x4D415046; // "MAPF"

        private static readonly FieldInfo? DynamicTargetRecordsField =
            AccessTools.Field(
                typeof(GameComponent_DataProcessingAllocationRegistry),
                "dynamicTargetRecords");

        private static readonly MethodInfo? RunDynamicPlanForOverseerMethod =
            AccessTools.Method(
                typeof(GameComponent_DataProcessingAllocationRegistry),
                "RunDynamicPlanForOverseer");

        private static readonly MethodInfo? ReconcileNonDynamicOverseerMethod =
            AccessTools.Method(
                typeof(GameComponent_DataProcessingAllocationRegistry),
                "TryReconcileNonDynamicOverseerAfterLoad");

        private static readonly Dictionary<Pawn, int> PendingPawns =
            new Dictionary<Pawn, int>(ReferencePawnComparer.Instance);

        private static Game? queuedGame;
        private static int lastFallbackScanTick = -FallbackScanIntervalTicks;

        public static void Enqueue(Pawn? pawn)
        {
            if (pawn == null
                || pawn.Dead
                || pawn.Destroyed
                || pawn.Discarded
                || Current.Game == null
                || Find.TickManager == null)
            {
                return;
            }

            GameComponent_DataProcessingAllocationRegistry? registry =
                GameComponent_DataProcessingAllocationRegistry.CurrentRegistry;
            if (registry == null
                || (registry.GetDynamicTargetRecord(null, pawn) == null
                    && registry.FindDynamicAllocationRecordForUI(pawn) == null))
            {
                return;
            }

            EnsureCurrentGame();
            PendingPawns[pawn] = Find.TickManager.TicksGame + 1;
        }

        public static void Process(
            GameComponent_DataProcessingAllocationRegistry? registry)
        {
            if (registry == null
                || Current.Game == null
                || Find.TickManager == null)
            {
                Clear();
                return;
            }

            EnsureCurrentGame();
            int now = Find.TickManager.TicksGame;
            bool fallbackDue =
                now - lastFallbackScanTick >= FallbackScanIntervalTicks;

            List<Pawn> due = new List<Pawn>();
            foreach (KeyValuePair<Pawn, int> pair in PendingPawns)
            {
                if (pair.Value <= now)
                {
                    due.Add(pair.Key);
                }
            }

            if (due.Count == 0 && !fallbackDue)
            {
                return;
            }

            for (int i = 0; i < due.Count; i++)
            {
                PendingPawns.Remove(due[i]);
            }

            if (fallbackDue)
            {
                lastFallbackScanTick = now;
            }

            HashSet<Pawn> overseers =
                new HashSet<Pawn>(ReferencePawnComparer.Instance);

            for (int i = 0; i < due.Count; i++)
            {
                CollectRequiredOverseers(registry, due[i], overseers);
            }

            if (fallbackDue)
            {
                CollectFixedAllocationDiscrepancies(registry, overseers);
            }

            foreach (Pawn overseer in overseers)
            {
                RestoreOverseer(registry, overseer);
            }
        }

        private static void CollectRequiredOverseers(
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn pawn,
            HashSet<Pawn> outOverseers)
        {
            if (!IsActive(pawn))
            {
                return;
            }

            DataProcessingDynamicAllocationRecord? ownGlobal =
                registry.FindDynamicAllocationRecordForUI(pawn);
            if (ownGlobal != null
                && RequiresDirectRestoreForOverseer(registry, pawn, ownGlobal))
            {
                outOverseers.Add(pawn);
            }

            DataProcessingDynamicTargetRecord? config =
                registry.GetDynamicTargetRecord(null, pawn);
            if (config == null)
            {
                return;
            }

            Pawn? currentOverseer = ResolveCurrentOverseer(registry, pawn);
            if (currentOverseer == null)
            {
                return;
            }

            DataProcessingDynamicAllocationRecord? global =
                registry.FindDynamicAllocationRecordForUI(currentOverseer);
            if (global == null)
            {
                return;
            }

            if (!global.enabled || !config.enabled)
            {
                outOverseers.Add(currentOverseer);
            }
        }

        private static bool RequiresDirectRestoreForOverseer(
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn overseer,
            DataProcessingDynamicAllocationRecord global)
        {
            if (!global.enabled)
            {
                return true;
            }

            List<DataProcessingDynamicTargetRecord>? configs =
                GetDynamicTargetRecords(registry);
            if (configs == null)
            {
                return false;
            }

            for (int i = 0; i < configs.Count; i++)
            {
                DataProcessingDynamicTargetRecord? config = configs[i];
                if (config?.target != null
                    && ReferenceEquals(config.overseer, overseer)
                    && !config.enabled
                    && registry.GetStepsForOverseerTarget(
                        overseer,
                        config.target) != config.normalSteps)
                {
                    return true;
                }
            }

            return false;
        }

        private static void CollectFixedAllocationDiscrepancies(
            GameComponent_DataProcessingAllocationRegistry registry,
            HashSet<Pawn> outOverseers)
        {
            List<DataProcessingDynamicTargetRecord>? configs =
                GetDynamicTargetRecords(registry);
            if (configs == null)
            {
                Log.ErrorOnce(
                    "[MAP-机械族机械师] 无法访问动态目标配置，固定额度恢复兜底已跳过。",
                    ReflectionFailureLogKey);
                return;
            }

            List<DataProcessingDynamicTargetRecord> snapshot =
                new List<DataProcessingDynamicTargetRecord>(configs);
            for (int i = 0; i < snapshot.Count; i++)
            {
                DataProcessingDynamicTargetRecord? config = snapshot[i];
                Pawn? target = config?.target;
                if (config == null || !IsActive(target))
                {
                    continue;
                }

                Pawn? overseer = ResolveCurrentOverseer(registry, target!);
                if (overseer == null)
                {
                    continue;
                }

                DataProcessingDynamicAllocationRecord? global =
                    registry.FindDynamicAllocationRecordForUI(overseer);
                if (global == null
                    || (global.enabled && config.enabled)
                    || registry.GetStepsForOverseerTarget(
                        overseer,
                        target) == config.normalSteps)
                {
                    continue;
                }

                outOverseers.Add(overseer);
            }
        }

        private static void RestoreOverseer(
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn overseer)
        {
            if (!IsActive(overseer)
                || !registry.IsDynamicAllocationOverseerValid(overseer))
            {
                return;
            }

            DataProcessingDynamicAllocationRecord? global =
                registry.FindDynamicAllocationRecordForUI(overseer);
            if (global == null)
            {
                return;
            }

            MethodInfo? method = global.enabled
                ? RunDynamicPlanForOverseerMethod
                : ReconcileNonDynamicOverseerMethod;
            if (method == null)
            {
                Log.ErrorOnce(
                    "[MAP-机械族机械师] 无法访问固定额度恢复计划方法。",
                    ReflectionFailureLogKey + 1);
                return;
            }

            try
            {
                method.Invoke(
                    registry,
                    new object[] { overseer });
            }
            catch (Exception ex)
            {
                Exception actual =
                    ex is TargetInvocationException invocation
                    && invocation.InnerException != null
                        ? invocation.InnerException
                        : ex;

                Log.ErrorOnce(
                    "[MAP-机械族机械师] 恢复复活目标固定额度失败：" + actual,
                    ReflectionFailureLogKey + 2);
            }
        }

        private static Pawn? ResolveCurrentOverseer(
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn target)
        {
            Pawn? externalOverseer = DataProcessingOverseerResolver.GetAllocationOverseer(target);
            if (IsActive(externalOverseer)
                && registry.IsValidAllocationPairForList(
                    externalOverseer,
                    target))
            {
                return externalOverseer;
            }

            if (IsActive(target)
                && registry.IsValidAllocationPairForList(target, target))
            {
                return target;
            }

            return null;
        }

        private static bool IsActive(Pawn? pawn)
        {
            return pawn != null
                && !pawn.Dead
                && !pawn.Destroyed
                && !pawn.Discarded
                && pawn.health?.isBeingKilled != true;
        }

        private static List<DataProcessingDynamicTargetRecord>? GetDynamicTargetRecords(
            GameComponent_DataProcessingAllocationRegistry registry)
        {
            return DynamicTargetRecordsField?.GetValue(registry)
                as List<DataProcessingDynamicTargetRecord>;
        }

        private static void EnsureCurrentGame()
        {
            if (ReferenceEquals(queuedGame, Current.Game))
            {
                return;
            }

            Clear();
            queuedGame = Current.Game;
        }

        private static void Clear()
        {
            PendingPawns.Clear();
            queuedGame = null;
            lastFallbackScanTick = -FallbackScanIntervalTicks;
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
