using RimWorld;
using Verse;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor
{
    public class LordJob_JusticeBossAssault : LordJob_AssaultColony
    {
        private int justiceEventId;

        public int JusticeEventId => justiceEventId;

        public LordJob_JusticeBossAssault()
        {
        }

        public LordJob_JusticeBossAssault(Faction? faction, int justiceEventId)
            : base(
                faction,
                canKidnap: false,
                canTimeoutOrFlee: false,
                sappers: false,
                useAvoidGridSmart: true,
                canSteal: false)
        {
            this.justiceEventId = justiceEventId;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref justiceEventId, "justiceEventId", 0);
        }
    }
}