using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    internal static class MovementCostImmunityUtility
    {
        internal static bool HasImmunity(Pawn? pawn)
        {
            return MechanoidMechanitorCapabilityUtility.HasCapability(
                pawn, MechanoidMechanitorCapability.MovementCostImmunity);
        }

        // 仅替换实际步进耗时中的调用，不修改共享 PathGrid 或寻路缓存。
        internal static int CalculatedCostAt(
            PathGrid grid, IntVec3 cell, bool perceivedStatic, IntVec3 prevCell,
            int? baseCostOverride, Pawn pawn)
        {
            int original = grid.CalculatedCostAt(
                cell, perceivedStatic, prevCell, baseCostOverride);
            Map? map = pawn?.Map;
            if (!HasImmunity(pawn) || map == null || !cell.InBounds(map)
                || perceivedStatic || original >= 10000)
            {
                // 原版统一检查地形、物体、栅栏和当前飞行路径网格的不可通行条件。
                // perceivedStatic 包含避火等寻路权重，不属于本组件的过滤范围。
                return original;
            }

            // 原版对地形、物体、雪、沙取最大值，不能从最终值直接减去地形成本。
            // 统一移除地形（含水格覆盖值）、物体、雪沙和连续门格附加成本。
            // 能力来源不影响效果，同时拥有组件和机动作战状态也只计算一次。
            return 0;
        }

        internal static ushort PathWalkCostFor(Building building, Pawn pawn)
        {
            return HasImmunity(pawn) ? (ushort)0 : building.PathWalkCostFor(pawn);
        }

        internal static bool TryGetTerrainSpeedFactor(
            Dictionary<string, float> factors, string tag, out float factor, Pawn pawn)
        {
            bool found = factors.TryGetValue(tag, out factor);
            if (found && factor < 1f && HasImmunity(pawn))
            {
                // 只改本次查询结果；共享 PawnKindDef 字典不变，地形加速倍率保留。
                factor = 1f;
            }
            return found;
        }
    }
}
