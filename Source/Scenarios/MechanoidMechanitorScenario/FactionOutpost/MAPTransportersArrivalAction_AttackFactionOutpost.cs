using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MAPTransportersArrivalAction_AttackFactionOutpost : TransportersArrivalAction
    {
        private MAPFactionOutpost outpost = null!;
        private PawnsArrivalModeDef arrivalMode = null!;

        public override bool GeneratesMap => true;

        public MAPTransportersArrivalAction_AttackFactionOutpost()
        {
        }

        public MAPTransportersArrivalAction_AttackFactionOutpost(
            MAPFactionOutpost outpost,
            PawnsArrivalModeDef arrivalMode)
        {
            this.outpost = outpost;
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

            if (outpost == null || !outpost.Spawned || outpost.Tile != destinationTile)
            {
                return false;
            }

            return CanAttack(pods, outpost);
        }

        public override bool ShouldUseLongEvent(List<ActiveTransporterInfo> pods, PlanetTile tile)
        {
            return !outpost.HasMap;
        }

        public override void Arrived(List<ActiveTransporterInfo> transporters, PlanetTile tile)
        {
            if (!FactionOutpostInteraction.TryFinalizeAttackEntry(
                    outpost,
                    out string? failMessage))
            {
                RejectAndFormCaravan(transporters, tile, failMessage);
                return;
            }

            Thing lookTarget = TransportersArrivalActionUtility.GetLookTarget(transporters);
            bool newMap = !outpost.HasMap;
            Map map = GetOrGenerateMapUtility.GetOrGenerateMap(
                outpost.Tile,
                outpost.PreferredMapSize,
                null);
            if (map == null || !outpost.TryValidateMapReadyForEntry(out failMessage))
            {
                RejectAndFormCaravan(
                    transporters,
                    tile,
                    failMessage
                        ?? "MAP_MechanoidMechanitor.FactionOutpost.Attack.MapInitFailed".Translate());
                return;
            }

            TaggedString letterLabel = "LetterLabelCaravanEnteredEnemyBase".Translate();
            TaggedString letterText = "LetterTransportPodsLandedInEnemyBase".Translate(outpost.Label).CapitalizeFirst();
            AttackArrivalNotificationUtility.PrepareLetter(
                map, newMap, true, ref letterLabel, ref letterText);
            Find.LetterStack.ReceiveLetter(
                letterLabel,
                letterText,
                LetterDefOf.NeutralEvent,
                lookTarget,
                outpost.Faction);

            arrivalMode.Worker.TravellingTransportersArrived(transporters, map);
            AttackArrivalNotificationUtility.NotifyEntered();
        }

        private static void RejectAndFormCaravan(
            List<ActiveTransporterInfo> transporters,
            PlanetTile tile,
            string? failMessage)
        {
            Messages.Message(
                (failMessage
                    ?? "MAP_MechanoidMechanitor.FactionOutpost.Attack.RelationBlocked".Translate())
                    .CapitalizeFirst(),
                new GlobalTargetInfo(tile),
                MessageTypeDefOf.NegativeEvent);
            new TransportersArrivalAction_FormCaravan("MessageTransportPodsArrived")
                .Arrived(transporters, tile);
        }

        public static FloatMenuAcceptanceReport CanAttack(
            IEnumerable<IThingHolder> pods,
            MAPFactionOutpost outpost)
        {
            FloatMenuAcceptanceReport report = FactionOutpostInteraction.CanAttack(outpost);
            if (!report)
            {
                return report;
            }

            return TransportersArrivalActionUtility.AnyNonDownedColonist(pods);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref outpost, "MAP_factionOutpost");
            Scribe_Defs.Look(ref arrivalMode, "MAP_arrivalMode");
        }
    }
}
