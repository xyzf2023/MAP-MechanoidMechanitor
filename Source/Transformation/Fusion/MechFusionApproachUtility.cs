using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 合体快速接近的集中调参。20 格按实际步行路径长度判断，
    /// 8 格是落点搜索半径，5 格是最小飞行收益，避免为了一两格差距
    /// 执行完整升降动画。
    /// </summary>
    internal static class MechFusionApproachTuning
    {
        internal const int MaximumGroundPathSteps = 20;
        internal const int LandingSearchRadius = 8;
        internal const int MinimumFlightStepSaving = 5;
    }

    /// <summary>
    /// 一次合体接近的规划结果。只存在于 Job 运行期，不写入存档；
    /// 读档后重新规划即可。
    /// </summary>
    internal sealed class MechFusionApproachPlan
    {
        internal bool GroundReachable;
        internal int GroundPathSteps;
        internal IntVec3 GroundApproachCell = IntVec3.Invalid;
        internal bool UseFlight;
        internal IntVec3 LandingCell = IntVec3.Invalid;
        internal int LandingPathSteps;
    }

    /// <summary>
    /// 普通接近路径的权威计算入口：负责寻找目标人类周围的合法相邻格，
    /// 并按真实寻路路径长度选出最短方案。所有 PawnPath 都在这里申请并释放，
    /// 规划只允许在玩家下达合体命令或方案明显失效时执行。
    /// </summary>
    internal static class MechFusionApproachUtility
    {
        /// <summary>
        /// 生成完整接近方案：普通步行路径 A，以及是否建议快速飞行与最佳落点。
        /// 返回 false 表示普通路线不可达且没有可用的飞行方案。
        /// </summary>
        internal static bool TryPlan(
            Pawn? source,
            Pawn? wearer,
            out MechFusionApproachPlan plan)
        {
            plan = new MechFusionApproachPlan();
            if (source?.Map == null
                || wearer?.Map == null
                || wearer.Map != source.Map
                || !wearer.Spawned)
            {
                return false;
            }

            plan.GroundReachable = TryFindGroundApproach(
                source,
                wearer,
                out int groundSteps,
                out IntVec3 groundCell);
            plan.GroundPathSteps = groundSteps;
            plan.GroundApproachCell = groundCell;

            if (plan.GroundReachable
                && groundSteps
                    <= MechFusionApproachTuning.MaximumGroundPathSteps)
            {
                // A <= 20：强制普通步行，绝不触发快速飞行。
                return true;
            }

            if (!MechFusionRelocationUtility.CanSourceUseFusionFlight(source))
            {
                return plan.GroundReachable;
            }

            if (!MechFusionRelocationUtility.TryFindBestLandingCell(
                    source,
                    wearer,
                    out IntVec3 landingCell,
                    out int landingSteps))
            {
                return plan.GroundReachable;
            }

            if (plan.GroundReachable
                && landingSteps + MechFusionApproachTuning.MinimumFlightStepSaving
                    > groundSteps)
            {
                // 收益不足：保留普通步行方案。
                return true;
            }

            plan.UseFlight = true;
            plan.LandingCell = landingCell;
            plan.LandingPathSteps = landingSteps;
            return true;
        }

        /// <summary>
        /// 计算 source 到 wearer 周围所有合法相邻格的实际步行路径，
        /// 返回其中最短的路径步数与对应交互格。
        /// </summary>
        internal static bool TryFindGroundApproach(
            Pawn source,
            Pawn wearer,
            out int bestSteps,
            out IntVec3 bestCell)
        {
            bestSteps = 0;
            bestCell = IntVec3.Invalid;
            Map? map = source.Map;
            if (map == null || wearer.Map != map)
            {
                return false;
            }

            int best = int.MaxValue;
            IntVec3 cell = IntVec3.Invalid;
            for (int i = 0; i < GenAdj.AdjacentCells.Length; i++)
            {
                IntVec3 candidate = wearer.Position + GenAdj.AdjacentCells[i];
                if (!candidate.InBounds(map)
                    || !candidate.WalkableBy(map, source))
                {
                    continue;
                }

                if (!TryComputeCurrentPawnPathSteps(
                        source,
                        new LocalTargetInfo(candidate),
                        PathEndMode.OnCell,
                        out int steps))
                {
                    continue;
                }

                if (!cell.IsValid || steps < best)
                {
                    best = steps;
                    cell = candidate;
                }
            }

            if (!cell.IsValid)
            {
                return false;
            }

            bestSteps = best;
            bestCell = cell;
            return true;
        }

        /// <summary>
        /// 从 source 当前真实位置计算一次路径并返回实际路径步数（节点数 - 1）。
        /// PawnPath 必定在这里释放，调用方永远不会拿到路径对象。
        /// </summary>
        internal static bool TryComputeCurrentPawnPathSteps(
            Pawn source,
            LocalTargetInfo target,
            PathEndMode pathEndMode,
            out int steps)
        {
            steps = 0;
            Map? map = source.Map;
            if (map == null || !source.Spawned)
            {
                return false;
            }

            PawnPath path = map.pathFinder.FindPathNow(
                source.Position,
                target,
                source,
                null,
                pathEndMode);
            try
            {
                if (!path.Found)
                {
                    return false;
                }

                steps = Mathf.Max(0, path.NodesReversed.Count - 1);
                return true;
            }
            finally
            {
                path.Dispose();
            }
        }

        /// <summary>
        /// 以“假设 source 真的站在 start”为前提计算实际步行路径步数。
        /// 不能使用 Pawn 形式的 FindPathNow：PathRequest.ValidateInt 在
        /// TraverseMode.ByPawn 下会先执行 pawn.CanReach（基于 Pawn 当前真实
        /// 位置），导致“假想落点可达、当前真实位置不可达”的 B 路径被错误拒绝。
        /// 这里先用同一套 ByPawn 规则做以 start 为权威的可达性预检，再用携带
        /// 相同 source 通行属性的 PassDoors TraverseParms 取得路径，最后按
        /// ByPawn 的门与围栏规则逐格复核，保证与真实地面移动一致。
        /// </summary>
        internal static bool TryComputeHypotheticalPathSteps(
            Pawn source,
            IntVec3 start,
            LocalTargetInfo target,
            PathEndMode pathEndMode,
            out int steps)
        {
            steps = 0;
            Map? map = source.Map;
            if (map == null || !start.InBounds(map) || !target.IsValid)
            {
                return false;
            }

            bool canBashDoors = source.CurJob?.canBashDoors == true;
            bool canBashFences = source.CurJob?.canBashFences == true;
            TraverseParms byPawnParms = TraverseParms.For(
                source,
                Danger.Deadly,
                TraverseMode.ByPawn,
                canBashDoors,
                alwaysUseAvoidGrid: false,
                canBashFences: canBashFences);

            // 以假想 start 为权威、完全按 ByPawn 规则判断可达性。
            if (!map.reachability.CanReach(
                    start,
                    target,
                    pathEndMode,
                    byPawnParms))
            {
                return false;
            }

            // PathRequest.ValidateInt 在非 ByPawn 模式下才会使用传入 start
            // 做可达性预检；其他通行属性与 ByPawn 完全一致。
            TraverseParms hypotheticalParms = byPawnParms;
            hypotheticalParms.mode = TraverseMode.PassDoors;

            PawnPath path = map.pathFinder.FindPathNow(
                start,
                target,
                hypotheticalParms,
                null,
                pathEndMode);
            try
            {
                if (!path.Found
                    || !IsHypotheticalPathPassableForPawn(
                        path,
                        source,
                        byPawnParms))
                {
                    return false;
                }

                steps = Mathf.Max(0, path.NodesReversed.Count - 1);
                return true;
            }
            finally
            {
                path.Dispose();
            }
        }

        /// <summary>
        /// 复核 PassDoors 规划路径在 ByPawn 规则下是否真的可通行：
        /// 只补齐 PassDoors 与 ByPawn 存在差异的门与围栏规则，
        /// 不复制其他寻路规则。
        /// </summary>
        private static bool IsHypotheticalPathPassableForPawn(
            PawnPath path,
            Pawn source,
            TraverseParms byPawnParms)
        {
            Map? map = source.Map;
            if (map == null)
            {
                return false;
            }

            List<IntVec3> nodes = path.NodesReversed;
            for (int i = 0; i < nodes.Count; i++)
            {
                IntVec3 cell = nodes[i];
                Building? edifice = cell.GetEdifice(map);
                if (edifice is Building_Door door)
                {
                    if (!byPawnParms.canBashDoors
                        && door.IsForbiddenToPass(source))
                    {
                        return false;
                    }

                    if (door.PawnCanOpen(source) && !door.FreePassage)
                    {
                        continue;
                    }

                    if (door.CanPhysicallyPass(source))
                    {
                        continue;
                    }

                    if (byPawnParms.canBashDoors)
                    {
                        continue;
                    }

                    return false;
                }

                if (byPawnParms.fenceBlocked
                    && !byPawnParms.canBashFences
                    && edifice?.def.building?.isFence == true)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
