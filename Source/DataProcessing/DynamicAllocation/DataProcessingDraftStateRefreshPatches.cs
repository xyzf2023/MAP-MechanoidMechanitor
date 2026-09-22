using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 监听征召状态的真实变化。只负责排队，不在 Drafted setter 内同步执行动态计划，
    /// 避免批量征召时对同一监管者重复进行完整预算重算。
    /// </summary>
    [HarmonyPatch(
        typeof(Pawn_DraftController),
        nameof(Pawn_DraftController.Drafted),
        MethodType.Setter)]
    internal static class DataProcessingDraftStateChangedPatch
    {
        private static void Prefix(
            Pawn_DraftController __instance,
            out bool __state)
        {
            __state = __instance != null && __instance.Drafted;
        }

        private static void Postfix(
            Pawn_DraftController __instance,
            bool __state)
        {
            Pawn? pawn = __instance?.pawn;
            if (pawn == null || pawn.Drafted == __state)
            {
                return;
            }

            DataProcessingDraftStateRefreshQueue.Enqueue(pawn);
        }
    }

    /// <summary>
    /// 在注册表每个游戏刻处理结束后刷新到期的征召变化。
    /// 队列把目标写入注册表既有的“下一次动态检查”字典，再复用 TickDynamicScheduler：
    /// 每个目标只评估一次，同一监管者在本轮只执行一次预算计划。
    /// </summary>
    [HarmonyPatch(
        typeof(GameComponent_DataProcessingAllocationRegistry),
        nameof(GameComponent_DataProcessingAllocationRegistry.GameComponentTick))]
    internal static class DataProcessingDraftStateRefreshTickPatch
    {
        private static void Postfix(
            GameComponent_DataProcessingAllocationRegistry __instance)
        {
            DataProcessingDraftStateRefreshQueue.Process(__instance);
        }
    }

    internal static class DataProcessingDraftStateRefreshQueue
    {
        private const int ReflectionFailureLogKey = 0x4D415252; // "MARR"

        private static readonly Dictionary<Pawn, int> PendingTargets =
            new Dictionary<Pawn, int>(ReferencePawnComparer.Instance);

        private static readonly FieldInfo? NextDynamicCheckTickField =
            AccessTools.Field(
                typeof(GameComponent_DataProcessingAllocationRegistry),
                "nextDynamicCheckTickByTarget");

        private static readonly MethodInfo? TickDynamicSchedulerMethod =
            AccessTools.Method(
                typeof(GameComponent_DataProcessingAllocationRegistry),
                "TickDynamicScheduler");

        private static Game? queuedGame;

        private static bool Enabled =>
            MAPMechanitorMod.Settings?.enableImmediateDraftStateRefresh ?? true;

        public static void Enqueue(Pawn? target)
        {
            if (!Enabled
                || target == null
                || target.Dead
                || target.Destroyed
                || !target.RaceProps.IsMechanoid
                || target.Faction == null
                || !target.Faction.IsPlayerSafe()
                || Current.Game == null
                || Find.TickManager == null)
            {
                return;
            }

            if (!ReferenceEquals(queuedGame, Current.Game))
            {
                Clear();
                queuedGame = Current.Game;
            }

            // 始终以最近一次真实切换为准，保证至少延迟到该切换后的下一游戏刻。
            PendingTargets[target] = Find.TickManager.TicksGame + 1;
        }

        public static void Process(
            GameComponent_DataProcessingAllocationRegistry? registry)
        {
            if (PendingTargets.Count == 0)
            {
                return;
            }

            if (!Enabled
                || registry == null
                || Current.Game == null
                || Find.TickManager == null
                || !ReferenceEquals(queuedGame, Current.Game))
            {
                Clear();
                return;
            }

            int now = Find.TickManager.TicksGame;
            List<Pawn> dueTargets = new List<Pawn>();
            foreach (KeyValuePair<Pawn, int> pair in PendingTargets)
            {
                if (pair.Value <= now)
                {
                    dueTargets.Add(pair.Key);
                }
            }

            if (dueTargets.Count == 0)
            {
                return;
            }

            for (int i = 0; i < dueTargets.Count; i++)
            {
                PendingTargets.Remove(dueTargets[i]);
            }

            Dictionary<Pawn, int>? nextChecks =
                NextDynamicCheckTickField?.GetValue(registry)
                    as Dictionary<Pawn, int>;
            if (nextChecks == null || TickDynamicSchedulerMethod == null)
            {
                Log.ErrorOnce(
                    "[MAP-机械族机械师] 无法访问动态分配调度器，征召即时刷新已跳过。",
                    ReflectionFailureLogKey);
                return;
            }

            bool anyScheduled = false;
            for (int i = 0; i < dueTargets.Count; i++)
            {
                Pawn target = dueTargets[i];
                if (!TryResolveEnabledDynamicConfig(
                        registry,
                        target,
                        out DataProcessingDynamicTargetRecord? config)
                    || config == null)
                {
                    continue;
                }

                nextChecks[target] = now;
                anyScheduled = true;
            }

            if (!anyScheduled)
            {
                return;
            }

            try
            {
                // 既有调度器会一次处理所有到期目标，并按监管者合并完整预算重算。
                TickDynamicSchedulerMethod.Invoke(registry, null);
            }
            catch (Exception ex)
            {
                Exception actual = ex is TargetInvocationException invocation
                    && invocation.InnerException != null
                        ? invocation.InnerException
                        : ex;
                Log.ErrorOnce(
                    "[MAP-机械族机械师] 征召状态即时刷新失败：" + actual,
                    ReflectionFailureLogKey + 1);
            }
        }

        private static bool TryResolveEnabledDynamicConfig(
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn? target,
            out DataProcessingDynamicTargetRecord? config)
        {
            config = null;
            if (target == null
                || target.Dead
                || target.Destroyed
                || !target.RaceProps.IsMechanoid
                || target.Faction == null
                || !target.Faction.IsPlayerSafe())
            {
                return false;
            }

            config = registry.GetDynamicTargetRecord(
                overseer: null,
                target: target);

            if (config == null)
            {
                Pawn? overseer = ResolveCurrentOverseer(registry, target);
                if (overseer == null
                    || !registry.IsDynamicAllocationEnabled(overseer)
                    || !registry.IsValidAllocationPairForList(overseer, target))
                {
                    return false;
                }

                config = registry.GetOrCreateDynamicTargetRecord(overseer, target);
            }

            return config.overseer != null
                && config.enabled
                && registry.IsDynamicAllocationEnabled(config.overseer)
                && registry.IsValidAllocationPairForList(config.overseer, target);
        }

        private static Pawn? ResolveCurrentOverseer(
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn target)
        {
            Pawn? externalOverseer = DataProcessingOverseerResolver.GetAllocationOverseer(target);
            if (externalOverseer != null
                && registry.IsValidAllocationPairForList(externalOverseer, target))
            {
                return externalOverseer;
            }

            if (target.mechanitor != null
                && MechanoidMechanitorCapabilityUtility.HasCapability(target, MechanoidMechanitorCapability.SelfDataProcessing)
                && registry.IsValidAllocationPairForList(target, target))
            {
                return target;
            }

            return null;
        }

        private static void Clear()
        {
            PendingTargets.Clear();
            queuedGame = null;
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
