using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    internal static class MechanoidMechanitorSkillUtility
    {
        internal static void PromoteNonePassionsToMinor(Pawn? pawn)
        {
            Pawn_SkillTracker? tracker = pawn?.skills;
            if (tracker?.skills == null)
            {
                return;
            }

            for (int i = 0; i < tracker.skills.Count; i++)
            {
                SkillRecord? record = tracker.skills[i];
                if (record != null && record.passion == Passion.None)
                {
                    record.passion = Passion.Minor;
                }
            }
        }
    }
}
