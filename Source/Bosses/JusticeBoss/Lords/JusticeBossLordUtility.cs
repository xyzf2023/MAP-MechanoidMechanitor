using RimWorld;
using Verse;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor
{
    public static class JusticeBossLordUtility
    {
        public static Lord EnsureAssaultLord(Map map, Faction? faction, int justiceEventId)
        {
            foreach (Lord lord in map.lordManager.lords)
            {
                if (lord.LordJob is LordJob_JusticeBossAssault assault
                    && assault.JusticeEventId == justiceEventId
                    && lord.faction == faction)
                {
                    return lord;
                }
            }

            LordJob_JusticeBossAssault job = new LordJob_JusticeBossAssault(faction, justiceEventId);
            return LordMaker.MakeNewLord(faction, job, map);
        }

        public static Lord EnsureGuardLord(
            Map map,
            Faction? faction,
            int justiceEventId,
            IntVec3 anchor)
        {
            foreach (Lord lord in map.lordManager.lords)
            {
                if (lord.LordJob is LordJob_JusticeBossGuards guards
                    && guards.JusticeEventId == justiceEventId
                    && lord.faction == faction)
                {
                    return lord;
                }
            }

            LordJob_JusticeBossGuards job =
                new LordJob_JusticeBossGuards(faction, anchor, justiceEventId);
            return LordMaker.MakeNewLord(faction, job, map);
        }
    }
}