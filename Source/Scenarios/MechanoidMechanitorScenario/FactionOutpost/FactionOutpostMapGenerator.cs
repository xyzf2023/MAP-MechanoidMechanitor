using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 普通派系前哨的轻量地图内容生成器：使用原派系 Combat PawnGroup 生成守军，
    /// 配合简单钢铁路障防御圈和 DefendPoint Lord。失败会清理本轮生成内容并拒绝进入。
    /// </summary>
    public static class FactionOutpostMapGenerator
    {
        private const int MaxPawnPlaceTries = 120;

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

            List<Thing> spawnedBarricades = new List<Thing>();
            List<Pawn> generatedPawns = new List<Pawn>();
            List<Pawn> placedPawns = new List<Pawn>();
            Lord? lord = null;

            Rand.PushState(Gen.HashCombineInt(outpost.LayoutSeed, 0x46A271));
            try
            {
                IntVec3 center = ResolveCenter(map);
                SpawnBarricadeRing(
                    map,
                    center,
                    outpost.IsCompleted ? 9 : 6,
                    outpost.IsCompleted ? 2 : 3,
                    owner,
                    spawnedBarricades);

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
                    if (TryPlacePawn(map, center, pawn))
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
                    throw new InvalidOperationException("所有生成守军均无法落地。");
                }

                lord = LordMaker.MakeNewLord(
                    owner,
                    new LordJob_DefendPoint(
                        center,
                        wanderRadius: 18f,
                        defendRadius: 34f,
                        isCaravanSendable: false,
                        addFleeToil: true),
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
                Log.Error("[MAP] 普通派系前哨地图初始化失败，已回滚本轮内容: " + ex);
                CleanupFailedGeneration(
                    map,
                    lord,
                    generatedPawns,
                    spawnedBarricades);
                outpost.NotifyMapGarrisonInitialized(false);
            }
            finally
            {
                Rand.PopState();
            }
        }

        private static IntVec3 ResolveCenter(Map map)
        {
            IntVec3 center = map.Center;
            if (center.InBounds(map) && center.Standable(map))
            {
                return center;
            }

            if (CellFinder.TryFindRandomCellNear(
                center,
                map,
                40,
                c => c.InBounds(map) && c.Standable(map),
                out IntVec3 found,
                200))
            {
                return found;
            }

            return map.Center;
        }

        private static void SpawnBarricadeRing(
            Map map,
            IntVec3 center,
            int radius,
            int step,
            Faction owner,
            List<Thing> spawned)
        {
            for (int dx = -radius; dx <= radius; dx += step)
            {
                TrySpawnBarricade(center + new IntVec3(dx, 0, -radius), map, owner, spawned);
                TrySpawnBarricade(center + new IntVec3(dx, 0, radius), map, owner, spawned);
            }

            for (int dz = -radius + step; dz <= radius - step; dz += step)
            {
                TrySpawnBarricade(center + new IntVec3(-radius, 0, dz), map, owner, spawned);
                TrySpawnBarricade(center + new IntVec3(radius, 0, dz), map, owner, spawned);
            }
        }

        private static void TrySpawnBarricade(
            IntVec3 cell,
            Map map,
            Faction owner,
            List<Thing> spawned)
        {
            if (!cell.InBounds(map)
                || cell.GetEdifice(map) != null
                || !cell.Standable(map))
            {
                return;
            }

            Thing barricade = ThingMaker.MakeThing(ThingDefOf.Barricade, ThingDefOf.Steel);
            if (barricade.def.CanHaveFaction)
            {
                barricade.SetFactionDirect(owner);
            }

            Thing? result = GenSpawn.Spawn(
                barricade,
                cell,
                map,
                Rot4.North,
                WipeMode.Vanish);
            if (result != null && result.Spawned)
            {
                spawned.Add(result);
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
                c => c.InBounds(map)
                    && c.Standable(map)
                    && c.GetEdifice(map) == null,
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
            List<Pawn> generated,
            List<Thing> barricades)
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

            // placedPawns 始终是 generated 的子集，统一按生成列表清理一次即可。
            for (int i = 0; i < generated.Count; i++)
            {
                MechHiveCombatPawnUtility.SafelyDiscardPawn(generated[i]);
            }

            for (int i = 0; i < barricades.Count; i++)
            {
                Thing thing = barricades[i];
                if (thing != null && !thing.Destroyed)
                {
                    try
                    {
                        thing.Destroy(DestroyMode.Vanish);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning("[MAP] 回滚普通派系前哨路障失败: " + ex);
                    }
                }
            }
        }
    }
}
