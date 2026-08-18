using System;
using System.Collections.Generic;
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

        // 在 CanFireNowSub 作用域期间，记录当前正在评估的 IncidentParms，
        // 使得 fallback 找到的 cell 能直接缓存进同一个 parms，
        // 避免“资格检查找到一次、真正执行又重新随机一次”的不一致。
        [ThreadStatic]
        private static IncidentParms? standardInfestationCanFireParms;

        public static bool InStandardInfestationCanFireScope =>
            standardInfestationCanFireScopeDepth > 0;

        public static IncidentParms? StandardInfestationCanFireParms =>
            InStandardInfestationCanFireScope
                ? standardInfestationCanFireParms
                : null;

        public static void BeginStandardInfestationCanFireScope(
            IncidentParms parms)
        {
            standardInfestationCanFireScopeDepth++;

            // 只在第一层进入时记录 parms，嵌套调用不直接覆盖。
            if (standardInfestationCanFireScopeDepth == 1)
            {
                standardInfestationCanFireParms = parms;
            }
        }

        public static void EndStandardInfestationCanFireScope()
        {
            if (standardInfestationCanFireScopeDepth > 0)
            {
                standardInfestationCanFireScopeDepth--;
            }

            // 防止负数，并在作用域完全退出时清空 parms（异常安全由调用方 Finalizer 保证）。
            if (standardInfestationCanFireScopeDepth <= 0)
            {
                standardInfestationCanFireScopeDepth = 0;
                standardInfestationCanFireParms = null;
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
        /// 是否应该由本模块接管一次标准虫灾的 TryExecute 准备/拦截。
        /// 仅当：非任务虫灾 + 当前允许无厚岩顶 fallback（即 Pursuit 模式 + 设置开启）。
        /// 其他模式（Default / Ally / PermanentNeutral）、Quest，以及设置关闭时一律返回 false，
        /// TryExecuteWorker Prefix 对此直接放行，不改变原版行为。
        /// </summary>
        public static bool ShouldManageStandardInfestationExecution(
            IncidentParms parms)
        {
            if (parms == null)
            {
                return false;
            }

            // 任务型虫灾：保留原版行为，不被追杀 fallback 改变。
            if (parms.quest != null)
            {
                return false;
            }

            return CanUseNoThickRoofFallback();
        }

        /// <summary>
        /// Pursuit Manager 自己触发标准虫灾的 authoritative 准备方法。
        /// 同时解决：原版虫族 faction 是否存在、原版 30 Hive 上限、
        /// vanilla-first、fallback、并把 fallback cell 直接写入 parms.infestationLocOverride。
        ///
        /// 资格检查与实际执行使用同一次解析，避免二次随机导致
        /// “检查成功但执行时第二次找不到位置、最终 IncidentWorker 返回 true 却没有实际生成”。
        ///
        /// 返回 true 表示 parms 已准备好（或无需 override）可以执行；
        /// 返回 false 表示不满足原版虫灾自身资格，不应触发。
        /// </summary>
        public static bool TryPrepareManagedInfestation(
            Map map,
            IncidentParms parms)
        {
            if (map == null || parms == null)
            {
                return false;
            }

            // 原版虫灾自身资格 1：虫族派系必须存在。
            if (Faction.OfInsects == null)
            {
                return false;
            }

            // 原版虫灾自身资格 2：已有 Hive 数量必须小于 30。
            if (HiveUtility.TotalSpawnedHivesCount(map) >= 30)
            {
                return false;
            }

            // 已有 override（例如 Manager 已写入或 CanFireNowSub 已缓存）：直接复用，不重新找。
            if (parms.infestationLocOverride != null)
            {
                return true;
            }

            // 原版优先：此处调用在 CanFireNowSub 作用域之外，postfix 不会改写结果，不递归。
            if (InfestationCellFinder.TryFindCell(out _, map))
            {
                return true;
            }

            // 原版失败以后才尝试一次 fallback。
            if (!CanUseNoThickRoofFallback())
            {
                return false;
            }

            if (TryFindFallbackCell(map, out IntVec3 fallbackCell))
            {
                parms.infestationLocOverride = fallbackCell;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 为即将执行的 IncidentWorker_Infestation 准备 parms.infestationLocOverride。
        /// 返回 true 表示可以继续原版 TryExecuteWorker；false 表示当前既无 vanilla cell
        /// 也无法获得 fallback，不应继续执行（原版 TryExecuteWorker 即使无实际 Tunnel 也可能 return true）。
        ///
        /// 只有 Pursuit + fallback 设置 ON + 非 quest 标准 Infestation 才会走到“失败阻止”分支；
        /// 其他模式与设置关闭时一律返回 true，不改变原版行为。
        /// </summary>
        public static bool PrepareInfestationParmsForExecution(
            Map map,
            IncidentParms parms)
        {
            if (parms == null || map == null)
            {
                return true;
            }

            // 任务型虫灾：保留原版行为，不被追杀 fallback 改变。
            if (parms.quest != null)
            {
                return true;
            }

            if (!CanUseNoThickRoofFallback())
            {
                return true;
            }

            // 已有 override（例如 CanFireNowSub 作用域中已缓存，或 Manager 已写入）：
            // 直接复用，不重新找 cell。
            if (parms.infestationLocOverride != null)
            {
                return true;
            }

            // 原版优先：此处调用在 CanFireNowSub 作用域之外，postfix 不会改写结果，不递归。
            if (InfestationCellFinder.TryFindCell(out _, map))
            {
                return true;
            }

            // 原版失败：只尝试一次 fallback。
            if (TryFindFallbackCell(map, out IntVec3 fallbackCell))
            {
                parms.infestationLocOverride = fallbackCell;
                return true;
            }

            // fallback 也失败：当前 Pursuit fallback 路径不应让 SpawnTunnels 空执行。
            return false;
        }

        /// <summary>
        /// 在玩家殖民地实际活动范围附近寻找一个适合 TunnelHiveSpawner 的备用格。
        /// 只以玩家 faction 的人工建筑为 anchor（排除遗迹 / 敌方建筑 / 中立建筑 / 任务建筑），
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

            List<Thing> allBuildings = map.listerThings.ThingsInGroup(
                ThingRequestGroup.BuildingArtificial);

            // 只保留玩家 faction 的建筑作为殖民地 anchor。
            List<Thing> playerBuildings = new List<Thing>(allBuildings.Count);
            for (int i = 0; i < allBuildings.Count; i++)
            {
                Thing building = allBuildings[i];
                if (building.Faction == Faction.OfPlayer)
                {
                    playerBuildings.Add(building);
                }
            }

            if (playerBuildings.Count == 0)
            {
                // 没有玩家建筑：不 fallback 到全部世界建筑。
                return false;
            }

            int attempts = 0;
            int maxAttempts = 80;
            while (attempts < maxAttempts)
            {
                attempts++;
                Thing anchor = playerBuildings[Rand.Range(0, playerBuildings.Count)];
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
