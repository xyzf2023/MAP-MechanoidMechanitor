using RimWorld;

namespace MAP_MechanoidMechanitor
{
    public class CompProperties_AbilityMechRecode : CompProperties_AbilityEffect
    {
        public float recodeTicksPerBandwidth = 300f;

        public float cooldownTicksPerBandwidth = 18000f;

        public CompProperties_AbilityMechRecode()
        {
            compClass = typeof(CompAbilityEffect_MechRecode);
        }
    }
}
