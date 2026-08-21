#nullable enable
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 远行队向建成非敌对机械族前哨赠送礼物。复用原版 Dialog_Trade（giftsOnly）与前哨 ITrader。
    /// </summary>
    public class MAPCaravanArrivalAction_OfferGiftsFactionOutpost : CaravanArrivalAction
    {
        private MAPFactionOutpost? outpost;

        public override string Label => "OfferGifts".Translate();

        public override string ReportString => "CaravanOfferingGifts".Translate(outpost!.Label);

        public MAPCaravanArrivalAction_OfferGiftsFactionOutpost()
        {
        }

        public MAPCaravanArrivalAction_OfferGiftsFactionOutpost(MAPFactionOutpost outpost)
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

            return CanOfferGiftsTo(caravan, outpost);
        }

        public override void Arrived(Caravan caravan)
        {
            CameraJumper.TryJumpAndSelect(caravan);
            Pawn playerNegotiator = BestCaravanPawnUtility.FindBestNegotiator(caravan);
            if (playerNegotiator == null)
            {
                Messages.Message(
                    "MessageNoNegotiator".Translate(),
                    caravan,
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            Find.WindowStack.Add(new Dialog_Trade(playerNegotiator, outpost!, giftsOnly: true));
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref outpost, "outpost");
        }

        public static FloatMenuAcceptanceReport CanOfferGiftsTo(
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

            Pawn negotiator = BestCaravanPawnUtility.FindBestNegotiator(caravan);
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
                () => CanOfferGiftsTo(caravan, outpost),
                () => new MAPCaravanArrivalAction_OfferGiftsFactionOutpost(outpost),
                "OfferGifts".Translate(),
                caravan,
                outpost.Tile, outpost);
        }
    }
}
