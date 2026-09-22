using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class CompProperties_MechRestrictedWorkGiverUser : CompProperties
    {
        public List<WorkTypeDef>? allowedWorkTypes;

        public CompProperties_MechRestrictedWorkGiverUser()
        {
            compClass = typeof(CompMechRestrictedWorkGiverUser);
        }
    }

    public class CompMechRestrictedWorkGiverUser : ThingComp
    {
        public CompProperties_MechRestrictedWorkGiverUser Props =>
            (CompProperties_MechRestrictedWorkGiverUser)props;

    }
}
