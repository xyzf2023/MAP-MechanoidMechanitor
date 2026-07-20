using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class HediffComp_VoidEngine : HediffComp_DistributedImplantEffect
    {
        protected override HediffDef DistributedHediffDef =>
            MAPMechanitor_HediffDefOf.MAP_ReactorSelfPowering;

        protected override bool CanApplyTo(Pawn recipient, bool isSelf)
        {
            return !isSelf
                || !ResearchFeatureUnlockUtility
                    .IsAutonomousDirectiveOptimizationUnlocked();
        }
    }

    public sealed class HediffCompProperties_VoidEngine : HediffCompProperties
    {
        public HediffCompProperties_VoidEngine()
        {
            compClass = typeof(HediffComp_VoidEngine);
        }
    }
}
