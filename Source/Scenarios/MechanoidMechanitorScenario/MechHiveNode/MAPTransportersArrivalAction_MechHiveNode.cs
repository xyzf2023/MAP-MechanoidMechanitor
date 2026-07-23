using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 运输舱抵达机械巢节点的自定义抵达动作。不生成地图，抵达时按当前实际状态（阶段、清理、
    /// 关系、需求、肃清指令、实际载荷）重新分类结算，避免信任发射时缓存的数据，且任何分支互斥不重复结算。
    /// </summary>
    public class MAPTransportersArrivalAction_MechHiveNode : TransportersArrivalAction
    {
        private MAPMechHiveNode node = null!;

        public override bool GeneratesMap => false;

        public MAPTransportersArrivalAction_MechHiveNode()
        {
        }

        public MAPTransportersArrivalAction_MechHiveNode(MAPMechHiveNode node)
        {
            this.node = node;
        }

        public override FloatMenuAcceptanceReport StillValid(
            IEnumerable<IThingHolder> pods,
            PlanetTile destinationTile)
        {
            FloatMenuAcceptanceReport report = base.StillValid(pods, destinationTile);
            if (!report)
            {
                return report;
            }

            if (node == null || !node.Spawned || node.Tile != destinationTile || node.Cleaned)
            {
                return false;
            }

            return true;
        }

        public override void Arrived(List<ActiveTransporterInfo> transporters, PlanetTile tile)
        {
            MechHiveNodeTransportInteraction.SettleArrival(node, transporters, tile);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref node, "MAP_node");
        }
    }
}
