using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 普通派系前哨的自然生成、关系类别权重抽取、世界选址与统计查询。
    /// 权重只在创建新前哨的这一刻读取；创建后节点只保留真实 Faction。
    /// </summary>
    public static class FactionOutpostGenerationUtility
    {
        public const int ColonyProximityTiles = 20;
        public const int MaxOutpostsPerColony = 8;
        public const int MinOutpostSpacingTiles = 3;

        private const int PreferredMinDistTiles = 6;
        private const int PreferredMaxDistTiles = 16;
        private const int FallbackMinDistTiles = 1;
        private const int FallbackMaxDistTiles = 5;

        private static int MaxTilesForProximitySearch =>
            3 * ColonyProximityTiles * (ColonyProximityTiles + 1) + 1 + 64;

        private sealed class ColonyProximityCache
        {
            public PlanetTile ColonyTile;
            public readonly Dictionary<PlanetTile, int> TileDistances =
                new Dictionary<PlanetTile, int>();
            public int ExistingUncleanedOutpostCount;
        }

        public static void GetAllOutposts(List<MAPFactionOutpost> dest)
        {
            dest.Clear();
            List<WorldObject> objects = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < objects.Count; i++)
            {
                if (objects[i] is MAPFactionOutpost outpost)
                {
                    dest.Add(outpost);
                }
            }
        }

        public static void GetCompletedUncleanedOutpostsNearColony(
            PlanetTile colonyTile,
            List<MAPFactionOutpost> dest)
        {
            dest.Clear();
            List<MAPFactionOutpost> all = new List<MAPFactionOutpost>();
            GetAllOutposts(all);
            for (int i = 0; i < all.Count; i++)
            {
                MAPFactionOutpost outpost = all[i];
                if (outpost.IsCompleted
                    && !outpost.Cleaned
                    && IsWithinTraversal(colonyTile, outpost.Tile, ColonyProximityTiles))
                {
                    dest.Add(outpost);
                }
            }
        }

        public static bool TryRunGenerationAttempt()
        {
            try
            {
                MechanoidMechanitorStoryConfiguration? configuration =
                    GameComponent_MechanoidMechanitorStoryState.CurrentConfiguration;
                if (configuration == null || !configuration.factionOutpostFrequency.IsEnabled())
                {
                    return false;
                }

                if (!TrySelectFaction(configuration, out Faction? faction) || faction == null)
                {
                    return false;
                }

                List<Settlement> colonies = GetPlayerSurfaceColonies();
                if (colonies.Count == 0)
                {
                    return false;
                }

                List<MAPFactionOutpost> existing = new List<MAPFactionOutpost>();
                GetAllOutposts(existing);
                List<ColonyProximityCache> caches = BuildColonyProximityCaches(colonies, existing);

                List<ColonyProximityCache> availableCaches = new List<ColonyProximityCache>();
                for (int i = 0; i < caches.Count; i++)
                {
                    if (caches[i].ExistingUncleanedOutpostCount < MaxOutpostsPerColony)
                    {
                        availableCaches.Add(caches[i]);
                    }
                }

                if (availableCaches.Count == 0)
                {
                    return false;
                }

                ColonyProximityCache targetCache = availableCaches.RandomElement();
                if (!TryFindOutpostTile(targetCache, existing, caches, out PlanetTile tile))
                {
                    return false;
                }

                CreateOutpost(tile, faction);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning("[MAP] 普通派系前哨生成尝试异常: " + ex);
                return false;
            }
        }

        /// <summary>
        /// 两阶段抽取：先按实时关系分池并使用三个用户权重抽关系类别，再在该池内等权选 Faction。
        /// 空池的有效权重临时视为 0；三个有效权重总和为 0 时安全失败。
        /// </summary>
        public static bool TrySelectFaction(
            MechanoidMechanitorStoryConfiguration configuration,
            out Faction? selected)
        {
            selected = null;
            if (configuration == null)
            {
                return false;
            }

            List<Faction> eligible = new List<Faction>();
            FactionOutpostFactionUtility.CollectEligibleFactions(eligible);
            if (eligible.Count == 0)
            {
                return false;
            }

            List<Faction> hostile = new List<Faction>();
            List<Faction> ally = new List<Faction>();
            List<Faction> neutral = new List<Faction>();
            for (int i = 0; i < eligible.Count; i++)
            {
                Faction faction = eligible[i];
                switch (faction.PlayerRelationKind)
                {
                    case FactionRelationKind.Hostile:
                        hostile.Add(faction);
                        break;
                    case FactionRelationKind.Ally:
                        ally.Add(faction);
                        break;
                    case FactionRelationKind.Neutral:
                        neutral.Add(faction);
                        break;
                }
            }

            int hostileWeight = hostile.Count > 0
                ? Mathf.Max(0, configuration.hostileFactionOutpostWeight)
                : 0;
            int allyWeight = ally.Count > 0
                ? Mathf.Max(0, configuration.allyFactionOutpostWeight)
                : 0;
            int neutralWeight = neutral.Count > 0
                ? Mathf.Max(0, configuration.neutralFactionOutpostWeight)
                : 0;
            int totalWeight = hostileWeight + allyWeight + neutralWeight;
            if (totalWeight <= 0)
            {
                return false;
            }

            int roll = Rand.Range(0, totalWeight);
            if (roll < hostileWeight)
            {
                selected = hostile.RandomElement();
                return true;
            }

            roll -= hostileWeight;
            if (roll < allyWeight)
            {
                selected = ally.RandomElement();
                return true;
            }

            roll -= allyWeight;
            if (neutralWeight > 0 && roll < neutralWeight)
            {
                selected = neutral.RandomElement();
                return true;
            }

            return false;
        }

        private static List<ColonyProximityCache> BuildColonyProximityCaches(
            List<Settlement> colonies,
            List<MAPFactionOutpost> existing)
        {
            List<ColonyProximityCache> caches = new List<ColonyProximityCache>(colonies.Count);
            for (int i = 0; i < colonies.Count; i++)
            {
                Settlement colony = colonies[i];
                if (colony == null || !colony.Tile.Valid)
                {
                    continue;
                }

                ColonyProximityCache cache = new ColonyProximityCache
                {
                    ColonyTile = colony.Tile
                };
                CollectTilesWithinTraversal(colony.Tile, ColonyProximityTiles, cache.TileDistances);

                int count = 0;
                for (int n = 0; n < existing.Count; n++)
                {
                    MAPFactionOutpost outpost = existing[n];
                    if (outpost != null
                        && !outpost.Cleaned
                        && cache.TileDistances.ContainsKey(outpost.Tile))
                    {
                        count++;
                    }
                }

                cache.ExistingUncleanedOutpostCount = count;
                caches.Add(cache);
            }

            return caches;
        }

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
                        return true;
                    }

                    dest[tile] = traversalDistance;
                    return false;
                },
                MaxTilesForProximitySearch);
        }

        private static bool TryFindOutpostTile(
            ColonyProximityCache targetCache,
            List<MAPFactionOutpost> existing,
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
            List<MAPFactionOutpost> existing,
            List<ColonyProximityCache> caches,
            out PlanetTile tile)
        {
            tile = PlanetTile.Invalid;
            List<PlanetTile> candidates = new List<PlanetTile>();
            foreach (KeyValuePair<PlanetTile, int> pair in targetCache.TileDistances)
            {
                if (pair.Value >= minDist
                    && pair.Value <= maxDist
                    && ValidateOutpostTile(pair.Key, existing, caches))
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

        private static bool ValidateOutpostTile(
            PlanetTile tile,
            List<MAPFactionOutpost> existing,
            List<ColonyProximityCache> caches)
        {
            if (!TileFinder.IsValidTileForNewSettlement(tile)
                || Find.WorldObjects.AnyWorldObjectAt(tile))
            {
                return false;
            }

            for (int i = 0; i < existing.Count; i++)
            {
                if (Find.WorldGrid.ApproxDistanceInTiles(tile, existing[i].Tile)
                    < MinOutpostSpacingTiles)
                {
                    return false;
                }
            }

            for (int i = 0; i < caches.Count; i++)
            {
                ColonyProximityCache cache = caches[i];
                if (cache.TileDistances.ContainsKey(tile)
                    && cache.ExistingUncleanedOutpostCount + 1 > MaxOutpostsPerColony)
                {
                    return false;
                }
            }

            return true;
        }

        private static MAPFactionOutpost CreateOutpost(PlanetTile tile, Faction faction)
        {
            MAPFactionOutpost outpost =
                (MAPFactionOutpost)WorldObjectMaker.MakeWorldObject(
                    FactionOutpostDefOf.MAP_FactionOutpost);
            outpost.Tile = tile;
            if (outpost.def.canHaveFaction)
            {
                outpost.SetFaction(faction);
            }

            outpost.AddPart(new SitePart(
                outpost,
                FactionOutpostDefOf.MAP_FactionOutpost_Building,
                new SitePartParams()));
            outpost.InitializeNewOutpost(Find.TickManager.TicksGame, Rand.Int);
            Find.WorldObjects.Add(outpost);
            outpost.SendCreationLetter();
            return outpost;
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
                if (settlement != null
                    && settlement.Faction == player
                    && settlement.Tile.LayerDef == PlanetLayerDefOf.Surface)
                {
                    result.Add(settlement);
                }
            }

            return result;
        }

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
