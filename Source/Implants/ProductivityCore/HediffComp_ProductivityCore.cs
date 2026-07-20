using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class HediffComp_ProductivityCore : HediffComp_DistributedImplantEffect
    {
        protected override HediffDef DistributedHediffDef =>
            MAPMechanitor_HediffDefOf.MAP_ProductivityCoreActive;
    }

    public sealed class HediffCompProperties_ProductivityCore : HediffCompProperties
    {
        public HediffCompProperties_ProductivityCore()
        {
            compClass = typeof(HediffComp_ProductivityCore);
        }
    }
}
