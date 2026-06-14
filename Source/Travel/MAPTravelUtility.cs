using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class MAPTravelUtility
    {
        public static bool CanActAsIndependentCaravanOwner(Pawn pawn)
        {
            return MAPMechanitorTravelUtility.CanLeadCaravan(pawn);
        }

        public static bool CanActAsCaravanCollector(Pawn pawn)
        {
            return MAPMechanitorTravelUtility.CanCollectCaravanItems(pawn);
        }

        public static bool MapHasIndependentCaravanOwner(Map map)
        {
            if (map == null)
            {
                return false;
            }

            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (CanActAsIndependentCaravanOwner(pawn))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
