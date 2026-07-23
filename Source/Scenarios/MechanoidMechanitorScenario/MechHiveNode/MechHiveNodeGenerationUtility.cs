using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 机械巢节点的生成、位置选择与统计查询。
    /// 选址：每座殖民地仅一次有界 20 格预计算（记录实际可通行距离），再从缓存筛选候选，
    /// 绝不调用 TryFindPassableTileWithTraversalDistance，候选验证也不启动世界寻路。
    /// </summary>
    public static class MechHiveNodeGenerationUtility
    {
        public const int ColonyProximityTiles = 20;

        public const int MaxNodesPerColony = 8;

        public const int MinNodeSpacingTiles = 3;

        private const int PreferredMinDistTiles = 6;

        private const int PreferredMaxDistTiles = 16;

        private const int FallbackMinDistTiles = 1;

        private const int FallbackMaxDistTiles = 5;

        private const int MinDemandSilverValue = 1000;

        private const int MaxDemandSilverValue = 10000;

        /// <summary>六边形网格距离 d 内理论格数 3d(d+1)+1，再留余量作为 FloodFill 安全上限。</summary>
        private static int MaxTilesForProximitySearch =>
            3 * ColonyProximityTiles * (ColonyProximityTiles + 1) + 1 + 64;

        private sealed class ColonyProximityCache
        {
            public PlanetTile ColonyTile;

            /// <summary>殖民地 20 格可通行范围内的地块 → 相对该殖民地的实际可通行距离。</summary>
            public Dictionary<PlanetTile, int> TileDistances = new Dictionary<PlanetTile, int>();

            public int ExistingUncleanedNodeCount;
        }

        public static void GetAllNodes(List<MAPMechHiveNode> dest)
        {
            dest.Clear();
            List<WorldObject> objects = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < objects.Count; i++)
            {
                if (objects[i] is MAPMechHiveNode node)
                {
                    dest.Add(node);
                }
            }
        }

        public static int CountNodesNearColony(PlanetTile colonyTile)
        {
            List<MAPMechHiveNode> nodes = new List<MAPMechHiveNode>();
            GetAllNodes(nodes);
            int count = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (!nodes[i].Cleaned
                    && IsWithinTraversal(colonyTile, nodes[i].Tile, ColonyProximityTiles))
                {
                    count++;
                }
            }

            return count;
        }

        public static void GetCompletedUncleanedNodesNearColony(
            PlanetTile colonyTile,
            List<MAPMechHiveNode> dest)
        {
            dest.Clear();
            List<MAPMechHiveNode> nodes = new List<MAPMechHiveNode>();
            GetAllNodes(nodes);
            for (int i = 0; i < nodes.Count; i++)
            {
                MAPMechHiveNode node = nodes[i];
                if (node.IsCompleted
                    && !node.Cleaned
                    && IsWithinTraversal(colonyTile, node.Tile, ColonyProximityTiles))
                {
                    dest.Add(node);
                }
            }
        }

        public static bool TryRunGenerationAttempt()
        {
            try
            {
                Faction? mechHive = MechHiveNodeRelationUtility.GetMechHive();
                if (mechHive == null)
                {
                    return false;
                }

                List<Settlement> colonies = GetPlayerSurfaceColonies();
                if (colonies.Count == 0)
                {
                    return false;
                }

                List<MAPMechHiveNode> existing = new List<MAPMechHiveNode>();
                GetAllNodes(existing);

                // 预计算必须在候选筛选之前完成；每座殖民地单独有界 FloodFill，串行执行。
                List<ColonyProximityCache> caches = BuildColonyProximityCaches(colonies, existing);

                Settlement colony = colonies.RandomElement();
                PlanetTile colonyTile = colony.Tile;
                ColonyProximityCache? targetCache = FindCache(caches, colonyTile);
                if (targetCache == null
                    || targetCache.ExistingUncleanedNodeCount >= MaxNodesPerColony)
                {
                    return false;
                }

                if (!TryFindNodeTile(targetCache, existing, caches, out PlanetTile tile))
                {
                    return false;
                }

                CreateNode(tile, mechHive);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning("[MAP] 机械巢节点生成尝试异常: " + ex);
                return false;
            }
        }

        private static List<ColonyProximityCache> BuildColonyProximityCaches(
            List<Settlement> colonies,
            List<MAPMechHiveNode> existing)
        {
            List<ColonyProximityCache> caches = new List<ColonyProximityCache>(colonies.Count);
            for (int i = 0; i < colonies.Count; i++)
            {
                Settlement colony = colonies[i];
                if (colony == null || !colony.Tile.Valid)
                {
                    continue;
                }

                // 上一座殖民地的 FloodFill 必须已完全结束，才能开始下一座。
                ColonyProximityCache cache = new ColonyProximityCache
                {
                    ColonyTile = colony.Tile
                };
                CollectTilesWithinTraversal(colony.Tile, ColonyProximityTiles, cache.TileDistances);

                int count = 0;
                for (int n = 0; n < existing.Count; n++)
                {
                    MAPMechHiveNode node = existing[n];
                    if (node == null || node.Cleaned)
                    {
                        continue;
                    }

                    if (cache.TileDistances.ContainsKey(node.Tile))
                    {
                        count++;
                    }
                }

                cache.ExistingUncleanedNodeCount = count;
                caches.Add(cache);
            }

            return caches;
        }

        /// <summary>
        /// 单次有界世界 FloodFill：只收集 maxDist 内可通行地块并记录距离。
        /// 超出 maxDist 立即结束，不向更远扩展；不得在另一 FloodFill 进行中调用。
        /// </summary>
        private static void CollectTilesWithinTraversal(
            PlanetTile root,
            int maxDist,
            Dictionary<PlanetTile, int> dest)
        {
            dest.Clear();
            if (!root.Valid || root.Layer?.Filler == null)
            {
                return;
            }

            root.Layer.Filler.FloodFill(
                root,
                x => !Find.World.Impassable(x),
                (PlanetTile tile, int traversalDistance) =>
                {
                    if (traversalDistance > maxDist)
                    {
                        // BFS：所有 ≤maxDist 的地块已处理完毕，结束本次填充，禁止继续扩展。
                        return true;
                    }

                    dest[tile] = traversalDistance;
                    return false;
                },
                MaxTilesForProximitySearch);
        }

        private static ColonyProximityCache? FindCache(
            List<ColonyProximityCache> caches,
            PlanetTile colonyTile)
        {
            for (int i = 0; i < caches.Count; i++)
            {
                if (caches[i].ColonyTile == colonyTile)
                {
                    return caches[i];
                }
            }

            return null;
        }

        /// <summary>
        /// 从目标殖民地缓存筛选：优先 6～16，全部无合法候选时才用 1～5。
        /// </summary>
        private static bool TryFindNodeTile(
            ColonyProximityCache targetCache,
            List<MAPMechHiveNode> existing,
            List<ColonyProximityCache> caches,
            out PlanetTile tile)
        {
            if (TryPickTileFromCache(
                    targetCache,
                    PreferredMinDistTiles,
                    PreferredMaxDistTiles,
                    existing,
                    caches,
                    out tile))
            {
                return true;
            }

            return TryPickTileFromCache(
                targetCache,
                FallbackMinDistTiles,
                FallbackMaxDistTiles,
                existing,
                caches,
                out tile);
        }

        private static bool TryPickTileFromCache(
            ColonyProximityCache targetCache,
            int minDist,
            int maxDist,
            List<MAPMechHiveNode> existing,
            List<ColonyProximityCache> caches,
            out PlanetTile tile)
        {
            tile = PlanetTile.Invalid;
            List<PlanetTile> candidates = new List<PlanetTile>();
            foreach (KeyValuePair<PlanetTile, int> pair in targetCache.TileDistances)
            {
                if (pair.Value < minDist || pair.Value > maxDist)
                {
                    continue;
                }

                if (ValidateNodeTile(pair.Key, existing, caches))
                {
                    candidates.Add(pair.Key);
                }
            }

            if (candidates.Count == 0)
            {
                return false;
            }

            tile = candidates.RandomElement();
            return true;
        }

        /// <summary>
        /// 候选验证：仅读取预计算结果与廉价检查，严禁启动任何世界 FloodFill / 寻路。
        /// </summary>
        private static bool ValidateNodeTile(
            PlanetTile tile,
            List<MAPMechHiveNode> existing,
            List<ColonyProximityCache> caches)
        {
            if (!TileFinder.IsValidTileForNewSettlement(tile))
            {
                return false;
            }

            if (Find.WorldObjects.AnyWorldObjectAt(tile))
            {
                return false;
            }

            for (int i = 0; i < existing.Count; i++)
            {
                // ApproxDistanceInTiles 为廉价近似距离，不启动世界寻路。
                if (Find.WorldGrid.ApproxDistanceInTiles(tile, existing[i].Tile) < MinNodeSpacingTiles)
                {
                    return false;
                }
            }

            // 重叠范围分别检查：候选属于某殖民地 20 格缓存时，该殖民地未清理节点 +1 不得超过上限。
            for (int i = 0; i < caches.Count; i++)
            {
                ColonyProximityCache cache = caches[i];
                if (!cache.TileDistances.ContainsKey(tile))
                {
                    continue;
                }

                if (cache.ExistingUncleanedNodeCount + 1 > MaxNodesPerColony)
                {
                    return false;
                }
            }

            return true;
        }

        private static MAPMechHiveNode CreateNode(PlanetTile tile, Faction mechHive)
        {
            MAPMechHiveNode node =
                (MAPMechHiveNode)WorldObjectMaker.MakeWorldObject(MechHiveNodeDefOf.MAP_MechHiveNode);
            node.Tile = tile;
            if (node.def.canHaveFaction)
            {
                node.SetFaction(mechHive);
            }

            SitePart part = new SitePart(
                node,
                MechHiveNodeDefOf.MAP_MechHiveNode_Building,
                new SitePartParams());
            node.AddPart(part);

            ThingDef demandDef = ChooseDemandMaterial();
            int initialCount = ComputeInitialDemandCount(demandDef);
            node.InitializeNewNode(
                Find.TickManager.TicksGame,
                Rand.Int,
                demandDef,
                initialCount);

            Find.WorldObjects.Add(node);
            node.SendCreationLetter();
            return node;
        }

        private static ThingDef ChooseDemandMaterial()
        {
            switch (Rand.RangeInclusive(0, 2))
            {
                case 0:
                    return ThingDefOf.Steel;
                case 1:
                    return ThingDefOf.ComponentIndustrial;
                default:
                    return ThingDefOf.Plasteel;
            }
        }

        private static int ComputeInitialDemandCount(ThingDef demandDef)
        {
            float marketValue = demandDef.BaseMarketValue;
            if (marketValue <= 0f)
            {
                return 1;
            }

            int targetValue = Rand.RangeInclusive(MinDemandSilverValue, MaxDemandSilverValue);
            return Mathf.Max(1, Mathf.CeilToInt(targetValue / marketValue));
        }

        private static List<Settlement> GetPlayerSurfaceColonies()
        {
            List<Settlement> result = new List<Settlement>();
            Faction? player = Faction.OfPlayerSilentFail;
            if (player == null)
            {
                return result;
            }

            List<Settlement> settlements = Find.WorldObjects.Settlements;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement settlement = settlements[i];
                if (settlement == null || settlement.Faction != player)
                {
                    continue;
                }

                if (settlement.Tile.LayerDef != PlanetLayerDefOf.Surface)
                {
                    continue;
                }

                result.Add(settlement);
            }

            return result;
        }

        /// <summary>可通行世界路径距离是否在上限内（不可达返回 false）。勿在候选 FloodFill 回调中调用。</summary>
        public static bool IsWithinTraversal(PlanetTile a, PlanetTile b, int maxDist)
        {
            if (!a.Valid || !b.Valid)
            {
                return false;
            }

            if (a == b)
            {
                return true;
            }

            int dist = Find.WorldGrid.TraversalDistanceBetween(
                a,
                b,
                passImpassable: false,
                maxDist);
            return dist <= maxDist;
        }
    }
}
