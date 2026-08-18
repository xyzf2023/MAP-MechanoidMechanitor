using System;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 追杀模式“允许无视厚岩顶”的标准虫灾 fallback 工具。
    ///
    /// 设计原则：VANILLA FIRST, FALLBACK SECOND。
    /// 原版 InfestationCellFinder.TryFindCell 必须首先完整运行；只有当原版失败，
    /// 且当前处于追杀模式并开启设置时，才在殖民地附近寻找不要求厚岩顶的备用位置。
    ///
    /// 作用域被严格限制在 IncidentWorker_Infestation.CanFireNowSub 执行期间，
    /// 因此 Quest / Jelly / DeepDrill / Wastepack / 其他独立 TryFindCell 调用不会受影响。
    /// </summary>
    public static class MechanoidMechanitorInsectPursuitInfestationUtility
    {
        [ThreadStatic]
        private static int standardInfestationCanFireScopeDepth;

        public static bool InStandardInfestationCanFireScope =>
            standardInfestationCanFireScopeDepth > 0;

        public static void BeginStandardInfestationCanFireScope()
        {
            standardInfestationCanFireScopeDepth++;
        }

        public static void EndStandardInfestationCanFireScope()
        {
            if (standardInfestationCanFireScopeDepth > 0)
            {
                standardInfestationCanFireScopeDepth--;
            }
        }

        /// <summary>
        /// 当前是否允许对标准虫灾使用无厚岩顶 fallback。
        /// 仅在追杀模式且设置开启时返回 true。
        /// </summary>
        public static bool CanUseNoThickRoofFallback()
        {
            if (!MechanoidMechanitorInsectPursuitUtility.IsPursuitActive())
            {
                return false;
            }

            if (MAPMechanitorMod.Settings == null
                || !MAPMechanitorMod.Settings.pursuitAllowInfestationWithoutThickRoof)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 为即将执行的 IncidentWorker_Infestation 准备 parms：
        /// 仅当原版 cell 不存在时，才把 infestationLocOverride 写入 fallback 位置。
        /// 有原版 cell 时完全不修改 parms，山地地图仍按原版选点。
        ///
        /// 注意：本调用内部的 InfestationCellFinder.TryFindCell 发生在 CanFireNowSub 作用域之外，
        /// 不会触发本模块的 fallback postfix，因此观察到的是原版真实结果（不递归、不误判）。
        ///
        /// 任务型虫灾（parms.quest != null）不应用 fallback，保留其原版独立生成资格。
        /// </summary>
        public static void PrepareInfestationParmsForExecution(
            Map map,
            IncidentParms parms)
        {
            if (parms == null || map == null)
            {
                return;
            }

            // 任务型虫灾：保留原版行为，不被追杀 fallback 改变。
            if (parms.quest != null)
            {
                return;
            }

            if (!CanUseNoThickRoofFallback())
            {
                return;
            }

            if (parms.infestationLocOverride != null)
            {
                return;
            }

            // 原版优先：此处调用在 CanFireNowSub 作用域之外，postfix 不会改写结果。
            if (InfestationCellFinder.TryFindCell(out _, map))
            {
                return;
            }

            if (TryFindFallbackCell(map, out IntVec3 fallbackCell))
            {
                parms.infestationLocOverride = fallbackCell;
            }
        }

        /// <summary>
        /// 在殖民地实际活动范围附近寻找一个适合 TunnelHiveSpawner 的备用格。
        /// 不要求 ThickRoof、不要求 mountainousness >= 0.17。
        /// 仅对事件资格/执行需要时调用，不每 tick 扫描大量地图格。
        /// </summary>
        public static bool TryFindFallbackCell(Map map, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;

            if (map == null)
            {
                return false;
            }

            // 优先以玩家建筑群作为 anchor，在附近一定半径内寻找合法格。
            var buildings = map.listerThings.ThingsInGroup(
                ThingRequestGroup.BuildingArtificial);
            if (buildings.Count == 0)
            {
                return false;
            }

            int attempts = 0;
            int maxAttempts = 80;
            while (attempts < maxAttempts)
            {
                attempts++;
                Thing anchor = buildings[Rand.Range(0, buildings.Count)];
                IntVec3 center = anchor.Position;

                int radius = Rand.RangeInclusive(4, 10);
                int samples = 10;
                for (int i = 0; i < samples; i++)
                {
                    IntVec3 candidate = center
                        + GenRadial.RadialPattern[
                            Rand.Range(0, GenRadial.NumCellsInRadius(radius))];

                    if (IsValidFallbackCell(candidate, map))
                    {
                        cell = candidate;
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsValidFallbackCell(IntVec3 cell, Map map)
        {
            if (!cell.InBounds(map))
            {
                return false;
            }

            if (!cell.Standable(map))
            {
                return false;
            }

            if (cell.Fogged(map))
            {
                return false;
            }

            if (cell.GetTemperature(map) < -17f)
            {
                return false;
            }

            var thingList = cell.GetThingList(map);
            for (int i = 0; i < thingList.Count; i++)
            {
                Thing thing = thingList[i];
                if (thing is Pawn)
                {
                    return false;
                }

                if (thing is Hive)
                {
                    return false;
                }

                if (thing is TunnelHiveSpawner)
                {
                    return false;
                }

                if (thing.def.category == ThingCategory.Building
                    && thing.def.passability == Traversability.Impassable
                    && GenSpawn.SpawningWipes(ThingDefOf.Hive, thing.def))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
