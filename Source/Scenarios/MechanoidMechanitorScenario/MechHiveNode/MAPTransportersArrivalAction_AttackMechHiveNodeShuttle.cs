using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 穿梭机进攻机械巢节点的抵达动作。
    /// 机械巢只负责自己的关系、冷却与地图状态校验；地图准备完成后，
    /// 实际 Shuttle 落地、载荷回装与卸载任务继续交给原版 TransportersArrivalAction_TransportShip。
    /// </summary>
    public class MAPTransportersArrivalAction_AttackMechHiveNodeShuttle : TransportersArrivalAction
    {
        private MAPMechHiveNode node = null!;
        private TransportShip transportShip = null!;

        public override bool GeneratesMap => true;

        public MAPTransportersArrivalAction_AttackMechHiveNodeShuttle()
        {
        }

        public MAPTransportersArrivalAction_AttackMechHiveNodeShuttle(
            MAPMechHiveNode node,
            TransportShip transportShip)
        {
            this.node = node;
            this.transportShip = transportShip;
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

            // 不在这里重新判断关系/冷却/清理状态。
            // TravellingTransporters 会在 StillValid=false 时改走自己的通用 fallback；
            // 若目标地图已存在，这可能绕过机械巢规则直接落入地图。
            // 因此只确认仍指向同一节点，把动态资格复查统一留给 Arrived。
            return node != null && node.Tile == destinationTile;
        }

        public override bool ShouldUseLongEvent(
            List<ActiveTransporterInfo> pods,
            PlanetTile tile)
        {
            return node != null && (!node.HasMap || !node.IsMapContentReady);
        }

        public override void Arrived(
            List<ActiveTransporterInfo> transporters,
            PlanetTile tile)
        {
            if (transporters == null || transporters.Count == 0)
            {
                return;
            }

            if (node == null)
            {
                FallBackToCaravan(transporters, tile, null);
                return;
            }

            // 抵达时重新检查节点、进入冷却与当前关系；
            // 普通中立/盟友若在飞行途中发生变化，也必须在这里重新满足转敌条件。
            if (!MechHiveNodeCaravanInteraction.TryFinalizeAttackEntry(
                    node,
                    out string? failMessage))
            {
                FallBackToCaravan(transporters, tile, failMessage);
                return;
            }

            if (transportShip == null
                || transportShip.Disposed
                || transportShip.shipThing == null)
            {
                Log.Warning(
                    "[MAP-机械族机械师] 穿梭机进攻机械巢抵达时 TransportShip 已失效，回退为远行队。");
                FallBackToCaravan(transporters, tile, null);
                return;
            }

            // 已有且 Succeeded 的节点地图会在这里直接通过；
            // 只有 Failed/Generating 等异常地图才执行现有清理/卸载流程。
            if (!node.TryPrepareCompletedMapForEntry(out failMessage))
            {
                FallBackToCaravan(transporters, tile, failMessage);
                return;
            }

            bool newMap = !node.HasMap;
            Map map = GetOrGenerateMapUtility.GetOrGenerateMap(
                node.Tile,
                node.PreferredMapSize,
                null);

            if (map == null
                || !node.TryValidateCompletedMapReadyForEntry(out failMessage))
            {
                FallBackToCaravan(
                    transporters,
                    tile,
                    failMessage
                    ?? "MAP_MechanoidMechanitor.MechHiveNode.Attack.MapInitFailed".Translate());
                return;
            }

            // 先找好合法降落格，再交给原版 TransportShip 抵达动作，
            // 避免原版找不到降落格时已经没有机会回退保护载荷。
            IntVec3 landingCell = DropCellFinder.GetBestShuttleLandingSpot(
                map,
                Faction.OfPlayer);
            if (!landingCell.IsValid)
            {
                Log.Warning(
                    "[MAP-机械族机械师] 穿梭机进攻机械巢时未找到合法降落格，回退为远行队。");
                FallBackToCaravan(transporters, tile, null);
                return;
            }

            TaggedString letterLabel = "LetterLabelCaravanEnteredEnemyBase".Translate();
            TaggedString letterText = "LetterShuttleLandedInEnemyBase".Translate(node.Label).CapitalizeFirst();
            AttackArrivalNotificationUtility.PrepareLetter(
                map, newMap, true, ref letterLabel, ref letterText);
            LookTargets lookTargets = new LookTargets(landingCell, map);
            Find.LetterStack.ReceiveLetter(
                letterLabel,
                letterText,
                LetterDefOf.NeutralEvent,
                lookTargets,
                node.Faction);

            // 复用原版 Shuttle 实体抵达机制，不复制载荷转移、TransportShip.ArriveAt
            // 与玩家穿梭机卸载 Job 等细节。地图已经由机械巢流程准备完成，
            // 并预先指定 landingCell，原版不会再次自行生成地图或重新选点。
            TransportersArrivalAction_TransportShip originalAction =
                new TransportersArrivalAction_TransportShip(node, transportShip)
                {
                    cell = landingCell
                };
            originalAction.Arrived(transporters, tile);
            AttackArrivalNotificationUtility.NotifyEntered();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref node, "MAP_node");
            Scribe_References.Look(ref transportShip, "MAP_transportShip");
        }

        private static void FallBackToCaravan(
            List<ActiveTransporterInfo> transporters,
            PlanetTile tile,
            string? failMessage)
        {
            if (!failMessage.NullOrEmpty())
            {
                Messages.Message(
                    failMessage.CapitalizeFirst(),
                    new GlobalTargetInfo(tile),
                    MessageTypeDefOf.NegativeEvent,
                    historical: false);
            }

            new TransportersArrivalAction_FormCaravan("MessageShuttleArrived")
                .Arrived(transporters, tile);
        }
    }
}
