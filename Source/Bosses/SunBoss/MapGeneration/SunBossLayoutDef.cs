using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    // 仅由 Odyssey 目录中的 Def 实例化；不通过 DefOf 强制读取未启用 DLC 的定义。
    public sealed class SunBossLayoutDef : StructureLayoutDef
    {
        public int arenaInteriorSize = 51;
        public int corridorWidth = 5;
        public int centerOffset = 4;
        // 每个翼区随机选择原版分割算法的最小房间边长，而非固定长条宽度。
        public IntRange outerRoomSpan = new IntRange(9, 12);
        public IntRange outerRoomMergeCount = new IntRange(1, 3);
        public LayoutRoomDef lootRoom = null!;
        public ThingDef fallbackLootCrate = null!;
        public LayoutRoomDef arenaRoom = null!;
        public LayoutRoomDef passageRoom = null!;
        public ThingDef coreBuilding = null!;
        public ThingDef stabilizerBuilding = null!;
        public IntRange arenaWallCount = new IntRange(10, 14);
        public IntRange arenaWallLength = new IntRange(3, 5);

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (arenaRoom == null || passageRoom == null || coreBuilding == null || stabilizerBuilding == null)
                yield return "太阳设施必须配置大厅、走廊、核心与稳定器定义。";
            if (wallDef == null || doorDef == null || terrainDef == null)
                yield return "太阳设施必须配置墙、门与地形。";
            if (arenaInteriorSize < 40 || corridorWidth < 3 || centerOffset < 0)
                yield return "太阳设施大厅不得小于 40 格，走廊不得窄于 3 格，中心偏移不得为负。";
            if (arenaInteriorSize % 2 == 0)
                yield return "太阳设施大厅净尺寸必须为奇数，以保证核心建筑准确居中。";
            if (coreBuilding != null && (coreBuilding.size.x % 2 == 0 || coreBuilding.size.z % 2 == 0))
                yield return "太阳设施核心建筑的占地长宽必须为奇数，以保证准确居中。";
            if (outerRoomSpan.min < 9 || outerRoomSpan.max < outerRoomSpan.min || outerRoomSpan.max > 14)
                yield return "外围最小房间边长应在 9~14 格内。";
            if (outerRoomMergeCount.min < 1 || outerRoomMergeCount.max < outerRoomMergeCount.min || outerRoomMergeCount.max > 4)
                yield return "外围房间合并数量应在 1~4 之间。";
            if (lootRoom == null || fallbackLootCrate == null)
                yield return "太阳设施必须配置原版战利品房间与保底密封箱。";
            if (arenaWallCount.min < 0 || arenaWallCount.max < arenaWallCount.min ||
                arenaWallLength.min < 1 || arenaWallLength.max < arenaWallLength.min)
                yield return "太阳大厅随机墙体数量或长度范围无效。";
        }
    }
}
