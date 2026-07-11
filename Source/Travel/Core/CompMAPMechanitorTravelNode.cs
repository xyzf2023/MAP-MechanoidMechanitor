using Verse;

namespace MAP_MechanoidMechanitor
{
    public class CompProperties_MAPMechanitorTravelNode : CompProperties
    {
        public bool canLeadCaravan = true;
        public bool canCollectCaravanItems = true;
        public bool refreshTrackersOnTransporterArrival = true;

        public CompProperties_MAPMechanitorTravelNode()
        {
            compClass = typeof(CompMAPMechanitorTravelNode);
        }
    }

    public class CompMAPMechanitorTravelNode : ThingComp
    {
        public CompProperties_MAPMechanitorTravelNode? TravelProps => props as CompProperties_MAPMechanitorTravelNode;

        public static bool PawnHasTravelNode(Pawn? pawn)
        {
            return TryGetTravelNodeComp(pawn, out _);
        }

        public static bool TryGetTravelNodeComp(Pawn? pawn, out CompMAPMechanitorTravelNode? comp)
        {
            comp = null;
            if (pawn == null)
            {
                return false;
            }

            comp = pawn.GetComp<CompMAPMechanitorTravelNode>();
            return comp != null;
        }
    }
}
