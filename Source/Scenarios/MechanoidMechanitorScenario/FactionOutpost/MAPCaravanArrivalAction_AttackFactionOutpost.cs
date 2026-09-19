using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MAPCaravanArrivalAction_AttackFactionOutpost : CaravanArrivalAction
    {
        private MAPFactionOutpost outpost = null!;

        public override string Label => "AttackSettlement".Translate(outpost.Label);

        public override string ReportString => "CaravanAttacking".Translate(outpost.Label);

        public MAPCaravanArrivalAction_AttackFactionOutpost()
        {
        }

        public MAPCaravanArrivalAction_AttackFactionOutpost(MAPFactionOutpost outpost)
        {
            this.outpost = outpost;
        }

        public override FloatMenuAcceptanceReport StillValid(
            Caravan caravan,
            PlanetTile destinationTile)
        {
            FloatMenuAcceptanceReport report = base.StillValid(caravan, destinationTile);
            if (!report)
            {
                return report;
            }

            if (outpost == null || !outpost.Spawned || outpost.Tile != destinationTile)
            {
                return false;
            }

            return FactionOutpostInteraction.CanAttack(outpost);
        }

        public override void Arrived(Caravan caravan)
        {
            if (!FactionOutpostInteraction.TryFinalizeAttackEntry(
                    outpost,
                    out string? failMessage))
            {
                RejectArrival(caravan, failMessage);
                return;
            }

            if (!outpost.HasMap)
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
            if (!FactionOutpostInteraction.TryFinalizeAttackEntry(
                    outpost,
                    out string? failMessage))
            {
                RejectArrival(caravan, failMessage);
                return;
            }

            bool newMap = !outpost.HasMap;
            Map map = GetOrGenerateMapUtility.GetOrGenerateMap(
                outpost.Tile,
                outpost.PreferredMapSize,
                null);
            if (map == null || !outpost.TryValidateMapReadyForEntry(out failMessage))
            {
                RejectArrival(
                    caravan,
                    failMessage
                        ?? "MAP_MechanoidMechanitor.FactionOutpost.Attack.MapInitFailed".Translate());
                return;
            }

            TaggedString letterLabel = "LetterLabelCaravanEnteredEnemyBase".Translate();
            TaggedString letterText = "LetterCaravanEnteredEnemyBase".Translate(
                    caravan.Label,
                    outpost.Label.ApplyTag(TagType.Settlement, outpost.Faction?.GetUniqueLoadID() ?? ""))
                    .CapitalizeFirst();
            AttackArrivalNotificationUtility.PrepareLetter(
                map, newMap, false, ref letterLabel, ref letterText);
            Find.LetterStack.ReceiveLetter(
                letterLabel,
                letterText,
                LetterDefOf.NeutralEvent,
                caravan.PawnsListForReading,
                outpost.Faction);

            CaravanEnterMapUtility.Enter(
                caravan,
                map,
                CaravanEnterMode.Edge,
                CaravanDropInventoryMode.DoNotDrop,
                draftColonists: true);
            AttackArrivalNotificationUtility.NotifyEntered();
        }

        private static void RejectArrival(Caravan caravan, string? failMessage)
        {
            Messages.Message(
                "MessageCaravanArrivalActionNoLongerValid".Translate(caravan.Name).CapitalizeFirst()
                    + (!failMessage.NullOrEmpty() ? (" " + failMessage) : ""),
                caravan,
                MessageTypeDefOf.NegativeEvent);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref outpost, "MAP_factionOutpost");
        }
    }
}
