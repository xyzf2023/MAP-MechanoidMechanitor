using System;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 合体假想落点路径的线程本地作用域。
    /// 只在本 MOD 同步调用 PathFinder.FindPathNow 期间生效：
    /// PathRequest.ValidateInt 在 TraverseMode.ByPawn 下会调用
    /// Pawn.CanReach（该原版重载基于 Pawn 当前真实位置），这里让该次
    /// 校验改用假想起点 start。其它时间、其它 Pawn 的任何寻路都不受影响。
    /// </summary>
    internal static class MechFusionHypotheticalPathContext
    {
        [ThreadStatic] private static int depth;
        [ThreadStatic] private static Pawn? contextPawn;
        [ThreadStatic] private static IntVec3 contextStart = IntVec3.Invalid;

        internal static bool TryGetStart(Pawn? pawn, out IntVec3 start)
        {
            start = IntVec3.Invalid;
            if (depth <= 0
                || pawn == null
                || !ReferenceEquals(pawn, contextPawn))
            {
                return false;
            }

            start = contextStart;
            return start.IsValid;
        }

        internal static IDisposable Begin(Pawn pawn, IntVec3 start)
        {
            return new Scope(pawn, start);
        }

        private sealed class Scope : IDisposable
        {
            private readonly Pawn? previousPawn;
            private readonly IntVec3 previousStart;

            internal Scope(Pawn pawn, IntVec3 start)
            {
                previousPawn = contextPawn;
                previousStart = contextStart;
                contextPawn = pawn;
                contextStart = start;
                depth++;
            }

            public void Dispose()
            {
                contextPawn = previousPawn;
                contextStart = previousStart;
                depth = Math.Max(0, depth - 1);
            }
        }
    }

    /// <summary>
    /// 仅服务于合体假想落点路径：
    /// 当假想路径上下文激活且 Pawn 匹配时，把
    /// ReachabilityUtility.CanReach(Pawn, ...) 的“当前位置”重载
    /// 重定向到同名的 start 权威重载，其它情况一律走原版。
    /// </summary>
    [HarmonyPatch(
        typeof(ReachabilityUtility),
        nameof(ReachabilityUtility.CanReach),
        new Type[]
        {
            typeof(Pawn),
            typeof(LocalTargetInfo),
            typeof(PathEndMode),
            typeof(Danger),
            typeof(bool),
            typeof(bool),
            typeof(TraverseMode)
        })]
    internal static class MechFusionHypotheticalReachabilityPatch
    {
        public static bool Prefix(
            Pawn pawn,
            LocalTargetInfo dest,
            PathEndMode peMode,
            Danger maxDanger,
            bool canBashDoors,
            bool canBashFences,
            TraverseMode mode,
            ref bool __result)
        {
            if (!MechFusionHypotheticalPathContext.TryGetStart(
                    pawn,
                    out IntVec3 start))
            {
                return true;
            }

            // 原版公开的 start 重载：通行规则与 source 的 ByPawn 完全一致。
            __result = pawn.CanReach(
                start,
                dest,
                peMode,
                maxDanger,
                canBashDoors,
                canBashFences,
                mode);
            return false;
        }
    }
}
