using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 只在新地图生成时初始化迷雾；保留外部入口可见，内部按原版探索规则揭雾。
    /// 不在读档或 Tick 中执行，避免覆盖玩家已经探索的区域。
    /// </summary>
    public sealed class GenStep_SunBossFacilityFog : GenStep_Fog
    {
        public override void Generate(Map map, GenStepParams parms)
        {
            MapComponent_SunBossArena arena = map.GetComponent<MapComponent_SunBossArena>();
            if (arena.Generated)
            {
                // 避免原版的额外揭雾起点提前暴露设备、守军和露天大厅。
                MapGenerator.rootsToUnfog.RemoveAll(cell => arena.FacilityBounds.Contains(cell));
            }
            base.Generate(map, parms);
            RefogInterior(map);
        }

        public override void PostMapInitialized(Map map, GenStepParams parms)
        {
            base.PostMapInitialized(map, parms);
            // 所有生成步骤及地图初始化完成后收尾，仅作用于此次新地图。
            RefogInterior(map);
        }

        private static void RefogInterior(Map map)
        {
            MapComponent_SunBossArena arena = map.GetComponent<MapComponent_SunBossArena>();
            if (!arena.Generated) return;
            // 最外层墙和外门保持可见，让玩家可以从室外选中防爆门开始骇入。
            map.fogGrid.Refog(arena.FacilityBounds.ContractedBy(1));
        }
    }
}
