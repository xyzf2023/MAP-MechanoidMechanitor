using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 远行队进攻机械巢节点的抵达动作：生成/进入节点战斗地图。
    /// 生成地图前执行最终关系复查；普通中立/盟友会重新尝试转敌。
    /// 完整节点初始化失败时取消进入并安全卸载空地图，远行队保留在世界层。
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
            if (!MechHiveNodeCaravanInteraction.TryFinalizeAttackEntry(node, out string? failMessage))
            {
                Messages.Message(
                    "MessageCaravanArrivalActionNoLongerValid".Translate(caravan.Name).CapitalizeFirst()
                        + (failMessage != null ? (" " + failMessage) : ""),
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
            if (!MechHiveNodeCaravanInteraction.TryFinalizeAttackEntry(node, out string? failMessage))
            {
                Messages.Message(
                    "MessageCaravanArrivalActionNoLongerValid".Translate(caravan.Name).CapitalizeFirst()
                        + (failMessage != null ? (" " + failMessage) : ""),
                    caravan,
                    MessageTypeDefOf.NegativeEvent);
                return;
            }

            bool newMap = !node.HasMap;
            Map map = GetOrGenerateMapUtility.GetOrGenerateMap(node.Tile, node.PreferredMapSize, null);

            // GetOrGenerateMap 完成后、转移玩家 Pawn 之前检查完整节点初始化结果。
            if (node.IsCompleted && node.IsMapContentFailed)
            {
                Messages.Message(
                    "MAP_MechanoidMechanitor.MechHiveNode.Attack.MapInitFailed".Translate(),
                    caravan,
                    MessageTypeDefOf.NegativeEvent);
                node.TryUnloadFailedEmptyMap();
                return;
            }

            if (node.IsCompleted && !node.IsMapContentReady)
            {
                Messages.Message(
                    "MAP_MechanoidMechanitor.MechHiveNode.Attack.MapInitFailed".Translate(),
                    caravan,
                    MessageTypeDefOf.NegativeEvent);
                node.TryUnloadFailedEmptyMap();
                return;
            }

            if (map == null)
            {
                Messages.Message(
                    "MAP_MechanoidMechanitor.MechHiveNode.Attack.MapInitFailed".Translate(),
                    caravan,
                    MessageTypeDefOf.NegativeEvent);
                return;
            }

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
