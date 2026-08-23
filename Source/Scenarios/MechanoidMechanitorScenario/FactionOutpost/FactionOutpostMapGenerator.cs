using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 普通派系前哨的军事初始化器。
    /// 建筑主体由与文化 DLC Work Site 相同的 GenStep_Outpost -> BaseGen 流程生成；
    /// 本类只验证前哨布局、按真实所属派系生成 Combat 守军，并建立基地防御 Lord。
    /// 守军仍按真实建筑群分散落地，但与文化 DLC Work Site 一致：
    /// 所有守军共用一个 LordJob_DefendBase，并只生成在可经门正常到达地图边缘的格子。
    /// </summary>
    public static class FactionOutpostMapGenerator
    {
        private const int MaxPawnPlaceTries = 120;
        private const int MaxDefendCenterSearchTries = 240;
        private const int DefendCenterSearchRadius = 24;

        // 建筑群邻近分组阈值：两栋建筑在 x / z 方向上均不超过该距离视为可连通同一群。
        private const int GroupThreshold = 12;

        public static void Generate(Map map, MAPFactionOutpost outpost)
        {
            if (map == null || outpost == null)
            {
                return;
            }

            outpost.NotifyMapGarrisonInitialized(false);
            Faction? owner = outpost.Faction;
            if (owner == null || !FactionOutpostFactionUtility.IsEligibleFaction(owner))
            {
                Log.Warning("[MAP] 普通派系前哨缺少可用所属派系，拒绝初始化地图内容。");
                return;
            }

            List<Pawn> generatedPawns = new List<Pawn>();
            List<Pawn> placedPawns = new List<Pawn>();
            List<Lord> lords = new List<Lord>();

            // 计划分配表：只表示「该 Pawn 第一遍应尝试在哪个建筑群附近落地」。
            Dictionary<BuildingCluster, List<Pawn>> plannedClusterPawns =
                new Dictionary<BuildingCluster, List<Pawn>>();
            // 实际落地表：只表示「该 Pawn 最终实际在哪个建筑群附近成功落地」。
            Dictionary<BuildingCluster, List<Pawn>> landedClusterPawns =
                new Dictionary<BuildingCluster, List<Pawn>>();
            List<BuildingCluster> clusters = new List<BuildingCluster>();

            Rand.PushState(Gen.HashCombineInt(outpost.LayoutSeed, 0x46A271));
            try
            {
                if (!TryResolveBuildingClusters(map, owner, clusters))
                {
                    throw new InvalidOperationException(
                        "GenStep_Outpost 未生成可识别的所属派系建筑，无法确定前哨防御中心。");
                }

                if (!TryGenerateCombatPawns(
                        owner,
                        map,
                        outpost.GarrisonThreatPoints,
                        generatedPawns)
                    || generatedPawns.Count == 0)
                {
                    throw new InvalidOperationException("所属派系没有生成任何有效 Combat 守军。");
                }

                // 每个建筑群初始化计划表与实际落地表的空列表。
                foreach (BuildingCluster cluster in clusters)
                {
                    plannedClusterPawns[cluster] = new List<Pawn>();
                    landedClusterPawns[cluster] = new List<Pawn>();
                }

                // 将守军分配到各建筑群（写入计划表；在 PushState 作用域内，保证同一 LayoutSeed 下稳定）。
                AllocatePawnsToClusters(generatedPawns, clusters, plannedClusterPawns);

                // 第一遍：按预定建筑群尝试落地。只写入实际落地表，不在第一遍修改计划表。
                List<Pawn> unplaced = new List<Pawn>();
                foreach (KeyValuePair<BuildingCluster, List<Pawn>> kvp in plannedClusterPawns)
                {
                    BuildingCluster cluster = kvp.Key;
                    foreach (Pawn pawn in kvp.Value)
                    {
                        if (TryPlacePawnNearCluster(map, cluster, pawn))
                        {
                            landedClusterPawns[cluster].Add(pawn);
                            placedPawns.Add(pawn);
                        }
                        else
                        {
                            unplaced.Add(pawn);
                        }
                    }
                }

                // 第二遍：未落地 Pawn 尝试其他建筑群，尽量不浪费守军；全部失败才安全丢弃。
                // 成功只写入实际落地表，绝不再写入计划表。
                foreach (Pawn pawn in unplaced)
                {
                    BuildingCluster? landed = TryPlacePawnOnAnyCluster(map, clusters, pawn);
                    if (landed != null)
                    {
                        landedClusterPawns[landed].Add(pawn);
                        placedPawns.Add(pawn);
                    }
                    else
                    {
                        MechHiveCombatPawnUtility.SafelyDiscardPawn(pawn);
                    }
                }

                if (placedPawns.Count == 0)
                {
                    throw new InvalidOperationException("所有生成守军均无法在前哨附近落地。");
                }

                // 创建 Lord 前校验：每个实际落地 Pawn 只能属于一个建筑群、且确实在地图上。
                ValidateLandedPawnAssignments(map, landedClusterPawns);

                // 与文化 DLC Work Site 的 GenStep_WorkSitePawns 保持一致：
                // 所有守军共用一个 LordJob_DefendBase，以地图中心为防守中心，
                // 25000 tick 后必定由原版状态机从守卫基地转入主动进攻。
                // 建筑群只决定 Pawn 的初始分散落地位置，不再拆成多个互不联动的 Lord。
                Lord lord = LordMaker.MakeNewLord(
                    owner,
                    new LordJob_MAPFactionOutpostDefendBase(owner, map.Center, 25000),
                    map);
                if (lord == null)
                {
                    throw new InvalidOperationException("前哨守军 Lord 创建失败。");
                }

                // 先登记 Lord 再逐个加入 Pawn；若任一步骤抛出异常，现有回滚路径
                // 能同时移除该 Lord 并安全丢弃本轮生成的全部守军。
                lords.Add(lord);
                for (int i = 0; i < placedPawns.Count; i++)
                {
                    lord.AddPawn(placedPawns[i]);
                }

                outpost.NotifyMapGarrisonInitialized(true);

                if (Prefs.DevMode)
                {
                    LogDevGenerationSummary(outpost, clusters, landedClusterPawns);
                }
            }
            catch (Exception ex)
            {
                Log.Error("[MAP] 普通派系前哨地图初始化失败，已回滚本轮守军: " + ex);
                CleanupFailedGeneration(map, lords, generatedPawns);
                outpost.NotifyMapGarrisonInitialized(false);
            }
            finally
            {
                Rand.PopState();
            }
        }

        /// <summary>
        /// 一个真实生成的所属派系建筑群，包含其成员建筑、防守中心与守军分配用评分。
        /// </summary>
        private sealed class BuildingCluster
        {
            public readonly List<Building> Buildings = new List<Building>();

            public IntVec3 Center;
            public int Score;
        }

        /// <summary>
        /// 使用原版 BaseGen 实际生成的所属派系建筑，按真正的连通关系分组为多个建筑群。
        /// 采用从任一未处理建筑出发、吸收所有通过传递邻近关系可达建筑的连通分量算法，
        /// 避免 A 接近 B、B 接近 C 时被错误拆成多个群。
        /// 每个有效群计算中心并存入 result（按 Score 从高到低排序，并列时稳定排序）。
        /// 至少需有一个有效建筑群，否则返回 false 触发现有回滚。
        /// </summary>
        private static bool TryResolveBuildingClusters(
            Map map,
            Faction owner,
            List<BuildingCluster> result)
        {
            result.Clear();

            List<Building> owned = new List<Building>();
            foreach (Building building in map.listerThings.GetThingsOfType<Building>())
            {
                if (building == null
                    || building.Destroyed
                    || !building.Spawned
                    || building.Faction != owner
                    || building.def?.building == null
                    || building.def.building.isNaturalRock)
                {
                    continue;
                }

                owned.Add(building);
            }

            if (owned.Count == 0)
            {
                return false;
            }

            // 连通分量分组：BFS 吸收所有相距不超过 GroupThreshold 且通过传递关系可达的建筑。
            bool[] visited = new bool[owned.Count];
            for (int start = 0; start < owned.Count; start++)
            {
                if (visited[start])
                {
                    continue;
                }

                List<Building> clusterBuildings = new List<Building>();
                Queue<int> queue = new Queue<int>();
                queue.Enqueue(start);
                visited[start] = true;
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    clusterBuildings.Add(owned[current]);
                    for (int other = 0; other < owned.Count; other++)
                    {
                        if (visited[other])
                        {
                            continue;
                        }

                        Building a = owned[current];
                        Building b = owned[other];
                        if (Mathf.Abs(a.Position.x - b.Position.x) <= GroupThreshold
                            && Mathf.Abs(a.Position.z - b.Position.z) <= GroupThreshold)
                        {
                            visited[other] = true;
                            queue.Enqueue(other);
                        }
                    }
                }

                BuildingCluster cluster = new BuildingCluster();
                foreach (Building b in clusterBuildings)
                {
                    cluster.Buildings.Add(b);
                }

                int score = clusterBuildings.Count;
                foreach (Building b in clusterBuildings)
                {
                    if (b.OccupiedRect().Area > 1)
                    {
                        score++;
                    }
                }

                cluster.Score = score < 1 ? 1 : score;

                if (!TryResolveClusterCenter(clusterBuildings, map, out IntVec3 center))
                {
                    continue;
                }

                cluster.Center = center;
                result.Add(cluster);
            }

            if (result.Count == 0)
            {
                return false;
            }

            result.Sort((a, b) =>
            {
                int cmp = b.Score.CompareTo(a.Score);
                if (cmp != 0)
                {
                    return cmp;
                }

                cmp = b.Buildings.Count.CompareTo(a.Buildings.Count);
                if (cmp != 0)
                {
                    return cmp;
                }

                return (a.Center.x + a.Center.z).CompareTo(b.Center.x + b.Center.z);
            });

            return true;
        }

        /// <summary>
        /// 计算一个建筑群的中心：先取成员建筑平均位置，可站立则直接使用；
        /// 否则在该位置附近寻找可站立格。无法找到有效中心时返回 false。
        /// </summary>
        private static bool TryResolveClusterCenter(
            List<Building> buildings,
            Map map,
            out IntVec3 center)
        {
            center = IntVec3.Invalid;

            long sumX = 0;
            long sumZ = 0;
            foreach (Building b in buildings)
            {
                sumX += b.Position.x;
                sumZ += b.Position.z;
            }

            IntVec3 approximateCenter = new IntVec3(
                (int)(sumX / buildings.Count),
                0,
                (int)(sumZ / buildings.Count));

            if (IsValidDefendCenterCell(approximateCenter, map))
            {
                center = approximateCenter;
                return true;
            }

            return CellFinder.TryFindRandomCellNear(
                approximateCenter,
                map,
                DefendCenterSearchRadius,
                c => IsValidDefendCenterCell(c, map),
                out center,
                MaxDefendCenterSearchTries);
        }

        /// <summary>
        /// 将守军分配到各建筑群。
        /// - 仅一个群：全部 Pawn 分配给它。
        /// - Pawn 数 &gt;= 群数：每群先分 1 名，剩余按 Score 权重分配。
        /// - Pawn 数 &lt; 群数：分给 Score 最高的若干群，不额外生成 Pawn。
        /// </summary>
        private static void AllocatePawnsToClusters(
            List<Pawn> pawns,
            List<BuildingCluster> clusters,
            Dictionary<BuildingCluster, List<Pawn>> result)
        {
            result.Clear();
            foreach (BuildingCluster cluster in clusters)
            {
                result[cluster] = new List<Pawn>();
            }

            if (clusters.Count == 0 || pawns.Count == 0)
            {
                return;
            }

            if (clusters.Count == 1)
            {
                result[clusters[0]].AddRange(pawns);
                return;
            }

            if (pawns.Count >= clusters.Count)
            {
                // 每个建筑群先分配 1 名 Pawn。
                for (int i = 0; i < clusters.Count; i++)
                {
                    result[clusters[i]].Add(pawns[i]);
                }

                // 剩余 Pawn 按各群 Score 的权重分配。
                int remaining = pawns.Count - clusters.Count;
                int totalScore = 0;
                foreach (BuildingCluster cluster in clusters)
                {
                    totalScore += cluster.Score;
                }

                for (int r = 0; r < remaining; r++)
                {
                    BuildingCluster target = PickWeightedCluster(clusters, totalScore);
                    result[target].Add(pawns[clusters.Count + r]);
                }
            }
            else
            {
                // 守军不足以覆盖全部建筑群：分给 Score 最高的若干群。
                for (int i = 0; i < pawns.Count; i++)
                {
                    result[clusters[i]].Add(pawns[i]);
                }
            }
        }

        /// <summary>
        /// 校验实际落地表：每个 Pawn 只能属于一个建筑群、且确实已在地图上 Spawn。
        /// 任意 Pawn 为 null / 已销毁 / 未 Spawn / 不在当前 map / 被重复加入时，
        /// 抛出 InvalidOperationException，交由 Generate 的 catch 走回滚路径，
        /// 绝不把同一 Pawn 重复加入统一守军 Lord。正常成功路径不受影响。
        /// </summary>
        private static void ValidateLandedPawnAssignments(
            Map map,
            Dictionary<BuildingCluster, List<Pawn>> landedClusterPawns)
        {
            HashSet<Pawn> seen = new HashSet<Pawn>();
            foreach (KeyValuePair<BuildingCluster, List<Pawn>> kvp in landedClusterPawns)
            {
                foreach (Pawn pawn in kvp.Value)
                {
                    if (pawn == null
                        || pawn.Destroyed
                        || !pawn.Spawned
                        || pawn.Map != map
                        || !seen.Add(pawn))
                    {
                        throw new InvalidOperationException(
                            "前哨守军落地分配存在重复或无效 Pawn，拒绝创建 Lord。");
                    }
                }
            }
        }

        private static BuildingCluster PickWeightedCluster(
            List<BuildingCluster> clusters,
            int totalScore)
        {
            if (totalScore <= 0)
            {
                return clusters.RandomElement();
            }

            int roll = Rand.Range(0, totalScore);
            int acc = 0;
            foreach (BuildingCluster cluster in clusters)
            {
                acc += cluster.Score;
                if (roll < acc)
                {
                    return cluster;
                }
            }

            return clusters[clusters.Count - 1];
        }

        private static bool IsValidDefendCenterCell(IntVec3 cell, Map map)
        {
            return cell.InBounds(map)
                && cell.Standable(map)
                && cell.GetEdifice(map) == null;
        }

        /// <summary>
        /// 与文化 DLC Work Site 的 singlePawnSpawnCellExtraPredicate 一致：
        /// 守军可以生成在建筑内，但必须能以正常通过门的方式到达地图边缘，
        /// 防止落入真正密封的房间或封闭院落。
        /// </summary>
        private static bool IsValidDefenderSpawnCell(IntVec3 cell, Map map)
        {
            return IsValidDefendCenterCell(cell, map)
                && map.reachability.CanReachMapEdge(
                    cell,
                    TraverseParms.For(TraverseMode.PassDoors));
        }

        /// <summary>
        /// 在该建筑群附近落地：先随机选一栋建筑作锚点在其周围 8 格寻找，
        /// 找不到再在该群 Center 周围 16 格寻找。
        /// </summary>
        private static bool TryPlacePawnNearCluster(Map map, BuildingCluster cluster, Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed)
            {
                return false;
            }

            if (cluster.Buildings.Count > 0
                && TryPlacePawnNear(map, cluster.Buildings.RandomElement().Position, pawn, 8))
            {
                return true;
            }

            if (cluster.Center.IsValid
                && TryPlacePawnNear(map, cluster.Center, pawn, 16))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 在任一建筑群附近落地（用于第一遍失败后的兜底，尽量不浪费守军）。
        /// </summary>
        private static BuildingCluster? TryPlacePawnOnAnyCluster(
            Map map,
            List<BuildingCluster> clusters,
            Pawn pawn)
        {
            for (int i = 0; i < clusters.Count; i++)
            {
                if (TryPlacePawnNearCluster(map, clusters[i], pawn))
                {
                    return clusters[i];
                }
            }

            return null;
        }

        private static bool TryPlacePawnNear(Map map, IntVec3 origin, Pawn pawn, int radius)
        {
            if (!CellFinder.TryFindRandomCellNear(
                origin,
                map,
                radius,
                c => IsValidDefenderSpawnCell(c, map),
                out IntVec3 cell,
                MaxPawnPlaceTries))
            {
                return false;
            }

            try
            {
                Thing? spawned = GenSpawn.Spawn(pawn, cell, map, Rot4.Random);
                return ReferenceEquals(spawned, pawn) && pawn.Spawned && pawn.Map == map;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGenerateCombatPawns(
            Faction owner,
            Map map,
            int points,
            List<Pawn> result)
        {
            result.Clear();
            IncidentParms parms = new IncidentParms
            {
                target = map,
                points = points,
                faction = owner,
                pawnGroupKind = PawnGroupKindDefOf.Combat,
                raidStrategy = RaidStrategyDefOf.ImmediateAttack,
                raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn,
                canKidnap = false,
                canSteal = false,
                canTimeoutOrFlee = false
            };

            PawnGroupMakerParms groupParms = IncidentParmsUtility.GetDefaultPawnGroupMakerParms(
                PawnGroupKindDefOf.Combat,
                parms,
                ensureCanGenerateAtLeastOnePawn: true);
            if (groupParms == null)
            {
                return false;
            }

            foreach (Pawn pawn in PawnGroupMakerUtility.GeneratePawns(
                groupParms,
                warnOnZeroResults: false))
            {
                if (pawn != null && !pawn.Destroyed)
                {
                    result.Add(pawn);
                }
            }

            return result.Count > 0;
        }

        /// <summary>
        /// 回滚本轮生成：移除所有本轮创建的 Lord（不影响其他 MOD / 原版 / 联合行动创建的 Lord），
        /// 并安全丢弃全部本轮生成的 Pawn（含已成功 Spawn 的）。
        /// </summary>
        private static void CleanupFailedGeneration(
            Map map,
            List<Lord> lords,
            List<Pawn> generated)
        {
            if (lords != null && map?.lordManager != null)
            {
                for (int i = 0; i < lords.Count; i++)
                {
                    Lord lord = lords[i];
                    if (lord == null)
                    {
                        continue;
                    }

                    try
                    {
                        map.lordManager.RemoveLord(lord);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning("[MAP] 回滚普通派系前哨 Lord 失败: " + ex);
                    }
                }
            }

            for (int i = 0; i < generated.Count; i++)
            {
                MechHiveCombatPawnUtility.SafelyDiscardPawn(generated[i]);
            }
        }

        /// <summary>
        /// DEV 专用诊断日志：仅在 DevMode 下、每次生成完成后记录一次，不污染玩家日志。
        /// </summary>
        private static void LogDevGenerationSummary(
            MAPFactionOutpost outpost,
            List<BuildingCluster> clusters,
            Dictionary<BuildingCluster, List<Pawn>> landedClusterPawns)
        {
            MechanoidMechanitorFactionOutpostPhase phase =
                outpost.IsCompleted
                    ? MechanoidMechanitorFactionOutpostPhase.Completed
                    : MechanoidMechanitorFactionOutpostPhase.Building;
            int expected = FactionOutpostLayoutUtility.GetExpectedClusterCount(
                phase,
                outpost.LayoutTier);

            StringBuilder sb = new StringBuilder();
            sb.Append("[MAP-FactionOutpost] ");
            sb.Append("Outpost=").Append(outpost.Label);
            sb.Append(", Phase=").Append(outpost.IsCompleted ? "Completed" : "Building");
            sb.Append(", GarrisonBudget=").Append(outpost.GarrisonThreatPoints);
            sb.Append(", LayoutTier=").Append(outpost.LayoutTier);
            sb.Append(", ExpectedClusters=").Append(expected);
            sb.Append(", DetectedClusters=").Append(clusters.Count);
            Log.Message(sb.ToString());

            for (int i = 0; i < clusters.Count; i++)
            {
                BuildingCluster cluster = clusters[i];
                int landed = landedClusterPawns.TryGetValue(cluster, out List<Pawn>? list) ? list.Count : 0;
                Log.Message(
                    "[MAP-FactionOutpost] Cluster[" + i + "]=Buildings:" + cluster.Buildings.Count
                    + ", Score:" + cluster.Score
                    + ", Pawns:" + landed
                    + ", Center:" + cluster.Center);
            }
        }
    }
}
