using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 远行队进攻机械巢节点的抵达动作：生成/进入节点战斗地图。进攻前的关系确认与转敌
    /// 在世界地图菜单点击时完成，此处只负责进入地图。
    /// </summary>
    public class MAPCaravanArrivalAction_AttackMechHiveNode : CaravanArrivalAction
    {
        private MAPMechHiveNode node = null!;

        public override string Label =>
            "AttackSettlement".Translate(node.Label);

        public override string ReportString =>
            "CaravanAttacking".Translate(node.Label);

        public MAPCaravanArrivalAction_AttackMechHiveNode()
        {
        }

        public MAPCaravanArrivalAction_AttackMechHiveNode(MAPMechHiveNode node)
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

            return !node.Cleaned;
        }

        public override void Arrived(Caravan caravan)
        {
            if (!node.HasMap)
            {
                LongEventHandler.QueueLongEvent(
                    () => DoEnter(caravan),
                    "GeneratingMapForNewEncounter",
                    doAsynchronously: false,
                    null);
            }
            else
            {
                DoEnter(caravan);
            }
        }

        private void DoEnter(Caravan caravan)
        {
            bool newMap = !node.HasMap;
            Map map = GetOrGenerateMapUtility.GetOrGenerateMap(node.Tile, node.PreferredMapSize, null);
            if (newMap)
            {
                Find.TickManager.Notify_GeneratedPotentiallyHostileMap();
            }

            Find.LetterStack.ReceiveLetter(
                "LetterLabelCaravanEnteredMap".Translate(node),
                "LetterCaravanEnteredMap".Translate(caravan.Label, node).CapitalizeFirst(),
                LetterDefOf.NeutralEvent,
                new LookTargets(caravan.PawnsListForReading));

            CaravanEnterMapUtility.Enter(
                caravan,
                map,
                CaravanEnterMode.Edge,
                CaravanDropInventoryMode.DoNotDrop,
                draftColonists: true);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref node, "MAP_node");
        }
    }
}
