using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 保留中央大厅，外围复用原版机械巢的随机分割、合并房间算法。相邻房间共墙开门，
    /// 继续复用原版 LayoutWorker 的草图、设备、屋顶、休眠守军及存档流程。
    /// Worker 由 Def 共享，因此不在字段中保存本次生成状态。
    /// </summary>
    public sealed class LayoutWorker_SunBossFacility : LayoutWorker_Structure
    {
        private SunBossLayoutDef Settings => (SunBossLayoutDef)Def;

        public LayoutWorker_SunBossFacility(LayoutDef def) : base(def) { }

        protected override void PostLayoutFlushedToSketch(LayoutStructureSketch parms)
        {
            base.PostLayoutFlushedToSketch(parms);
            CellRect bounds = parms.structureLayout.container;
            CellRect hall = parms.structureLayout.Rooms.First(r => r.requiredDef == Settings.arenaRoom).rects[0];
            HashSet<IntVec3> requiredDoors = new HashSet<IntVec3>(bounds.EdgeCells);
            requiredDoors.UnionWith(hall.EdgeCells);
            List<SketchThing> internalDoors = new List<SketchThing>();
            foreach (SketchThing thing in parms.layoutSketch.Things)
            {
                if (!thing.def.IsDoor) continue;
                if (requiredDoors.Contains(thing.pos))
                {
                    thing.def = ThingDefOf.AncientBlastDoor;
                    thing.stuff = null;
                }
                else internalDoors.Add(thing);
            }
            // 原版 LayoutWorker_Mechhive 使用向上取整的 20% 随机替换。
            // 强制防爆门单独处理，随机比例仅应用于其余内部门。
            int count = (int)Math.Ceiling(internalDoors.Count * 0.2f);
            foreach (SketchThing thing in internalDoors.InRandomOrder().Take(count))
            {
                thing.def = ThingDefOf.AncientBlastDoor;
                thing.stuff = null;
            }
        }

        protected override StructureLayout GenerateStructure(StructureGenParams parms)
        {
            // 自己建立实际共墙连接，避免原版重要房间选取和室外连接回退。
            return GetStructureLayout(parms, new CellRect(0, 0, parms.size.x, parms.size.z));
        }

        protected override StructureLayout GetStructureLayout(StructureGenParams parms, CellRect rect)
        {
            SunBossLayoutDef settings = Settings;
            if (settings.arenaInteriorSize % 2 == 0)
                throw new InvalidOperationException("[MAP] 太阳大厅净尺寸必须为奇数。");
            int hallSize = settings.arenaInteriorSize + 2;
            int ringMargin = settings.corridorWidth + 1;
            int minBand = 12;
            int slackX = (rect.Width - hallSize) / 2 - ringMargin - minBand;
            int slackZ = (rect.Height - hallSize) / 2 - ringMargin - minBand;
            if (slackX < 0 || slackZ < 0 || settings.arenaRoom == null || settings.passageRoom == null)
                throw new InvalidOperationException("[MAP] 太阳设施尺寸不足或缺少大厅/走廊定义。");

            int shiftX = Math.Min(settings.centerOffset, slackX);
            int shiftZ = Math.Min(settings.centerOffset, slackZ);
            CellRect hall = new CellRect((rect.Width - hallSize) / 2 + Rand.RangeInclusive(-shiftX, shiftX),
                (rect.Height - hallSize) / 2 + Rand.RangeInclusive(-shiftZ, shiftZ), hallSize, hallSize);
            CellRect ring = hall.ExpandedBy(ringMargin);
            StructureLayout layout = new StructureLayout(parms.sketch, rect);
            layout.AddRoom(new List<CellRect> { hall }, settings.arenaRoom).noExteriorDoors = true;

            Add(layout, ring.minX, ring.minZ, ring.maxX, hall.minZ, settings.passageRoom);
            Add(layout, ring.minX, hall.maxZ, ring.maxX, ring.maxZ, settings.passageRoom);
            Add(layout, ring.minX, hall.minZ, hall.minX, hall.maxZ, settings.passageRoom);
            Add(layout, hall.maxX, hall.minZ, ring.maxX, hall.maxZ, settings.passageRoom);

            GenerateWing(layout, new CellRect(rect.minX, rect.minZ, rect.Width, ring.minZ - rect.minZ + 1));
            GenerateWing(layout, new CellRect(rect.minX, ring.maxZ, rect.Width, rect.maxZ - ring.maxZ + 1));
            GenerateWing(layout, new CellRect(rect.minX, ring.minZ, ring.minX - rect.minX + 1, ring.Height));
            GenerateWing(layout, new CellRect(ring.maxX, ring.minZ, rect.maxX - ring.maxX + 1, ring.Height));
            layout.FinalizeRooms(avoidDoubleWalls: false);

            // 房间矩形共享一格墙；连接所有共墙房间，形成内部回路。
            for (int i = 0; i < layout.Rooms.Count; i++)
            for (int j = i + 1; j < layout.Rooms.Count; j++)
                Connect(layout, layout.Rooms[i], layout.Rooms[j]);

            AddEntrance(layout, rect, north: false);
            AddEntrance(layout, rect, north: true);
            VerifyRoomGraph(layout);
            return layout;
        }

        private static void Add(StructureLayout layout, int x0, int z0, int x1, int z1, LayoutRoomDef? roomDef = null)
        {
            layout.AddRoom(new List<CellRect> { new CellRect(x0, z0, x1 - x0 + 1, z1 - z0 + 1) }, roomDef);
        }

        private void GenerateWing(StructureLayout layout, CellRect bounds)
        {
            // 原版 StructureLayout 的网格以 (0,0) 为原点；先在局部空间生成，再复制矩形。
            // 禁止原版裁剪房间，保留封闭外壳及各翼与环廊的连接。
            int minimum = Settings.outerRoomSpan.RandomInRange;
            StructureLayout wing = RoomLayoutGenerator.GenerateRandomLayout(layout.sketch,
                new CellRect(0, 0, bounds.Width, bounds.Height), minRoomWidth: minimum,
                minRoomHeight: minimum, areaPrunePercent: 0f, canRemoveRooms: false,
                generateDoors: false, maxMergeRoomsRange: Settings.outerRoomMergeCount,
                canDisconnectRooms: false);
            foreach (LayoutRoom room in wing.Rooms)
                layout.AddRoom(room.rects.Select(r => r.MovedBy(bounds.Min)).ToList());
        }

        private static void Connect(StructureLayout layout, LayoutRoom a, LayoutRoom b)
        {
            List<IntVec3> candidates = new List<IntVec3>();
            foreach (CellRect ra in a.rects)
            foreach (CellRect rb in b.rects)
            {
                int x0 = Math.Max(ra.minX, rb.minX), x1 = Math.Min(ra.maxX, rb.maxX);
                int z0 = Math.Max(ra.minZ, rb.minZ), z1 = Math.Min(ra.maxZ, rb.maxZ);
                if (x0 == x1 && z1 - z0 >= 2)
                    for (int z = z0 + 1; z < z1; z++) candidates.Add(new IntVec3(x0, 0, z));
                else if (z0 == z1 && x1 - x0 >= 2)
                    for (int x = x0 + 1; x < x1; x++) candidates.Add(new IntVec3(x, 0, z0));
            }
            candidates.RemoveAll(c => !layout.IsWallAt(c) || !layout.IsGoodForDoor(c));
            if (candidates.Count == 0) return;
            IntVec3 door = candidates.RandomElement();
            layout.Add(door, RoomLayoutCellType.Door);
            a.connections.Add(b);
            b.connections.Add(a);
        }

        private static void AddEntrance(StructureLayout layout, CellRect bounds, bool north)
        {
            List<CellRect> candidates = layout.Rooms.Where(r => !r.noExteriorDoors)
                .SelectMany(r => r.rects)
                .Where(r => north ? r.maxZ == bounds.maxZ : r.minZ == bounds.minZ).ToList();
            CellRect rect = candidates.RandomElement();
            layout.Add(new IntVec3(rect.CenterCell.x, 0, north ? bounds.maxZ : bounds.minZ), RoomLayoutCellType.Door);
        }

        private static void VerifyRoomGraph(StructureLayout layout)
        {
            HashSet<LayoutRoom> seen = new HashSet<LayoutRoom>();
            Queue<LayoutRoom> queue = new Queue<LayoutRoom>();
            seen.Add(layout.Rooms[0]);
            queue.Enqueue(layout.Rooms[0]);
            while (queue.Count > 0)
                foreach (LayoutRoom neighbour in queue.Dequeue().connections)
                    if (seen.Add(neighbour)) queue.Enqueue(neighbour);
            if (seen.Count != layout.Rooms.Count)
                throw new InvalidOperationException("[MAP] 太阳设施出现不连通房间，拒绝生成。");
        }
    }
}
