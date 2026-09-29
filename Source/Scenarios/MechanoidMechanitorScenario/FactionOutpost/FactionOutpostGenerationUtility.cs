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
            public Settlement? SourceColony;
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

                return TryGenerateWithFaction(faction);
            }
            catch (Exception ex)
            {
                Log.Warning("[MAP-机械族机械师] 普通派系前哨生成尝试异常: " + ex);
                return false;
            }
        }

        /// <summary>
        /// DEV 指定关系生成：指定关系池非空时直接在该池内等权选派系，完全忽略三项生成权重；
        /// 指定池为空时回退普通权重抽取。之后仍复用完整的自然选址、数量和间距检查。
        /// </summary>
        public static bool TryRunGenerationAttemptForRelation(FactionRelationKind preferredRelation)
        {
            try
            {
                MechanoidMechanitorStoryConfiguration? configuration =
                    GameComponent_MechanoidMechanitorStoryState.CurrentConfiguration;
                if (configuration == null || !configuration.factionOutpostFrequency.IsEnabled())
                {
                    return false;
                }

                Faction? faction;
                if (!TrySelectFactionForRelation(preferredRelation, out faction) || faction == null)
                {
                    if (!TrySelectFaction(configuration, out faction) || faction == null)
                    {
                        return false;
                    }
                }

                return TryGenerateWithFaction(faction);
            }
            catch (Exception ex)
            {
                Log.Warning("[MAP-机械族机械师] 普通派系前哨指定关系生成尝试异常: " + ex);
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
            PartitionByRelation(eligible, hostile, ally, neutral);

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

        public static bool TrySelectFactionForRelation(
            FactionRelationKind relation,
            out Faction? selected)
        {
            selected = null;
            List<Faction> eligible = new List<Faction>();
            FactionOutpostFactionUtility.CollectEligibleFactions(eligible);
            if (eligible.Count == 0)
            {
                return false;
            }

            List<Faction> matching = new List<Faction>();
            for (int i = 0; i < eligible.Count; i++)
            {
                Faction faction = eligible[i];
                if (faction.PlayerRelationKind == relation)
                {
                    matching.Add(faction);
                }
            }

            if (matching.Count == 0)
            {
                return false;
            }

            selected = matching.RandomElement();
            return true;
        }

        private static void PartitionByRelation(
            List<Faction> eligible,
            List<Faction> hostile,
            List<Faction> ally,
            List<Faction> neutral)
        {
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
        }

        private static bool TryGenerateWithFaction(Faction faction)
        {
            if (!FactionOutpostFactionUtility.IsEligibleFaction(faction))
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

            Map? referenceMap = ResolveOutpostReferenceMap(targetCache.SourceColony, out bool fellBack);
            if (fellBack)
            {
                Log.Warning(
                    "[MAP-机械族机械师] 普通派系前哨来源殖民地地图未加载，回退使用其他玩家殖民地地图计算守军预算。");
            }

            CreateOutpost(tile, faction, referenceMap);
            return true;
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
                    ColonyTile = colony.Tile,
                    SourceColony = colony
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

        private static MAPFactionOutpost CreateOutpost(
            PlanetTile tile,
            Faction faction,
            Map? referenceMap)
        {
            MAPFactionOutpost outpost =
                (MAPFactionOutpost)WorldObjectMaker.MakeWorldObject(
                    FactionOutpostDefOf.MAP_FactionOutpost);
            outpost.Tile = tile;
            if (outpost.def.canHaveFaction)
            {
                outpost.SetFaction(faction);
            }

            outpost.InitializeNewOutpost(Find.TickManager.TicksGame, Rand.Int, referenceMap);
            Find.WorldObjects.Add(outpost);
            outpost.SendCreationLetter();
            return outpost;
        }

        /// <summary>
        /// 解析“用于挑选该前哨 tile 的来源玩家殖民地”地图，作为守军点数的参考地图。
        /// 优先使用来源殖民地自身地图；仅在确实未加载时才回退到其他已加载的玩家殖民地地图；
        /// 两者都不可用时返回 null，由计算工具使用旧默认值（并已在调用处告警）。
        /// </summary>
        private static Map? ResolveOutpostReferenceMap(
            Settlement? colony,
            out bool fellBackToOtherColony)
        {
            fellBackToOtherColony = false;

            Map? sourceMap = colony?.Map;
            if (sourceMap != null && !sourceMap.Disposed)
            {
                return sourceMap;
            }

            Map? homeMap = GetAnyLoadedPlayerHomeMap();
            if (homeMap != null)
            {
                fellBackToOtherColony = true;
                return homeMap;
            }

            return null;
        }

        private static Map? GetAnyLoadedPlayerHomeMap()
        {
            Faction? player = Faction.OfPlayerSilentFail;
            if (player == null)
            {
                return null;
            }

            List<Settlement> settlements = Find.WorldObjects.Settlements;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement settlement = settlements[i];
                if (settlement != null
                    && settlement.Faction == player
                    && settlement.Map != null
                    && !settlement.Map.Disposed)
                {
                    return settlement.Map;
                }
            }

            return null;
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

        /// <summary>
        /// DEV 专用：生成一个「建成」状态的普通派系前哨，并返回精确引用。
        /// 允许绕过自然生成概率与普通前哨数量上限；仍保留基本的合法 tile 校验与不可覆盖已有 WorldObject。
        /// 正式游戏路径不得调用。
        /// </summary>
        public static bool TryDevGenerateCompletedOutpost(
            Faction targetFaction,
            out MAPFactionOutpost outpost,
            out string message)
        {
            outpost = null!;
            message = string.Empty;

            if (!Prefs.DevMode)
            {
                message = "非开发者模式";
                return false;
            }

            if (targetFaction == null)
            {
                message = "目标派系为空";
                return false;
            }

            if (!FactionOutpostFactionUtility.IsEligibleFaction(targetFaction))
            {
                message = "目标派系不符合前哨生成资格";
                return false;
            }

            List<Settlement> colonies = GetPlayerSurfaceColonies();
            if (colonies.Count == 0)
            {
                message = "找不到玩家地表殖民地作为距离参考";
                return false;
            }

            List<MAPFactionOutpost> existing = new List<MAPFactionOutpost>();
            GetAllOutposts(existing);
            List<ColonyProximityCache> caches = BuildColonyProximityCaches(colonies, existing);

            // DEV 允许放宽到全部殖民地缓存（不强制 MaxOutpostsPerColony，但仍要求 tile 合法）。
            ColonyProximityCache? chosen = null;
            foreach (ColonyProximityCache cache in caches)
            {
                if (cache.TileDistances.Count > 0)
                {
                    chosen = cache;
                    break;
                }
            }

            if (chosen == null)
            {
                message = "找不到合法生成 tile";
                return false;
            }

            List<MAPFactionOutpost> tmpExisting = new List<MAPFactionOutpost>(existing);
            if (!TryFindOutpostTile(
                    chosen,
                    tmpExisting,
                    caches,
                    out PlanetTile tile))
            {
                message = "找不到合法生成 tile";
                return false;
            }

            MAPFactionOutpost created =
                (MAPFactionOutpost)WorldObjectMaker.MakeWorldObject(
                    FactionOutpostDefOf.MAP_FactionOutpost);
            created.Tile = tile;
            if (created.def.canHaveFaction)
            {
                created.SetFaction(targetFaction);
            }

            Map? referenceMap = ResolveOutpostReferenceMap(chosen.SourceColony, out bool fellBack);
            if (fellBack)
            {
                Log.Warning(
                    "[MAP-机械族机械师] DEV 生成前哨：来源殖民地地图未加载，回退使用其他玩家殖民地地图计算守军预算。");
            }

            created.InitializeNewOutpost(Find.TickManager.TicksGame, Rand.Int, referenceMap);
            created.DevForceCompleteConstructionForTest();

            Find.WorldObjects.Add(created);
            outpost = created;
            message = "已生成建成前哨 @ tile " + tile;
            return true;
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
