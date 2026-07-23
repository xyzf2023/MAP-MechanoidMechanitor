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
    /// 建筑布局与守军点数分别生成：布局使用机械族专用炮塔与结构；守军按节点威胁点数从
    /// 机械巢 Combat 模板生成，全部休眠，纳入同一休眠/唤醒 Lord。
    /// 布局使用节点保存的随机种子，保证撤退后重新进入时同类型布局与完整守军可复现。
    /// 高低角护盾与地图状态建筑仅在 Royalty 启用时生成；Royalty 关闭时不查找相关 Def。
    /// </summary>
    public static class MechHiveNodeMapGenerator
    {
        private const int MaxPlacementAttempts = 40;

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

            bool completed = node.IsCompleted;

            // 使用节点布局种子保证可复现。独立 Rand 状态，不污染全局序列。
            Rand.PushState(Gen.HashCombineInt(node.LayoutSeed, 0x1B3F7));
            try
            {
                GenerateInternal(map, node, mechHive, completed);
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

        private static void GenerateInternal(
            Map map,
            MAPMechHiveNode node,
            Faction mechHive,
            bool completed)
        {
            IntVec3 center = ResolveCenter(map);
            float layoutRadius = completed ? 16f : 9f;

            List<Thing> allBuildings = new List<Thing>();
            List<Thing> threatBuildings = new List<Thing>();
            List<Thing> shields = new List<Thing>();

            // 1) 机械族专用炮塔（建设中 2~4，完整 6~10）。
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

            int turretCount = completed
                ? Rand.RangeInclusive(6, 10)
                : Rand.RangeInclusive(2, 4);
            for (int i = 0; i < turretCount && turretPalette.Count > 0; i++)
            {
                ThingDef turretDef = turretPalette.RandomElement();
                if (TrySpawnBuilding(map, center, layoutRadius, turretDef, mechHive, out Thing turret))
                {
                    allBuildings.Add(turret);
                    threatBuildings.Add(turret);
                }
            }

            // 2) “正在建设/防御环”结构：短墙 + 路障（非蓝图、非施工框架）。
            SpawnStructures(map, center, layoutRadius, mechHive, completed, allBuildings);

            // 3) 护盾与地图状态建筑（仅 Royalty）。
            if (completed && ModsConfig.RoyaltyActive)
            {
                SpawnShields(map, center, layoutRadius, mechHive, allBuildings, threatBuildings, shields);
                SpawnConditionCauser(
                    map,
                    center,
                    layoutRadius,
                    mechHive,
                    node.GarrisonThreatPoints,
                    allBuildings,
                    threatBuildings);
            }

            // 4) 建筑休眠初始化。
            for (int i = 0; i < allBuildings.Count; i++)
            {
                allBuildings[i].TryGetComp<CompCanBeDormant>()?.ToSleep();
            }

            // 至少保证一个建筑，供 Lord 的唤醒触发与信号使用。
            if (allBuildings.Count == 0)
            {
                return;
            }

            // 5) 守军：按节点威胁点数从 Combat 模板生成，落地后休眠。
            List<Pawn> generated =
                MechHiveNodeCombatPawnGenerator.GenerateCombatPawns(mechHive, node.GarrisonThreatPoints, map);
            List<Pawn> spawnedPawns = PlacePawns(map, center, layoutRadius + 4f, generated);

            // 6) 休眠防御 Lord：isMechCluster=false，胜负仅由本 MOD 威胁清理判定。
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
            bool completed,
            List<Thing> allBuildings)
        {
            // 短墙段（带缺口，不闭合）。
            int wallSegments = completed ? Rand.RangeInclusive(3, 5) : Rand.RangeInclusive(1, 2);
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
                int segLen = completed ? Rand.RangeInclusive(3, 5) : Rand.RangeInclusive(2, 3);
                IntVec3 cursor = start;
                for (int j = 0; j < segLen; j++)
                {
                    if (CanPlaceBuilding(map, ThingDefOf.Wall, cursor)
                        && TrySpawnAt(map, cursor, ThingDefOf.Wall, GenStuff.DefaultStuffFor(ThingDefOf.Wall), mechHive, out Thing wall))
                    {
                        allBuildings.Add(wall);
                    }

                    cursor += dir.FacingCell;
                }
            }

            // 零散路障。
            int barricades = completed ? Rand.RangeInclusive(8, 12) : Rand.RangeInclusive(3, 5);
            ThingDef barricadeDef = ThingDefOf.Barricade;
            for (int i = 0; i < barricades; i++)
            {
                if (CellFinder.TryFindRandomCellNear(
                        center,
                        map,
                        Mathf.CeilToInt(radius),
                        c => CanPlaceBuilding(map, barricadeDef, c),
                        out IntVec3 cell)
                    && TrySpawnAt(map, cell, barricadeDef, GenStuff.DefaultStuffFor(barricadeDef), mechHive, out Thing barricade))
                {
                    allBuildings.Add(barricade);
                }
            }
        }

        private static void SpawnShields(
            Map map,
            IntVec3 center,
            float radius,
            Faction mechHive,
            List<Thing> allBuildings,
            List<Thing> threatBuildings,
            List<Thing> shields)
        {
            if (MechClusterBuildingUtility.TryGetLowAngleShieldDef(out ThingDef lowShield)
                && TrySpawnBuilding(map, center, radius, lowShield, mechHive, out Thing low))
            {
                allBuildings.Add(low);
                threatBuildings.Add(low);
                shields.Add(low);
            }

            if (MechClusterBuildingUtility.TryGetHighAngleShieldDef(out ThingDef highShield)
                && TrySpawnBuilding(map, center, radius, highShield, mechHive, out Thing high))
            {
                allBuildings.Add(high);
                threatBuildings.Add(high);
                shields.Add(high);
            }
        }

        private static void SpawnConditionCauser(
            Map map,
            IntVec3 center,
            float radius,
            Faction mechHive,
            int threatPoints,
            List<Thing> allBuildings,
            List<Thing> threatBuildings)
        {
            List<ThingDef> causers = MechClusterBuildingUtility.GetConditionCausers(threatPoints);
            if (causers.Count == 0)
            {
                return;
            }

            ThingDef causerDef = causers.RandomElement();
            if (!TrySpawnBuilding(map, center, radius, causerDef, mechHive, out Thing causer))
            {
                return;
            }

            allBuildings.Add(causer);
            threatBuildings.Add(causer);

            // 状态建筑进入地图即视为已初始化生效（复用集群部署的初始化方式）。
            CompInitiatable? initiatable = causer.TryGetComp<CompInitiatable>();
            if (initiatable != null)
            {
                initiatable.initiationDelayTicksOverride = 1;
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

            // 未能落地的 Pawn 安全丢弃，避免残留世界 Pawn。
            MechHiveNodeCombatPawnGenerator.DiscardPawns(unused);
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

            // 不堵门。
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
            if (def == null)
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
