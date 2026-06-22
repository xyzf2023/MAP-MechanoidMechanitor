using RimWorld;

namespace MAP_MechanoidMechanitor
{
    public class CompProperties_AbilityMechRecode : CompProperties_AbilityEffect
    {
        public int recodeDurationTicks = 900;

        public float cooldownTicksPerBandwidth = 18000f;

        public CompProperties_AbilityMechRecode()
        {
            compClass = typeof(CompAbilityEffect_MechRecode);
        }
    }
}
