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
    /// 选址时先预计算各殖民地 20 格范围，再在候选搜索回调中只读缓存，避免嵌套 FloodFill。
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

        private sealed class ColonyProximityCache
        {
            public PlanetTile ColonyTile;

            public HashSet<PlanetTile> TilesWithin20 = new HashSet<PlanetTile>();

            public int ExistingNodeCount;
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
                if (IsWithinTraversal(colonyTile, nodes[i].Tile, ColonyProximityTiles))
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

                // 预计算必须在候选搜索之前完成；每座殖民地单独 FloodFill，串行执行。
                List<ColonyProximityCache> caches = BuildColonyProximityCaches(colonies, existing);

                Settlement colony = colonies.RandomElement();
                PlanetTile colonyTile = colony.Tile;
                ColonyProximityCache? targetCache = FindCache(caches, colonyTile);
                if (targetCache == null || targetCache.ExistingNodeCount >= MaxNodesPerColony)
                {
                    return false;
                }

                if (!TryFindNodeTile(colonyTile, existing, caches, out PlanetTile tile))
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
                CollectTilesWithinTraversal(colony.Tile, ColonyProximityTiles, cache.TilesWithin20);

                int count = 0;
                for (int n = 0; n < existing.Count; n++)
                {
                    if (cache.TilesWithin20.Contains(existing[n].Tile))
                    {
                        count++;
                    }
                }

                cache.ExistingNodeCount = count;
                caches.Add(cache);
            }

            return caches;
        }

        /// <summary>
        /// 单次世界 FloodFill，收集 maxDist 内可通行地块。不得在另一 FloodFill 进行中调用。
        /// </summary>
        private static void CollectTilesWithinTraversal(
            PlanetTile root,
            int maxDist,
            HashSet<PlanetTile> dest)
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
                        // BFS：首次超出距离时，更近地块已收集完毕，结束本次填充。
                        return true;
                    }

                    dest.Add(tile);
                    return false;
                });
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

        private static bool TryFindNodeTile(
            PlanetTile colonyTile,
            List<MAPMechHiveNode> existing,
            List<ColonyProximityCache> caches,
            out PlanetTile tile)
        {
            // 首选 6～16；仅当完全没有合法候选时才搜索 1～5。
            if (TileFinder.TryFindPassableTileWithTraversalDistance(
                    colonyTile,
                    PreferredMinDistTiles,
                    PreferredMaxDistTiles,
                    out tile,
                    t => ValidateNodeTile(t, existing, caches),
                    ignoreFirstTilePassability: false,
                    TileFinderMode.Random,
                    canTraverseImpassable: false,
                    exitOnFirstTileFound: false))
            {
                return true;
            }

            return TileFinder.TryFindPassableTileWithTraversalDistance(
                colonyTile,
                FallbackMinDistTiles,
                FallbackMaxDistTiles,
                out tile,
                t => ValidateNodeTile(t, existing, caches),
                ignoreFirstTilePassability: false,
                TileFinderMode.Random,
                canTraverseImpassable: false,
                exitOnFirstTileFound: false);
        }

        /// <summary>
        /// 候选验证：仅读取预计算结果与廉价检查，严禁启动任何世界 FloodFill。
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
                if (Find.WorldGrid.ApproxDistanceInTiles(tile, existing[i].Tile) < MinNodeSpacingTiles)
                {
                    return false;
                }
            }

            // 重叠范围分别检查：候选属于某殖民地 20 格缓存时，该殖民地 +1 不得超过上限。
            for (int i = 0; i < caches.Count; i++)
            {
                ColonyProximityCache cache = caches[i];
                if (!cache.TilesWithin20.Contains(tile))
                {
                    continue;
                }

                if (cache.ExistingNodeCount + 1 > MaxNodesPerColony)
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
