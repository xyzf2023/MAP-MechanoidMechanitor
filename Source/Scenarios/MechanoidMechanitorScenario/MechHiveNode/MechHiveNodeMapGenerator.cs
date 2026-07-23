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
    /// 建筑布局与守军点数分别生成：守军按节点威胁点数从机械巢 Combat 模板生成。
    /// </summary>
    public static class MechHiveNodeMapGenerator
    {
        private const int MaxRequiredBuildingPlaceAttempts = 48;

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

        private static void GenerateCompleted(Map map, MAPMechHiveNode node, Faction mechHive)
        {
            IntVec3 center = ResolveCenter(map);
            if (!MechClusterBuildingUtility.TryGenerateCompletedNodeBuildingSketch(
                    map,
                    MechClusterBuildingUtility.CompletedNodeBuildingPoints,
                    out MechClusterSketch sketch)
                || sketch?.buildingsSketch == null)
            {
                Log.Error("[MAP] 完整机械巢节点未能生成合法集群草图，终止该节点初始化。");
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

            float layoutRadius = Mathf.Max(
                12f,
                Mathf.Sqrt(
                    sketch.buildingsSketch.OccupiedSize.x * sketch.buildingsSketch.OccupiedSize.x
                    + sketch.buildingsSketch.OccupiedSize.z * sketch.buildingsSketch.OccupiedSize.z)
                    / 2f);

            if (ModsConfig.RoyaltyActive
                && !EnsureRequiredRoyaltyBuildingsOnMap(
                    map,
                    center,
                    layoutRadius,
                    mechHive,
                    spawnedThings))
            {
                Log.Error(
                    "[MAP] 完整机械巢节点落地后仍缺少必需的低角护盾、高角护盾或地图状态建筑，终止该节点初始化。");
                DestroySpawnedThings(spawnedThings);
                return;
            }

            float defendRadius = layoutRadius + 6f;
            LordJob_SleepThenMechanoidsDefend lordJob = new LordJob_SleepThenMechanoidsDefend(
                spawnedThings,
                mechHive,
                defendRadius,
                center,
                canAssaultColony: false,
                isMechCluster: false);
            Lord lord = LordMaker.MakeNewLord(mechHive, lordJob, map);

            MechClusterBuildingInitUtility.InitializeSpawnedBuildings(
                spawnedThings,
                mechHive,
                lordJob,
                lord,
                MechClusterBuildingInitUtility.InitOptions.ForCompletedNode());

            List<Pawn> generated =
                MechHiveCombatPawnUtility.GenerateCombatPawns(mechHive, map, node.GarrisonThreatPoints);
            List<Pawn> spawnedPawns = PlacePawns(map, center, layoutRadius + 4f, generated);
            for (int i = 0; i < spawnedPawns.Count; i++)
            {
                lord.AddPawn(spawnedPawns[i]);
            }
        }

        private static bool EnsureRequiredRoyaltyBuildingsOnMap(
            Map map,
            IntVec3 center,
            float layoutRadius,
            Faction mechHive,
            List<Thing> spawnedThings)
        {
            if (!ModsConfig.RoyaltyActive)
            {
                return true;
            }

            int points = Mathf.RoundToInt(MechClusterBuildingUtility.CompletedNodeBuildingPoints);
            EvaluateSpawnedRoyaltyRequirements(
                spawnedThings,
                points,
                out bool hasLow,
                out bool hasHigh,
                out bool hasCauser);

            if (!hasLow)
            {
                if (!MechClusterBuildingUtility.TryGetLowAngleShieldDef(out ThingDef low)
                    || !TryPlaceRequiredBuilding(
                        map,
                        center,
                        layoutRadius,
                        low,
                        mechHive,
                        spawnedThings))
                {
                    return false;
                }
            }

            if (!hasHigh)
            {
                if (!MechClusterBuildingUtility.TryGetHighAngleShieldDef(out ThingDef high)
                    || !TryPlaceRequiredBuilding(
                        map,
                        center,
                        layoutRadius,
                        high,
                        mechHive,
                        spawnedThings))
                {
                    return false;
                }
            }

            if (!hasCauser)
            {
                List<ThingDef> causers = MechClusterBuildingUtility.GetConditionCausers(points);
                if (causers.Count == 0
                    || !TryPlaceRequiredBuilding(
                        map,
                        center,
                        layoutRadius,
                        causers.RandomElement(),
                        mechHive,
                        spawnedThings))
                {
                    return false;
                }
            }

            EvaluateSpawnedRoyaltyRequirements(
                spawnedThings,
                points,
                out hasLow,
                out hasHigh,
                out hasCauser);
            return hasLow && hasHigh && hasCauser;
        }

        private static void EvaluateSpawnedRoyaltyRequirements(
            List<Thing> spawnedThings,
            int points,
            out bool hasLow,
            out bool hasHigh,
            out bool hasCauser)
        {
            hasLow = false;
            hasHigh = false;
            hasCauser = false;
            for (int i = 0; i < spawnedThings.Count; i++)
            {
                Thing thing = spawnedThings[i];
                if (thing == null || thing.Destroyed)
                {
                    continue;
                }

                if (MechClusterBuildingUtility.IsLowAngleShieldDef(thing.def))
                {
                    hasLow = true;
                }

                if (MechClusterBuildingUtility.IsHighAngleShieldDef(thing.def))
                {
                    hasHigh = true;
                }

                if (MechClusterBuildingUtility.IsConditionCauser(thing.def, points))
                {
                    hasCauser = true;
                }
            }
        }

        private static bool TryPlaceRequiredBuilding(
            Map map,
            IntVec3 center,
            float layoutRadius,
            ThingDef def,
            Faction mechHive,
            List<Thing> spawnedThings)
        {
            int maxRadius = Mathf.CeilToInt(layoutRadius) + 12;
            for (int attempt = 0; attempt < MaxRequiredBuildingPlaceAttempts; attempt++)
            {
                int radius = Mathf.Min(maxRadius, Mathf.CeilToInt(layoutRadius) + attempt / 4);
                if (!CellFinder.TryFindRandomCellNear(
                        center,
                        map,
                        radius,
                        c => CanPlaceBuilding(map, def, c),
                        out IntVec3 cell))
                {
                    continue;
                }

                ThingDef? stuff = def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null;
                if (!TrySpawnAt(map, cell, def, stuff, mechHive, out Thing spawned))
                {
                    continue;
                }

                spawnedThings.Add(spawned);
                return true;
            }

            return false;
        }

        private static void DestroySpawnedThings(List<Thing> spawnedThings)
        {
            for (int i = 0; i < spawnedThings.Count; i++)
            {
                Thing thing = spawnedThings[i];
                if (thing != null && !thing.Destroyed)
                {
                    thing.Destroy(DestroyMode.Vanish);
                }
            }

            spawnedThings.Clear();
        }

        private static void GenerateBuilding(Map map, MAPMechHiveNode node, Faction mechHive)
        {
            IntVec3 center = ResolveCenter(map);
            float layoutRadius = 9f;

            List<Thing> allBuildings = new List<Thing>();

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
                }
            }

            SpawnStructures(map, center, layoutRadius, mechHive, allBuildings);

            float defendRadius = layoutRadius + 6f;
            LordJob_SleepThenMechanoidsDefend lordJob = new LordJob_SleepThenMechanoidsDefend(
                allBuildings,
                mechHive,
                defendRadius,
                center,
                canAssaultColony: false,
                isMechCluster: false);
            Lord lord = LordMaker.MakeNewLord(mechHive, lordJob, map);

            // 建设中节点：复用同一初始化链路（休眠 + CompSpawnerPawn 首次生成时间），无 Royalty 必需建筑。
            MechClusterBuildingInitUtility.InitializeSpawnedBuildings(
                allBuildings,
                mechHive,
                lordJob,
                lord,
                new MechClusterBuildingInitUtility.InitOptions(
                    startDormant: true,
                    immediateAllConditionCausers: false,
                    immediateConditionCauserDef: null,
                    applyRandomInitiation: false,
                    randomInitiationDays: 0f,
                    assemblerDelayTicks: (int)(
                        MechClusterBuildingInitUtility.MechAssemblerInitialDelayDays.RandomInRange
                        * 60000f)));

            List<Pawn> generated =
                MechHiveCombatPawnUtility.GenerateCombatPawns(mechHive, map, node.GarrisonThreatPoints);
            List<Pawn> spawnedPawns = PlacePawns(map, center, layoutRadius + 4f, generated);
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
                pawn.TryGetComp<CompCanBeDormant>()?.ToSleep();
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
