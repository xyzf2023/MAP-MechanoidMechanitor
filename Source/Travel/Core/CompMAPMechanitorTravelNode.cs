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

    }
}
