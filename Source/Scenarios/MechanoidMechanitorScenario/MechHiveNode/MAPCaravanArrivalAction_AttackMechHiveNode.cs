using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 远行队进攻机械巢节点的抵达动作：生成/进入节点战斗地图。
    /// 抵达前通过统一入口重新校验进攻限制；失效时安全取消且不生成地图。
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

            return MechHiveNodeCaravanInteraction.CanAttack(node);
        }

        public override void Arrived(Caravan caravan)
        {
            FloatMenuAcceptanceReport report = MechHiveNodeCaravanInteraction.CanAttack(node);
            if (!report)
            {
                string fail = report.FailMessage;
                Messages.Message(
                    "MessageCaravanArrivalActionNoLongerValid".Translate(caravan.Name).CapitalizeFirst()
                        + (fail != null ? (" " + fail) : ""),
                    caravan,
                    MessageTypeDefOf.NegativeEvent);
                return;
            }

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
            // 长事件开始前再次校验，避免途中关系变化后仍生成地图。
            if (!(bool)MechHiveNodeCaravanInteraction.CanAttack(node))
            {
                Messages.Message(
                    "MessageCaravanArrivalActionNoLongerValid".Translate(caravan.Name).CapitalizeFirst(),
                    caravan,
                    MessageTypeDefOf.NegativeEvent);
                return;
            }

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
