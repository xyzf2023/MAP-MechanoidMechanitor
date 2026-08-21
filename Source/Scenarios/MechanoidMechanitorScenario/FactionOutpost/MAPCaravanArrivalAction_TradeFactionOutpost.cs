#nullable enable
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 远行队与建成非敌对机械族前哨交易。复用原版 Dialog_Trade 与 MAPFactionOutpost 的 ITrader 实现。
    /// </summary>
    public class MAPCaravanArrivalAction_TradeFactionOutpost : CaravanArrivalAction
    {
        private MAPFactionOutpost? outpost;

        public override string Label => "TradeWith".Translate(outpost!.Label);

        public override string ReportString => "CaravanTrading".Translate(outpost!.Label);

        public MAPCaravanArrivalAction_TradeFactionOutpost()
        {
        }

        public MAPCaravanArrivalAction_TradeFactionOutpost(MAPFactionOutpost outpost)
        {
            this.outpost = outpost;
        }

        public override FloatMenuAcceptanceReport StillValid(
            Caravan caravan,
            PlanetTile destinationTile)
        {
            FloatMenuAcceptanceReport baseReport = base.StillValid(caravan, destinationTile);
            if (!baseReport)
            {
                return baseReport;
            }

            if (outpost != null && outpost.Tile != destinationTile)
            {
                return false;
            }

            return CanTradeWith(caravan, outpost);
        }

        public override void Arrived(Caravan caravan)
        {
            CameraJumper.TryJumpAndSelect(caravan);
            Pawn playerNegotiator = BestCaravanPawnUtility.FindBestNegotiator(
                caravan,
                outpost!.Faction,
                outpost.TraderKind);
            if (playerNegotiator == null)
            {
                Messages.Message(
                    "MessageNoNegotiator".Translate(),
                    caravan,
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            Find.WindowStack.Add(new Dialog_Trade(playerNegotiator, outpost!, giftsOnly: false));
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref outpost, "outpost");
        }

        public static FloatMenuAcceptanceReport CanTradeWith(
            Caravan caravan,
            MAPFactionOutpost? outpost)
        {
            if (outpost == null
                || !outpost.Spawned
                || outpost.HasMap
                || outpost.Faction == null
                || outpost.Faction == Faction.OfPlayer
                || outpost.Faction.def.permanentEnemy
                || outpost.Faction.HostileTo(Faction.OfPlayer)
                || !outpost.CanTradeNow)
            {
                return false;
            }

            Pawn negotiator = BestCaravanPawnUtility.FindBestNegotiator(
                caravan,
                outpost.Faction,
                outpost.TraderKind);
            if (negotiator == null || negotiator.skills.GetSkill(SkillDefOf.Social).TotallyDisabled)
            {
                return false;
            }

            return true;
        }

        public static IEnumerable<FloatMenuOption> GetFloatMenuOptions(
            Caravan caravan,
            MAPFactionOutpost outpost)
        {
            return CaravanArrivalActionUtility.GetFloatMenuOptions(
                () => CanTradeWith(caravan, outpost),
                () => new MAPCaravanArrivalAction_TradeFactionOutpost(outpost),
                "TradeWith".Translate(outpost.Label),
                caravan,
                outpost.Tile, outpost);
        }
    }
}
