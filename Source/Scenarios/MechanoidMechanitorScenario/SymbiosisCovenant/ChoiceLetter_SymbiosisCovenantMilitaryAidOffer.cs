using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class ChoiceLetter_SymbiosisCovenantMilitaryAidOffer : ChoiceLetter
    {
        public Map? triggerMap;
        public Faction? supportFaction;
        public Faction? attackerFaction;
        public float triggerRaidPoints;
        public float supportPoints;
        public int offerCreatedTick;

        public ChoiceLetter_SymbiosisCovenantMilitaryAidOffer()
        {
        }

        public ChoiceLetter_SymbiosisCovenantMilitaryAidOffer(
            Map map,
            Faction supportFaction,
            Faction attackerFaction,
            float triggerRaidPoints,
            float supportPoints)
        {
            triggerMap = map;
            this.supportFaction = supportFaction;
            this.attackerFaction = attackerFaction;
            this.triggerRaidPoints = triggerRaidPoints;
            this.supportPoints = supportPoints;
            offerCreatedTick = Find.TickManager?.TicksGame ?? 0;

            def = LetterDefOf.PositiveEvent;
            relatedFaction = supportFaction;
            Label = "MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Letter.Label".Translate();
            title = Label;
            Text = "MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Letter.Text"
                .Translate(
                    supportFaction.NameColored,
                    attackerFaction.NameColored);
            lookTargets = new LookTargets(map.Center, map);
        }

        public override IEnumerable<DiaOption> Choices
        {
            get
            {
                if (ArchivedOnly)
                {
                    yield return base.Option_Close;
                    yield break;
                }

                yield return new DiaOption(
                    "MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Option.Accept".Translate())
                {
                    action = AcceptAid,
                    resolveTree = true
                };
                yield return new DiaOption(
                    "MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Option.Decline".Translate())
                {
                    action = () => Find.LetterStack.RemoveLetter(this),
                    resolveTree = true
                };
                yield return base.Option_Postpone;
            }
        }

        public void Send()
        {
            int timeoutTicks = SymbiosisCovenantMilitaryAidDefOf
                .MAP_SymbiosisCovenant_MilitaryAidConfig.offerTimeoutTicks;
            StartTimeout(timeoutTicks);
            Find.LetterStack.ReceiveLetter(this);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref triggerMap, "triggerMap");
            Scribe_References.Look(ref supportFaction, "supportFaction");
            Scribe_References.Look(ref attackerFaction, "attackerFaction");
            Scribe_Values.Look(ref triggerRaidPoints, "triggerRaidPoints", 0f);
            Scribe_Values.Look(ref supportPoints, "supportPoints", 0f);
            Scribe_Values.Look(ref offerCreatedTick, "offerCreatedTick", 0);
        }

        private void AcceptAid()
        {
            if (!SymbiosisCovenantMilitaryAidUtility.TryAcceptOffer(
                    this,
                    out TaggedString failureReason)
                && !failureReason.NullOrEmpty())
            {
                Messages.Message(
                    failureReason,
                    MessageTypeDefOf.RejectInput,
                    historical: false);
            }

            // 成功时由原版 RaidFriendly 自己发送援军抵达信件，避免重复提示。
            Find.LetterStack.RemoveLetter(this);
        }
    }
}
