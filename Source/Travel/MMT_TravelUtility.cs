using RimWorld;
using Verse;

namespace MMT
{
    public static class MMT_TravelUtility
    {
        public static bool CanActAsIndependentCaravanOwner(Pawn pawn)
        {
            return pawn != null
                && pawn.Faction == Faction.OfPlayer
                && !pawn.Dead
                && !pawn.Downed
                && OverseerlessMechanitorUtility.IsNode(pawn);
        }

        public static bool CanActAsCaravanCollector(Pawn pawn)
        {
            if (pawn == null
                || pawn.Faction != Faction.OfPlayer
                || pawn.Dead
                || pawn.Downed)
            {
                return false;
            }

            if (pawn.IsColonist)
            {
                return true;
            }

            return CanActAsIndependentCaravanOwner(pawn);
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
