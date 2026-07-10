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
            if (!PassesTravelSafetyChecks(pawn))
            {
                return false;
            }

            return MechanoidMechanitorCapabilityUtility.HasCapability(
                pawn,
                MechanoidMechanitorCapability.TravelLeadCaravan);
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

            if (!PassesTravelSafetyChecks(pawn))
            {
                return false;
            }

            return MechanoidMechanitorCapabilityUtility.HasCapability(
                pawn,
                MechanoidMechanitorCapability.TravelCollectItems);
        }

        public static bool ShouldRefreshTrackersOnTransporterArrival(Pawn? pawn)
        {
            if (!PassesTravelSafetyChecks(pawn))
            {
                return false;
            }

            return MechanoidMechanitorCapabilityUtility.HasCapability(
                pawn,
                MechanoidMechanitorCapability.TravelRefreshTrackers);
        }

        public static bool ShouldBlockMapRemoval(Pawn? pawn)
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

        private static bool PassesTravelSafetyChecks(Pawn? pawn)
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

            return true;
        }
    }
}
