using System;
using System.Collections.Generic;
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
    /// </summary>
    public static class FactionOutpostMapGenerator
    {
        private const int MaxPawnPlaceTries = 120;
        private const int MaxDefendCenterSearchTries = 240;
        private const int DefendCenterSearchRadius = 24;

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
            Lord? lord = null;

            Rand.PushState(Gen.HashCombineInt(outpost.LayoutSeed, 0x46A271));
            try
            {
                if (!TryResolveDefendCenter(map, owner, out IntVec3 defendCenter))
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

                for (int i = 0; i < generatedPawns.Count; i++)
                {
                    Pawn pawn = generatedPawns[i];
                    if (TryPlacePawn(map, defendCenter, pawn))
                    {
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

                lord = LordMaker.MakeNewLord(
                    owner,
                    new LordJob_DefendBase(owner, defendCenter, 25000),
                    map);
                if (lord == null)
                {
                    throw new InvalidOperationException("无法创建前哨守军 Lord。");
                }

                for (int i = 0; i < placedPawns.Count; i++)
                {
                    lord.AddPawn(placedPawns[i]);
                }

                outpost.NotifyMapGarrisonInitialized(true);
            }
            catch (Exception ex)
            {
                Log.Error("[MAP] 普通派系前哨地图初始化失败，已回滚本轮守军: " + ex);
                CleanupFailedGeneration(map, lord, generatedPawns);
                outpost.NotifyMapGarrisonInitialized(false);
            }
            finally
            {
                Rand.PopState();
            }
        }

        /// <summary>
        /// 使用原版 BaseGen 实际生成的所属派系建筑求中心，而不是假定前哨位于 map.Center。
        /// 这样即使 GenStep_Outpost 将营地放在地图中心附近的其他清晰区域，守军也会围绕真实基地部署。
        /// 当存在多个建筑群时（如建成前哨的主/次级建筑群），优先选择最大建筑群的中心，
        /// 避免守军被分配到两个建筑群中间的空地从而失去 Lord 协调。
        /// </summary>
        private static bool TryResolveDefendCenter(
            Map map,
            Faction owner,
            out IntVec3 defendCenter)
        {
            defendCenter = IntVec3.Invalid;

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

            // 按建筑位置邻近关系分组：相距不超过 GroupThreshold 视为同一建筑群。
            const int GroupThreshold = 12;
            List<List<Building>> groups = new List<List<Building>>();
            foreach (Building building in owned)
            {
                bool placed = false;
                foreach (List<Building> group in groups)
                {
                    foreach (Building member in group)
                    {
                        if (Mathf.Abs(building.Position.x - member.Position.x) <= GroupThreshold
                            && Mathf.Abs(building.Position.z - member.Position.z) <= GroupThreshold)
                        {
                            group.Add(building);
                            placed = true;
                            break;
                        }
                    }

                    if (placed)
                    {
                        break;
                    }
                }

                if (!placed)
                {
                    groups.Add(new List<Building> { building });
                }
            }

            // 选择建筑数量最多（并列时占地面积最大）的组作为防守中心来源。
            List<Building> bestGroup = owned;
            int bestScore = -1;
            foreach (List<Building> group in groups)
            {
                int score = group.Count;
                foreach (Building member in group)
                {
                    score += member.OccupiedRect().Area > 1 ? 1 : 0;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestGroup = group;
                }
            }

            long sumX = 0;
            long sumZ = 0;
            foreach (Building building in bestGroup)
            {
                sumX += building.Position.x;
                sumZ += building.Position.z;
            }

            IntVec3 approximateCenter = new IntVec3(
                (int)(sumX / bestGroup.Count),
                0,
                (int)(sumZ / bestGroup.Count));

            if (IsValidDefenderCell(approximateCenter, map))
            {
                defendCenter = approximateCenter;
                return true;
            }

            return CellFinder.TryFindRandomCellNear(
                approximateCenter,
                map,
                DefendCenterSearchRadius,
                c => IsValidDefenderCell(c, map),
                out defendCenter,
                MaxDefendCenterSearchTries);
        }

        private static bool IsValidDefenderCell(IntVec3 cell, Map map)
        {
            return cell.InBounds(map)
                && cell.Standable(map)
                && cell.GetEdifice(map) == null;
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

        private static bool TryPlacePawn(Map map, IntVec3 center, Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed)
            {
                return false;
            }

            if (!CellFinder.TryFindRandomCellNear(
                center,
                map,
                22,
                c => IsValidDefenderCell(c, map),
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

        private static void CleanupFailedGeneration(
            Map map,
            Lord? lord,
            List<Pawn> generated)
        {
            if (lord != null && map?.lordManager != null)
            {
                try
                {
                    map.lordManager.RemoveLord(lord);
                }
                catch (Exception ex)
                {
                    Log.Warning("[MAP] 回滚普通派系前哨 Lord 失败: " + ex);
                }
            }

            for (int i = 0; i < generated.Count; i++)
            {
                MechHiveCombatPawnUtility.SafelyDiscardPawn(generated[i]);
            }
        }
    }
}
