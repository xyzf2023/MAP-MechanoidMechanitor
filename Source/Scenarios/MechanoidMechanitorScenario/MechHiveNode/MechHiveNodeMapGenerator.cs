using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 机械巢节点地图内容生成。由 <see cref="SitePartWorker_MechHiveNode"/> 在地图生成后调用。
    /// 建设中节点使用简陋未完工布局；完整节点使用真正的机械集群式建筑草图。
    /// 建筑布局与守军点数分别生成：守军按节点威胁点数从机械巢 Combat 模板生成，
    /// 全部休眠，纳入同一休眠/唤醒 Lord。建筑预算不扣减守军预算。
    /// 高低角护盾与地图状态建筑仅在 Royalty 启用时生成；Royalty 关闭时不查找相关 Def。
    /// </summary>
    public static class MechHiveNodeMapGenerator
    {
        public static void Generate(Map map, MAPMechHiveNode node)
        {
            if (map == null || node == null)
            {
                return;
            }

            Faction? mechHive = MechHiveNodeRelationUtility.GetMechHive();
            if (mechHive == null)
            {
                Log.Warning("[MAP] 机械巢派系不存在，机械巢节点地图不生成守军与建筑。");
                return;
            }

            // 使用节点布局种子保证可复现。独立 Rand 状态，不污染全局序列。
            Rand.PushState(Gen.HashCombineInt(node.LayoutSeed, 0x1B3F7));
            try
            {
                if (node.IsCompleted)
                {
                    GenerateCompleted(map, node, mechHive);
                }
                else
                {
                    GenerateBuilding(map, node, mechHive);
                }
            }
            catch (Exception ex)
            {
                Log.Error("[MAP] 机械巢节点地图生成异常: " + ex);
            }
            finally
            {
                Rand.PopState();
            }
        }

        /// <summary>完整节点：真正的机械集群式建筑草图 + 独立守军预算。</summary>
        private static void GenerateCompleted(Map map, MAPMechHiveNode node, Faction mechHive)
        {
            IntVec3 center = ResolveCenter(map);
            if (!MechClusterBuildingUtility.TryGenerateCompletedNodeBuildingSketch(
                    map,
                    MechClusterBuildingUtility.CompletedNodeBuildingPoints,
                    out MechClusterSketch sketch)
                || sketch?.buildingsSketch == null)
            {
                Log.Warning("[MAP] 完整机械巢节点未能生成集群草图，跳过建筑布局。");
                SpawnGarrisonAndLord(map, node, mechHive, center, 18f, new List<Thing>(), new List<Thing>(), new List<Thing>());
                return;
            }

            List<Thing> spawnedThings = new List<Thing>();
            sketch.buildingsSketch.Spawn(
                map,
                center,
                mechHive,
                Sketch.SpawnPosType.Unchanged,
                Sketch.SpawnMode.Normal,
                wipeIfCollides: true,
                forceTerrainAffordance: false,
                clearEdificeWhereFloor: false,
                spawnedThings,
                sketch.startDormant,
                buildRoofsInstantly: false,
                canSpawnThing: null,
                onFailedToSpawnThing: (IntVec3 spot, SketchEntity entity) =>
                {
                    if (entity is SketchThing sketchThing
                        && sketchThing.def != ThingDefOf.Wall
                        && sketchThing.def != ThingDefOf.Barricade)
                    {
                        entity.SpawnNear(
                            spot,
                            map,
                            12f,
                            mechHive,
                            Sketch.SpawnMode.Normal,
                            wipeIfCollides: true,
                            forceTerrainAffordance: false,
                            spawnedThings,
                            sketch.startDormant);
                    }
                });

            List<Thing> allBuildings = new List<Thing>();
            List<Thing> threatBuildings = new List<Thing>();
            List<Thing> shields = new List<Thing>();
            Thing? requiredConditionCauser = null;

            for (int i = 0; i < spawnedThings.Count; i++)
            {
                Thing thing = spawnedThings[i];
                if (thing == null || thing.Destroyed)
                {
                    continue;
                }

                if (thing.def.CanHaveFaction)
                {
                    thing.SetFaction(mechHive);
                }

                allBuildings.Add(thing);
                MechClusterBuildingUtility.ApplyDormantSleep(thing);

                if (MechClusterBuildingUtility.IsShield(thing))
                {
                    shields.Add(thing);
                }

                if (MechClusterBuildingUtility.IsBuildingThreat(thing))
                {
                    threatBuildings.Add(thing);
                }

                if (ModsConfig.RoyaltyActive
                    && MechClusterBuildingUtility.IsConditionCauser(
                        thing.def,
                        Mathf.RoundToInt(MechClusterBuildingUtility.CompletedNodeBuildingPoints)))
                {
                    requiredConditionCauser = thing;
                }
            }

            // Royalty：确保至少一种状态建筑已正确初始化并立即生效。
            if (ModsConfig.RoyaltyActive && requiredConditionCauser != null)
            {
                MechClusterBuildingUtility.ApplyImmediateInitiation(requiredConditionCauser);
            }

            float layoutRadius = Mathf.Max(
                12f,
                Mathf.Sqrt(
                    sketch.buildingsSketch.OccupiedSize.x * sketch.buildingsSketch.OccupiedSize.x
                    + sketch.buildingsSketch.OccupiedSize.z * sketch.buildingsSketch.OccupiedSize.z)
                    / 2f);

            SpawnGarrisonAndLord(
                map,
                node,
                mechHive,
                center,
                layoutRadius,
                allBuildings,
                threatBuildings,
                shields);
        }

        /// <summary>建设中节点：保留简陋、未完工布局。</summary>
        private static void GenerateBuilding(Map map, MAPMechHiveNode node, Faction mechHive)
        {
            IntVec3 center = ResolveCenter(map);
            float layoutRadius = 9f;

            List<Thing> allBuildings = new List<Thing>();
            List<Thing> threatBuildings = new List<Thing>();
            List<Thing> shields = new List<Thing>();

            List<ThingDef> turretPalette = MechClusterBuildingUtility.GetMechTurretDefs();
            if (turretPalette.Count == 0)
            {
                if (MechHiveNodeDefOf.Turret_AutoInferno != null)
                {
                    turretPalette.Add(MechHiveNodeDefOf.Turret_AutoInferno);
                }

                if (MechHiveNodeDefOf.Turret_AutoChargeBlaster != null)
                {
                    turretPalette.Add(MechHiveNodeDefOf.Turret_AutoChargeBlaster);
                }
            }

            int turretCount = Rand.RangeInclusive(2, 4);
            for (int i = 0; i < turretCount && turretPalette.Count > 0; i++)
            {
                ThingDef turretDef = turretPalette.RandomElement();
                if (TrySpawnBuilding(map, center, layoutRadius, turretDef, mechHive, out Thing turret))
                {
                    allBuildings.Add(turret);
                    threatBuildings.Add(turret);
                }
            }

            SpawnStructures(map, center, layoutRadius, mechHive, allBuildings);

            for (int i = 0; i < allBuildings.Count; i++)
            {
                MechClusterBuildingUtility.ApplyDormantSleep(allBuildings[i]);
            }

            SpawnGarrisonAndLord(
                map,
                node,
                mechHive,
                center,
                layoutRadius,
                allBuildings,
                threatBuildings,
                shields);
        }

        private static void SpawnGarrisonAndLord(
            Map map,
            MAPMechHiveNode node,
            Faction mechHive,
            IntVec3 center,
            float layoutRadius,
            List<Thing> allBuildings,
            List<Thing> threatBuildings,
            List<Thing> shields)
        {
            List<Pawn> generated =
                MechHiveCombatPawnUtility.GenerateCombatPawns(mechHive, map, node.GarrisonThreatPoints);
            List<Pawn> spawnedPawns = PlacePawns(map, center, layoutRadius + 4f, generated);

            if (allBuildings.Count == 0 && spawnedPawns.Count == 0)
            {
                return;
            }

            float defendRadius = layoutRadius + 6f;
            LordJob_SleepThenMechanoidsDefend lordJob = new LordJob_SleepThenMechanoidsDefend(
                allBuildings,
                mechHive,
                defendRadius,
                center,
                canAssaultColony: false,
                isMechCluster: false);
            Lord lord = LordMaker.MakeNewLord(mechHive, lordJob, map);

            for (int i = 0; i < shields.Count; i++)
            {
                lordJob.AddThingToNotifyOnDefeat(shields[i]);
            }

            for (int i = 0; i < threatBuildings.Count; i++)
            {
                if (threatBuildings[i] is Building building)
                {
                    lord.AddBuilding(building);
                }
            }

            for (int i = 0; i < spawnedPawns.Count; i++)
            {
                lord.AddPawn(spawnedPawns[i]);
            }
        }

        private static IntVec3 ResolveCenter(Map map)
        {
            IntVec3 center = map.Center;
            if (center.Standable(map) && !center.Fogged(map))
            {
                return center;
            }

            if (CellFinder.TryFindRandomCellNear(
                    map.Center,
                    map,
                    20,
                    c => c.Standable(map) && !c.Fogged(map),
                    out IntVec3 result))
            {
                return result;
            }

            return map.Center;
        }

        private static void SpawnStructures(
            Map map,
            IntVec3 center,
            float radius,
            Faction mechHive,
            List<Thing> allBuildings)
        {
            int wallSegments = Rand.RangeInclusive(1, 2);
            for (int i = 0; i < wallSegments; i++)
            {
                if (!CellFinder.TryFindRandomCellNear(
                        center,
                        map,
                        Mathf.CeilToInt(radius),
                        c => CanPlaceBuilding(map, ThingDefOf.Wall, c),
                        out IntVec3 start))
                {
                    continue;
                }

                Rot4 dir = Rand.Bool ? Rot4.East : Rot4.North;
                int segLen = Rand.RangeInclusive(2, 3);
                IntVec3 cursor = start;
                for (int j = 0; j < segLen; j++)
                {
                    if (CanPlaceBuilding(map, ThingDefOf.Wall, cursor)
                        && TrySpawnAt(
                            map,
                            cursor,
                            ThingDefOf.Wall,
                            GenStuff.DefaultStuffFor(ThingDefOf.Wall),
                            mechHive,
                            out Thing wall))
                    {
                        allBuildings.Add(wall);
                    }

                    cursor += dir.FacingCell;
                }
            }

            int barricades = Rand.RangeInclusive(3, 5);
            ThingDef barricadeDef = ThingDefOf.Barricade;
            for (int i = 0; i < barricades; i++)
            {
                if (CellFinder.TryFindRandomCellNear(
                        center,
                        map,
                        Mathf.CeilToInt(radius),
                        c => CanPlaceBuilding(map, barricadeDef, c),
                        out IntVec3 cell)
                    && TrySpawnAt(
                        map,
                        cell,
                        barricadeDef,
                        GenStuff.DefaultStuffFor(barricadeDef),
                        mechHive,
                        out Thing barricade))
                {
                    allBuildings.Add(barricade);
                }
            }
        }

        private static List<Pawn> PlacePawns(
            Map map,
            IntVec3 center,
            float radius,
            List<Pawn> pawns)
        {
            List<Pawn> spawned = new List<Pawn>();
            if (pawns == null || pawns.Count == 0)
            {
                return spawned;
            }

            List<Pawn> unused = new List<Pawn>();
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn == null)
                {
                    continue;
                }

                if (!CellFinder.TryFindRandomCellNear(
                        center,
                        map,
                        Mathf.CeilToInt(radius),
                        c => IsValidPawnCell(map, c),
                        out IntVec3 cell))
                {
                    unused.Add(pawn);
                    continue;
                }

                GenSpawn.Spawn(pawn, cell, map, Rot4.Random);
                MechClusterBuildingUtility.ApplyDormantSleep(pawn);
                spawned.Add(pawn);
            }

            MechHiveCombatPawnUtility.DiscardPawns(unused);
            return spawned;
        }

        private static bool IsValidPawnCell(Map map, IntVec3 c)
        {
            if (!c.InBounds(map) || c.Fogged(map) || !c.Standable(map))
            {
                return false;
            }

            if (c.GetEdifice(map) != null)
            {
                return false;
            }

            Building_Door? door = c.GetDoor(map);
            if (door != null)
            {
                return false;
            }

            return !c.GetThingList(map).Exists(t => t is Pawn);
        }

        private static bool TrySpawnBuilding(
            Map map,
            IntVec3 center,
            float radius,
            ThingDef def,
            Faction mechHive,
            out Thing spawned)
        {
            spawned = null!;
            if (def == null || MechClusterBuildingUtility.IsExcludedFromNodeCluster(def))
            {
                return false;
            }

            if (!CellFinder.TryFindRandomCellNear(
                    center,
                    map,
                    Mathf.CeilToInt(radius),
                    c => CanPlaceBuilding(map, def, c),
                    out IntVec3 cell))
            {
                return false;
            }

            ThingDef? stuff = def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null;
            return TrySpawnAt(map, cell, def, stuff, mechHive, out spawned);
        }

        private static bool TrySpawnAt(
            Map map,
            IntVec3 cell,
            ThingDef def,
            ThingDef? stuff,
            Faction mechHive,
            out Thing spawned)
        {
            spawned = null!;
            try
            {
                Thing thing = ThingMaker.MakeThing(def, stuff);
                GenSpawn.Spawn(thing, cell, map, Rot4.North, WipeMode.Vanish);
                if (thing.def.CanHaveFaction)
                {
                    thing.SetFaction(mechHive);
                }

                spawned = thing;
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning("[MAP] 机械巢节点建筑生成失败（" + def.defName + "）: " + ex);
                return false;
            }
        }

        private static bool CanPlaceBuilding(Map map, ThingDef def, IntVec3 c)
        {
            CellRect rect = GenAdj.OccupiedRect(c, Rot4.North, def.Size);
            if (!rect.InBounds(map))
            {
                return false;
            }

            foreach (IntVec3 cell in rect)
            {
                if (cell.Fogged(map)
                    || !cell.Standable(map)
                    || cell.GetEdifice(map) != null
                    || cell.InNoBuildEdgeArea(map)
                    || !GenConstruct.CanBuildOnTerrain(def, cell, map, Rot4.North))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
