using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 合体假想落点路径的线程本地作用域。
    /// 只在本 MOD 同步调用 PathFinder.FindPathNow 期间生效：
    /// PathRequest.ValidateInt 的 ByPawn 起点可达性校验会经由专用路由方法，
    /// 在本作用域内改用假想起点 start；其它 PathRequest 仍保持原版行为。
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

        /// <summary>
        /// 只替代 PathRequest.ValidateInt 内部原本的 Pawn.CanReach(dest, ...) 调用。
        /// 常规路径请求直接转发原版；只有当前线程处于合体假想路径作用域，且 Pawn
        /// 与上下文一致时，才调用原版带显式 start 的重载。
        /// </summary>
        internal static bool CanReachForPathValidation(
            Pawn pawn,
            LocalTargetInfo dest,
            PathEndMode peMode,
            Danger maxDanger,
            bool canBashDoors,
            bool canBashFences,
            TraverseMode mode)
        {
            if (TryGetStart(pawn, out IntVec3 start))
            {
                return pawn.CanReach(
                    start,
                    dest,
                    peMode,
                    maxDanger,
                    canBashDoors,
                    canBashFences,
                    mode);
            }

            return pawn.CanReach(
                dest,
                peMode,
                maxDanger,
                canBashDoors,
                canBashFences,
                mode);
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
    /// 性能收窄补丁：不再给高频 ReachabilityUtility.CanReach 全局挂 Prefix。
    /// 仅在 Harmony 初始化时改写一次 PathRequest.ValidateInt，把其中唯一的
    /// Pawn.CanReach(dest, ...) 调用替换为本 MOD 的轻量路由方法。
    /// 因此普通 WorkGiver / JobGiver / AI 的 CanReach 热路径完全不经过本 MOD 补丁。
    /// </summary>
    [HarmonyPatch(typeof(PathRequest), "ValidateInt")]
    internal static class MechFusionHypotheticalPathValidationPatch
    {
        private static readonly MethodInfo? OriginalCanReach = AccessTools.Method(
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
            });

        private static readonly MethodInfo? RoutedCanReach = AccessTools.Method(
            typeof(MechFusionHypotheticalPathContext),
            nameof(MechFusionHypotheticalPathContext.CanReachForPathValidation));

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            bool replaced = false;
            foreach (CodeInstruction instruction in instructions)
            {
                if (OriginalCanReach != null
                    && RoutedCanReach != null
                    && instruction.Calls(OriginalCanReach))
                {
                    instruction.operand = RoutedCanReach;
                    replaced = true;
                }

                yield return instruction;
            }

            if (!replaced)
            {
                Log.Error(
                    "[MAP-机械族机械师] 未能定位 PathRequest.ValidateInt 中的 ByPawn CanReach 调用；合体假想路径起点兼容补丁未生效。");
            }
        }
    }
}
