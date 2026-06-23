using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class MAPMechanitorTravelUtility
    {
        public static bool TryGetTravelProps(Pawn? pawn, out CompProperties_MAPMechanitorTravelNode? props)
        {
            props = null;
            if (pawn == null || !ModsConfig.BiotechActive)
            {
                return false;
            }

            if (!CompMAPMechanitorTravelNode.TryGetTravelNodeComp(
                    pawn,
                    out CompMAPMechanitorTravelNode? comp)
                || comp?.TravelProps == null)
            {
                return false;
            }

            props = comp.TravelProps;
            return true;
        }

        public static bool CanLeadCaravan(Pawn? pawn)
        {
            if (!PassesTravelPawnBasics(pawn))
            {
                return false;
            }

            if (MechanoidMechanitorRoleUtility.IsAcquiredMechanoidMechanitor(pawn))
            {
                return true;
            }

            return TryGetTravelProps(pawn, out CompProperties_MAPMechanitorTravelNode? props)
                && props?.canLeadCaravan == true;
        }

        public static bool CanCollectCaravanItems(Pawn? pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            if (pawn.IsColonist)
            {
                return true;
            }

            if (!PassesTravelPawnBasics(pawn))
            {
                return false;
            }

            if (MechanoidMechanitorRoleUtility.IsAcquiredMechanoidMechanitor(pawn))
            {
                return true;
            }

            return TryGetTravelProps(pawn, out CompProperties_MAPMechanitorTravelNode? props)
                && props?.canCollectCaravanItems == true;
        }

        public static bool ShouldRefreshTrackersOnTransporterArrival(Pawn? pawn)
        {
            if (!PassesTravelPawnBasics(pawn))
            {
                return false;
            }

            if (MechanoidMechanitorRoleUtility.IsAcquiredMechanoidMechanitor(pawn))
            {
                return true;
            }

            return TryGetTravelProps(pawn, out CompProperties_MAPMechanitorTravelNode? props)
                && props?.refreshTrackersOnTransporterArrival == true;
        }

        public static bool ShouldBlockCampMapRemoval(Pawn? pawn)
        {
            if (pawn == null || !ModsConfig.BiotechActive)
            {
                return false;
            }

            if (!pawn.Spawned || pawn.Dead || pawn.Downed)
            {
                return false;
            }

            return CanLeadCaravan(pawn);
        }

        private static bool PassesTravelPawnBasics(Pawn? pawn)
        {
            if (pawn == null || !ModsConfig.BiotechActive)
            {
                return false;
            }

            if (pawn.Faction == null || !pawn.Faction.IsPlayerSafe())
            {
                return false;
            }

            if (pawn.Dead || pawn.Downed)
            {
                return false;
            }

            return MAPMechanitorNodeUtility.IsMechanitorNodeController(pawn);
        }
    }
}
