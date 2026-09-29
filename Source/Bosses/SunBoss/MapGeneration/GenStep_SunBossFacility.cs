using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class GenStep_SunBossFacility : GenStep
    {
        public SunBossLayoutDef layout = null!;
        public IntRange facilitySize = new IntRange(112, 128);
        public float defaultThreatPoints = 1800f;
        public override int SeedPart => 731694207;

        public override void Generate(Map map, GenStepParams parms)
        {
            if (!ModsConfig.OdysseyActive) return;
            MapComponent_SunBossArena record = map.GetComponent<MapComponent_SunBossArena>();
            if (record.Generated) return;
            if (layout == null || layout.coreBuilding == null || layout.stabilizerBuilding == null ||
                layout.terrainDef == null || Faction.OfMechanoids == null)
                throw new InvalidOperationException("[MAP-机械族机械师] 太阳设施缺少布局或场景建筑定义。");

            int width = facilitySize.RandomInRange;
            int height = facilitySize.RandomInRange;
            if (width + 24 > map.Size.x || height + 24 > map.Size.z)
                throw new InvalidOperationException("[MAP-机械族机械师] 地图太小，无法保留太阳设施及外围入场空间。");
            CellRect bounds = new CellRect((map.Size.x - width) / 2, (map.Size.z - height) / 2, width, height);
            LayoutStructureSketch sketch = layout.Worker.GenerateStructureSketch(new StructureGenParams
            {
                size = bounds.Size,
                faction = Faction.OfMechanoids
            });

            ClearSite(map, bounds.ExpandedBy(3), layout.terrainDef);
            MapGenerator.GetOrGenerateVar<List<CellRect>>("UsedRects").Add(bounds.ExpandedBy(3));
            map.layoutStructureSketches.Add(sketch);
            float points = parms.sitePart?.parms.points ?? defaultThreatPoints;
            // 直接使用自定义 MapGenerator 时没有 SitePart 参数，从 Site 读取地点预算。
            if (parms.sitePart == null && map.Parent is RimWorld.Planet.Site site)
                points = site.desiredThreatPoints > 0f ? site.desiredThreatPoints : defaultThreatPoints;
            bool oldDestroyPermission = Thing.allowDestroyNonDestroyable;
            try
            {
                layout.Worker.Spawn(sketch, map, bounds.Min, points, faction: Faction.OfMechanoids);
            }
            finally
            {
                // 原版 LayoutWorker 会临时修改此全局开关；异常路径也必须恢复。
                Thing.allowDestroyNonDestroyable = oldDestroyPermission;
            }

            LayoutRoom arenaRoom = sketch.structureLayout.Rooms.First(r => r.requiredDef == layout.arenaRoom);
            CellRect arena = arenaRoom.rects[0].ContractedBy(1);
            foreach (LayoutRoom room in sketch.structureLayout.Rooms)
            {
                if (room == arenaRoom) continue;
                ClearRoomRoutes(map, room);
                SupportRoomRoof(map, room);
                EnsureRoomLoot(map, room);
            }
            List<IntVec3> entrances = bounds.EdgeCells.Where(c => c.GetDoor(map) != null).ToList();
            foreach (IntVec3 entrance in entrances)
                ClearApproach(map, entrance, entrance.z == bounds.minZ, layout.terrainDef);
            if (entrances.Count == 0)
                throw new InvalidOperationException("[MAP-机械族机械师] 太阳设施未生成外部入口。");

            record.FacilityBounds = bounds;
            record.ArenaBounds = arena;
            PopulateArena(map, arena, record);
            VerifyWalkways(map, sketch, entrances[0]);
            IntVec3 southEntrance = entrances.First(c => c.z == bounds.minZ);
            MapGenerator.PlayerStartSpot = southEntrance + new IntVec3(0, 0, -8);
            record.Generated = true;
        }

        private static void ClearSite(Map map, CellRect rect, TerrainDef terrain)
        {
            foreach (IntVec3 cell in rect)
            {
                if (!cell.InBounds(map)) continue;
                map.roofGrid.SetRoof(cell, null);
                foreach (Thing thing in cell.GetThingList(map).ToList())
                {
                    if (thing is Pawn || !thing.def.destroyable) continue;
                    thing.Destroy(DestroyMode.Vanish);
                }
                map.terrainGrid.SetTerrain(cell, terrain);
            }
        }

        private static void ClearRoomRoutes(Map map, LayoutRoom room)
        {
            HashSet<IntVec3> interior = RoomInterior(room);
            HashSet<IntVec3> domain = new HashSet<IntVec3>(interior);
            List<IntVec3> targets = room.rects.Select(r => r.ContractedBy(1).Cells
                .Where(c => Passable(map, c)).OrderBy(c => c.DistanceToSquared(r.CenterCell))
                .DefaultIfEmpty(r.CenterCell).First()).Distinct().ToList();
            targets.AddRange(room.rects.SelectMany(r => r.EdgeCells)
                .Where(c => c.GetDoor(map) != null).Distinct());
            domain.UnionWith(targets);
            IntVec3 root = targets[0];
            foreach (IntVec3 target in targets)
            {
                // 优先沿已有空地绕行，只在确实堵塞时清除最短通路上的障碍。
                List<IntVec3>? path = FindRoomRoute(map, domain, root, target, false)
                    ?? FindRoomRoute(map, domain, root, target, true);
                if (path == null)
                    throw new InvalidOperationException("[MAP-机械族机械师] 太阳设施房间无法建立内部通路。");
                foreach (IntVec3 cell in path)
                    if (interior.Contains(cell))
                        foreach (Thing thing in cell.GetThingList(map).ToList())
                            if (thing.def.passability == Traversability.Impassable && CanClearRouteThing(thing))
                                thing.Destroy(DestroyMode.Vanish);
            }
        }

        private static HashSet<IntVec3> RoomInterior(LayoutRoom room)
        {
            HashSet<IntVec3> cells = new HashSet<IntVec3>(room.rects.SelectMany(r => r.Cells));
            return new HashSet<IntVec3>(cells.Where(c =>
                GenAdj.CardinalDirections.All(d => cells.Contains(c + d))));
        }

        private static bool CanClearRouteThing(Thing thing) => thing.def.destroyable &&
            !(thing is Pawn) && !(thing is Building_Door) && !(thing is Building_Crate) &&
            thing.TryGetComp<CompLootSpawn>() == null;

        private static List<IntVec3>? FindRoomRoute(Map map, HashSet<IntVec3> domain,
            IntVec3 start, IntVec3 target, bool clearObstacles)
        {
            bool Allowed(IntVec3 c) => Passable(map, c) || (clearObstacles &&
                c.GetThingList(map).Where(t => t.def.passability == Traversability.Impassable).All(CanClearRouteThing));
            if (!Allowed(start) || !Allowed(target)) return null;
            Dictionary<IntVec3, IntVec3> parents = new Dictionary<IntVec3, IntVec3> { [start] = start };
            Queue<IntVec3> queue = new Queue<IntVec3>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                IntVec3 cell = queue.Dequeue();
                if (cell == target)
                {
                    List<IntVec3> path = new List<IntVec3> { cell };
                    while (cell != start) { cell = parents[cell]; path.Add(cell); }
                    return path;
                }
                foreach (IntVec3 direction in GenAdj.CardinalDirections)
                {
                    IntVec3 next = cell + direction;
                    if (!domain.Contains(next) || parents.ContainsKey(next) || !Allowed(next)) continue;
                    parents.Add(next, cell);
                    queue.Enqueue(next);
                }
            }
            return null;
        }

        private void SupportRoomRoof(Map map, LayoutRoom room)
        {
            List<IntVec3> interior = RoomInterior(room).OrderBy(c => c.z).ThenBy(c => c.x).ToList();
            HashSet<IntVec3> domain = new HashSet<IntVec3>(interior);
            domain.UnionWith(room.rects.SelectMany(r => r.EdgeCells).Where(c => c.GetDoor(map) != null));
            foreach (IntVec3 cell in interior)
            {
                if (!cell.Roofed(map) || RoofCollapseUtility.WithinRangeOfRoofHolder(cell, map)) continue;
                List<IntVec3> candidates = interior.Where(c => c.Roofed(map) && c.InHorDistOf(cell, 5f) &&
                    c.GetThingList(map).Count == 0 && c.Walkable(map) &&
                    !GenAdj.CardinalDirections.Any(d => (c + d).GetDoor(map) != null))
                    .OrderBy(c => c.DistanceToSquared(cell)).ToList();
                bool placed = false;
                foreach (IntVec3 candidate in candidates)
                {
                    // 加固墙不可通行：先模拟移除这一格，确认原本相通的邻格仍可绕行。
                    domain.Remove(candidate);
                    List<IntVec3> neighbours = GenAdj.CardinalDirections.Select(d => candidate + d)
                        .Where(c => domain.Contains(c) && Passable(map, c)).ToList();
                    bool safe = neighbours.Count > 0 && neighbours.Skip(1)
                        .All(c => FindRoomRoute(map, domain, neighbours[0], c, false) != null);
                    domain.Add(candidate);
                    if (!safe) continue;
                    Thing support = ThingMaker.MakeThing(layout.wallDef, layout.wallStuffDef);
                    GenSpawn.Spawn(support, candidate, map);
                    placed = true;
                    break;
                }
                if (!placed)
                    throw new InvalidOperationException("[MAP-机械族机械师] 太阳设施房间缺少不会堵路的加固墙支撑位置。");
            }
        }

        private void EnsureRoomLoot(Map map, LayoutRoom room)
        {
            if (!room.defs.Contains(layout.lootRoom)) return;
            HashSet<IntVec3> interior = RoomInterior(room);
            if (interior.SelectMany(c => c.GetThingList(map)).Any(t => t.TryGetComp<CompLootSpawn>() != null)) return;
            List<IntVec3> candidates = interior.Where(c => c.GetThingList(map).Count == 0 && c.Walkable(map))
                .OrderBy(c => c.z).ThenBy(c => c.x).ToList();
            if (candidates.Count == 0)
                throw new InvalidOperationException("[MAP-机械族机械师] 太阳设施战利品房间无法放置密封箱。");
            // 仅在原版预制件没有放下战利品箱时保底，沿用原版 CompLootSpawn 的奖励表。
            GenSpawn.Spawn(ThingMaker.MakeThing(layout.fallbackLootCrate), candidates.RandomElement(), map);
        }

        private static void ClearApproach(Map map, IntVec3 entrance, bool south, TerrainDef terrain)
        {
            int z0 = south ? 0 : entrance.z + 1;
            int z1 = south ? entrance.z - 1 : map.Size.z - 1;
            ClearSite(map, new CellRect(entrance.x - 1, z0, 3, z1 - z0 + 1), terrain);
        }

        private void PopulateArena(Map map, CellRect arena, MapComponent_SunBossArena record)
        {
            // 六个外围分区各一座，坐标允许小幅随机变化，绝不缩减生成数量。
            IntVec3 center = arena.CenterCell;
            int inset = Math.Max(layout.stabilizerBuilding.size.x, layout.stabilizerBuilding.size.z) / 2 + 4;
            CellRect ring = arena.ContractedBy(inset);
            IntVec3[] anchors =
            {
                new IntVec3(center.x - 11, 0, ring.minZ),
                new IntVec3(center.x + 11, 0, ring.minZ),
                new IntVec3(ring.minX, 0, center.z + 6),
                new IntVec3(ring.maxX, 0, center.z - 6),
                new IntVec3(center.x - 11, 0, ring.maxZ),
                new IntVec3(center.x + 11, 0, ring.maxZ)
            };
            List<CellRect> reserved = new List<CellRect>();
            record.Core = SpawnSceneBuilding(map, arena, layout.coreBuilding, center, reserved);
            record.Stabilizers.Clear();
            foreach (IntVec3 anchor in anchors)
            {
                IntVec3 position = anchor + new IntVec3(Rand.RangeInclusive(-1, 1), 0, Rand.RangeInclusive(-1, 1));
                record.Stabilizers.Add(SpawnSceneBuilding(map, arena, layout.stabilizerBuilding, position, reserved));
            }

            CompProperties_AnnihilationCannon cannon = DefDatabase<ThingDef>.GetNamed("MAP_Mech_SunBOSS")
                .GetCompProperties<CompProperties_AnnihilationCannon>();
            if (cannon == null || !AllStabilizersHaveLure(map, arena, record, cannon))
                throw new InvalidOperationException("[MAP-机械族机械师] 太阳设施存在无法诱导湮灭炮摧毁的稳定器。");

            // 大厅内门口、中轴和周边留空；短墙彼此间隔，避免生成封闭小房间。
            int requested = layout.arenaWallCount.RandomInRange;
            int placed = 0;
            List<CellRect> wallAreas = new List<CellRect>();
            CellRect scatter = arena.ContractedBy(4);
            for (int attempt = 0; attempt < requested * 30 && placed < requested; attempt++)
            {
                int length = layout.arenaWallLength.RandomInRange;
                bool horizontal = Rand.Bool;
                IntVec3 root = scatter.RandomCell;
                CellRect wall = new CellRect(root.x, root.z, horizontal ? length : 1, horizontal ? 1 : length);
                if (!wall.FullyContainedWithin(scatter) || reserved.Any(r => r.Overlaps(wall.ExpandedBy(2))) ||
                    wallAreas.Any(r => r.Overlaps(wall.ExpandedBy(2))) ||
                    wall.Cells.Any(c => Math.Abs(c.x - center.x) <= 2 || Math.Abs(c.z - center.z) <= 2 || c.GetEdifice(map) != null))
                    continue;
                List<Thing> spawnedWalls = new List<Thing>();
                foreach (IntVec3 cell in wall)
                {
                    Thing thing = ThingMaker.MakeThing(layout.wallDef, layout.wallStuffDef);
                    GenSpawn.Spawn(thing, cell, map);
                    spawnedWalls.Add(thing);
                }
                if (!AllOpenCellsConnected(map, arena) || !AllStabilizersHaveLure(map, arena, record, cannon))
                {
                    foreach (Thing thing in spawnedWalls) thing.Destroy(DestroyMode.Vanish);
                    continue;
                }
                wallAreas.Add(wall);
                placed++;
            }
            // 无屋顶必须主动清除，不能只靠 noRoof 跳过生成。
            foreach (IntVec3 cell in arena)
            {
                map.roofGrid.SetRoof(cell, null);
                map.areaManager.NoRoof[cell] = true;
            }
        }

        private static bool AllStabilizersHaveLure(Map map, CellRect arena, MapComponent_SunBossArena record,
            CompProperties_AnnihilationCannon cannon)
        {
            if (record.Core == null) return false;
            // 激活后太阳可从核心位置走到这些相邻空格；生成时核心建筑尚未移除。
            List<IntVec3> firingCells = record.Core.OccupiedRect().ExpandedBy(1).EdgeCells
                .Where(c => arena.Contains(c) && c.Standable(map)).ToList();
            if (firingCells.Count == 0) return false;
            HashSet<IntVec3> reachable = Flood(map, arena, firingCells[0]);
            foreach (Building stabilizer in record.Stabilizers)
            {
                CellRect occupied = stabilizer.OccupiedRect();
                bool found = false;
                IEnumerable<IntVec3> candidates = occupied.ExpandedBy((int)Math.Ceiling(cannon.innerRadius)).Cells
                    .Where(c => arena.Contains(c) && c.Standable(map) && reachable.Contains(c))
                    .OrderBy(c => c.DistanceToSquared(record.Core.Position));
                foreach (IntVec3 lure in candidates)
                {
                    if (!occupied.Cells.Any(c => c.DistanceToSquared(lure) <= cannon.innerRadius * cannon.innerRadius)) continue;
                    if (!firingCells.Any(c => reachable.Contains(c)
                        && c.DistanceToSquared(lure) >= cannon.minRange * cannon.minRange
                        && c.DistanceToSquared(lure) <= cannon.range * cannon.range
                        && GenSight.LineOfSight(c, lure, map))) continue;
                    // 与湮灭炮使用同一内圈算法，墙体遮挡和建筑任一占地格命中规则一致。
                    if (!DamageDefOf.Bomb.Worker.ExplosionCellsToHit(lure, map, cannon.innerRadius).Any(occupied.Contains)) continue;
                    found = true;
                    break;
                }
                if (!found) return false;
            }
            return true;
        }

        private static Building SpawnSceneBuilding(Map map, CellRect arena, ThingDef def, IntVec3 cell, List<CellRect> reserved)
        {
            CellRect occupied = GenAdj.OccupiedRect(cell, Rot4.North, def.size);
            if (!occupied.FullyContainedWithin(arena) || reserved.Any(r => r.Overlaps(occupied)) ||
                occupied.Cells.Any(c => c.GetEdifice(map) != null))
                throw new InvalidOperationException("[MAP-机械族机械师] 太阳大厅场景建筑尺寸或位置冲突：" + def.defName);
            Thing thing = ThingMaker.MakeThing(def);
            if (def.CanHaveFaction) thing.SetFaction(Faction.OfMechanoids);
            Building building = (Building)GenSpawn.Spawn(thing, cell, map);
            reserved.Add(occupied.ExpandedBy(2));
            return building;
        }

        // 检查结构上的连通性：锁定的防爆门骇入后可通行，不在生成时自动解锁。
        private static bool Passable(Map map, IntVec3 cell) => cell.Walkable(map) || cell.GetDoor(map) != null;

        private static HashSet<IntVec3> Flood(Map map, CellRect bounds, IntVec3 start)
        {
            HashSet<IntVec3> seen = new HashSet<IntVec3> { start };
            Queue<IntVec3> queue = new Queue<IntVec3>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                IntVec3 cell = queue.Dequeue();
                foreach (IntVec3 direction in GenAdj.CardinalDirections)
                {
                    IntVec3 next = cell + direction;
                    if (bounds.Contains(next) && !seen.Contains(next) && Passable(map, next))
                    {
                        seen.Add(next);
                        queue.Enqueue(next);
                    }
                }
            }
            return seen;
        }

        private static bool AllOpenCellsConnected(Map map, CellRect bounds)
        {
            List<IntVec3> open = bounds.Cells.Where(c => Passable(map, c)).ToList();
            return open.Count > 0 && Flood(map, bounds, open[0]).Count == open.Count;
        }

        private static void VerifyWalkways(Map map, LayoutStructureSketch sketch, IntVec3 entrance)
        {
            HashSet<IntVec3> reachable = Flood(map, sketch.structureLayout.container, entrance);
            foreach (LayoutRoom room in sketch.structureLayout.Rooms)
            {
                foreach (CellRect rect in room.rects)
                {
                    if (!rect.ContractedBy(1).Cells.Any(reachable.Contains) ||
                        rect.EdgeCells.Any(c => c.GetDoor(map) != null && !reachable.Contains(c)))
                        throw new InvalidOperationException("[MAP-机械族机械师] 太阳设施内容填充后存在不可通行的房间或门。");
                }
            }
        }
    }
}
