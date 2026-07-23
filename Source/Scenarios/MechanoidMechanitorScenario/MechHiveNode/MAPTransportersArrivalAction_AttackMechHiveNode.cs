using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 运输舱进攻机械巢节点：生成/进入战斗地图。抵达时执行与远行队相同的最终关系复查。
    /// 初始化失败检查发生在 TravellingTransportersArrived 之前，确保舱内内容可回退为远行队。
    /// </summary>
    public class MAPTransportersArrivalAction_AttackMechHiveNode : TransportersArrivalAction
    {
        private MAPMechHiveNode node = null!;

        private PawnsArrivalModeDef arrivalMode = null!;

        public override bool GeneratesMap => true;

        public MAPTransportersArrivalAction_AttackMechHiveNode()
        {
        }

        public MAPTransportersArrivalAction_AttackMechHiveNode(
            MAPMechHiveNode node,
            PawnsArrivalModeDef arrivalMode)
        {
            this.node = node;
            this.arrivalMode = arrivalMode;
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

            if (node == null || !node.Spawned || node.Tile != destinationTile)
            {
                return false;
            }

            return CanAttack(pods, node);
        }

        public override bool ShouldUseLongEvent(List<ActiveTransporterInfo> pods, PlanetTile tile)
        {
            return !node.HasMap;
        }

        public override void Arrived(List<ActiveTransporterInfo> transporters, PlanetTile tile)
        {
            if (!MechHiveNodeCaravanInteraction.TryFinalizeAttackEntry(node, out string? failMessage))
            {
                Messages.Message(
                    (failMessage
                        ?? "MAP_MechanoidMechanitor.MechHiveNode.Attack.RelationBlocked".Translate())
                        .CapitalizeFirst(),
                    new GlobalTargetInfo(tile),
                    MessageTypeDefOf.NegativeEvent);
                // 转敌失败：回退为在此地组建远行队，避免舱内内容丢失。
                new TransportersArrivalAction_FormCaravan("MessageTransportPodsArrived")
                    .Arrived(transporters, tile);
                return;
            }

            Thing lookTarget = TransportersArrivalActionUtility.GetLookTarget(transporters);
            bool newMap = !node.HasMap;
            Map map = GetOrGenerateMapUtility.GetOrGenerateMap(node.Tile, node.PreferredMapSize, null);

            // GetOrGenerateMap 完成后、生成玩家内容之前检查；失败则组建远行队并卸载空地图。
            if (map == null
                || (node.IsCompleted && (node.IsMapContentFailed || !node.IsMapContentReady)))
            {
                Messages.Message(
                    "MAP_MechanoidMechanitor.MechHiveNode.Attack.MapInitFailed".Translate(),
                    new GlobalTargetInfo(tile),
                    MessageTypeDefOf.NegativeEvent);
                new TransportersArrivalAction_FormCaravan("MessageTransportPodsArrived")
                    .Arrived(transporters, tile);
                node.TryUnloadFailedEmptyMap();
                return;
            }

            if (newMap)
            {
                Find.TickManager.Notify_GeneratedPotentiallyHostileMap();
            }

            Find.LetterStack.ReceiveLetter(
                "LetterLabelCaravanEnteredEnemyBase".Translate(),
                "LetterTransportPodsLandedInEnemyBase".Translate(node.Label).CapitalizeFirst(),
                LetterDefOf.NeutralEvent,
                lookTarget,
                node.Faction);

            arrivalMode.Worker.TravellingTransportersArrived(transporters, map);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref node, "MAP_node");
            Scribe_Defs.Look(ref arrivalMode, "MAP_arrivalMode");
        }

        public static FloatMenuAcceptanceReport CanAttack(
            IEnumerable<IThingHolder> pods,
            MAPMechHiveNode node)
        {
            FloatMenuAcceptanceReport baseReport = MechHiveNodeCaravanInteraction.CanAttack(node);
            if (!baseReport)
            {
                return baseReport;
            }

            if (!TransportersArrivalActionUtility.AnyNonDownedColonist(pods))
            {
                return false;
            }

            return true;
        }
    }
}
