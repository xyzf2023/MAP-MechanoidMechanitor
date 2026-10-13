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

        /// <summary>远行队牵引者：保留殖民者资格，机械体须有远行能力及驯兽授权。</summary>
        public static bool CanActAsCaravanAnimalRoper(Pawn? pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            if (pawn.IsColonist)
            {
                return true;
            }

            return pawn.RaceProps.IsMechanoid
                && pawn.Spawned
                && !pawn.Destroyed
                && !pawn.InMentalState
                && pawn.roping != null
                && (MAPMechanitorTravelUtility.CanLeadCaravan(pawn)
                    || MAPMechanitorTravelUtility.CanCollectCaravanItems(pawn))
                && AnimalHandlingWorkUtility.IsAuthorized(pawn);
        }

        public static bool ShouldCheckCaravanExitReachability(Pawn? pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            return pawn.IsColonist
                || CanActAsIndependentCaravanOwner(pawn)
                || CanActAsCaravanCollector(pawn);
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

        public static bool IsEligibleRegisteredMechanoidMechanitor(Pawn? pawn)
        {
            return pawn != null
                && pawn.Faction == Faction.OfPlayer
                && !pawn.Dead
                && !pawn.Destroyed
                && pawn.Spawned
                && MAPMechanitorTravelUtility.CanLeadCaravan(pawn);
        }

        public static bool IsColonistOrEligibleMechanoidMechanitor(Pawn? pawn)
        {
            return pawn != null
                && (pawn.IsColonist || IsEligibleRegisteredMechanoidMechanitor(pawn));
        }
    }
}
