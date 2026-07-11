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

        public static bool Allows(Pawn? pawn, WorkGiver? workGiver)
        {
            WorkTypeDef? workType = workGiver?.def?.workType;
            if (pawn == null || workType == null)
            {
                return false;
            }

            CompMechRestrictedWorkGiverUser? comp =
                pawn.GetComp<CompMechRestrictedWorkGiverUser>();

            return comp?.Props.allowedWorkTypes?.Contains(workType) == true;
        }
    }
}
