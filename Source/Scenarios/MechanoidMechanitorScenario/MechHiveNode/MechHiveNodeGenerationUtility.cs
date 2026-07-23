using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 机械巢节点的生成、位置选择与统计查询。所有世界寻路/统计仅在生成、每日检查、
    /// 袭击/援军判定等必要时机调用，不进入逐 tick 热路径。
    /// </summary>
    public static class MechHiveNodeGenerationUtility
    {
        /// <summary>节点与殖民地/彼此的可通行距离上限（20 格）。</summary>
        public const int ColonyProximityTiles = 20;

        /// <summary>每座殖民地范围内节点数量上限。</summary>
        public const int MaxNodesPerColony = 8;

        /// <summary>节点之间最小间隔（近似格数）。</summary>
        public const int MinNodeSpacingTiles = 3;

        private const int GenerationMinDistTiles = 6;

        private const int GenerationMaxDistTiles = 16;

        private const int MinDemandSilverValue = 1000;

        private const int MaxDemandSilverValue = 10000;

        /// <summary>收集当前所有机械巢节点世界对象。</summary>
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

        /// <summary>殖民地 20 格可通行范围内的机械巢节点总数（建设中 + 完整）。</summary>
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

        /// <summary>
        /// 收集某殖民地 20 格可通行范围内、已建设完成且未清理的节点（用于额外袭击与援军统计）。
        /// </summary>
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

        /// <summary>
        /// 执行一次生成尝试。无论成功与否都由调用方在其后重新安排间隔，此处不负责计时。
        /// </summary>
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

                Settlement colony = colonies.RandomElement();
                PlanetTile colonyTile = colony.Tile;

                if (CountNodesNearColony(colonyTile) >= MaxNodesPerColony)
                {
                    return false;
                }

                if (!TryFindNodeTile(colonyTile, out PlanetTile tile))
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

        private static bool TryFindNodeTile(PlanetTile colonyTile, out PlanetTile tile)
        {
            List<MAPMechHiveNode> existing = new List<MAPMechHiveNode>();
            GetAllNodes(existing);

            return TileFinder.TryFindPassableTileWithTraversalDistance(
                colonyTile,
                GenerationMinDistTiles,
                GenerationMaxDistTiles,
                out tile,
                t => ValidateNodeTile(t, existing),
                ignoreFirstTilePassability: false,
                TileFinderMode.Random,
                canTraverseImpassable: false,
                exitOnFirstTileFound: false);
        }

        private static bool ValidateNodeTile(PlanetTile tile, List<MAPMechHiveNode> existing)
        {
            if (!TileFinder.IsValidTileForNewSettlement(tile))
            {
                return false;
            }

            if (Find.WorldObjects.AnyWorldObjectAt(tile))
            {
                return false;
            }

            // 与其他节点至少间隔 3 格（近似格距，避免逐候选世界寻路）。
            for (int i = 0; i < existing.Count; i++)
            {
                if (Find.WorldGrid.ApproxDistanceInTiles(tile, existing[i].Tile) < MinNodeSpacingTiles)
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

        /// <summary>可通行世界路径距离是否在上限内（不可达返回 false）。</summary>
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
