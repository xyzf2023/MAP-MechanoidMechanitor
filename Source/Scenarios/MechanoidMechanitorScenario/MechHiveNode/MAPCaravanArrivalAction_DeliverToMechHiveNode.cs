using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 远行队前往机械巢节点交付材料的抵达动作：不生成战斗地图，抵达后停留在节点地块。
    /// 实际交付通过远行队上的“提供材料”Gizmo 完成，点击时再次校验全部条件。
    /// </summary>
    public class MAPCaravanArrivalAction_DeliverToMechHiveNode : CaravanArrivalAction
    {
        private MAPMechHiveNode node = null!;

        public override string Label =>
            "MAP_MechanoidMechanitor.MechHiveNode.Caravan.GoDeliver".Translate(node.Label);

        public override string ReportString =>
            "MAP_MechanoidMechanitor.MechHiveNode.Caravan.GoDeliver".Translate(node.Label);

        public MAPCaravanArrivalAction_DeliverToMechHiveNode()
        {
        }

        public MAPCaravanArrivalAction_DeliverToMechHiveNode(MAPMechHiveNode node)
        {
            this.node = node;
        }

        public override FloatMenuAcceptanceReport StillValid(Caravan caravan, PlanetTile destinationTile)
        {
            FloatMenuAcceptanceReport report = base.StillValid(caravan, destinationTile);
            if (!report)
            {
                return report;
            }

            if (node == null || !node.Spawned || node.Tile != destinationTile)
            {
                return false;
            }

            return node.AllowsMaterialDelivery;
        }

        public override void Arrived(Caravan caravan)
        {
            // 不生成地图，远行队停留在节点地块；交付由“提供材料”Gizmo 处理。
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref node, "MAP_node");
        }
    }
}
