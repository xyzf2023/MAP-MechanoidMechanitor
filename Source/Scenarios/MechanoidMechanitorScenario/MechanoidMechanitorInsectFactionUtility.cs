using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public static class MechanoidMechanitorInsectFactionUtility
    {
        public static Faction? TryGetInsectFaction()
        {
            FactionManager? factionManager = Find.FactionManager;
            if (factionManager == null)
            {
                return null;
            }

            Faction? insects = factionManager.OfInsects;
            if (insects == null)
            {
                return null;
            }

            if (!factionManager.AllFactionsListForReading.Contains(insects))
            {
                return null;
            }

            return insects;
        }

        public static bool IsInsectFaction(Faction? faction)
        {
            if (faction == null)
            {
                return false;
            }

            FactionManager? factionManager = Find.FactionManager;
            if (factionManager == null)
            {
                return false;
            }

            return faction == factionManager.OfInsects
                || faction.def == FactionDefOf.Insect;
        }
    }
}
