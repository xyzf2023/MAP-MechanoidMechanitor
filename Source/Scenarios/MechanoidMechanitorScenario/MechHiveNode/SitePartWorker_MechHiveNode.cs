using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 机械巢节点主 SitePart 的 Worker。地图生成后按节点当前阶段生成建筑布局与休眠守军。
    /// 建设中与完整节点共用本 Worker，通过节点阶段区分布局与守军点数。
    /// </summary>
    public class SitePartWorker_MechHiveNode : SitePartWorker
    {
        public override void PostMapGenerate(Map map)
        {
            base.PostMapGenerate(map);
            if (map?.Parent is MAPMechHiveNode node)
            {
                MechHiveNodeMapGenerator.Generate(map, node);
            }
        }
    }
}
