using RimWorld;

namespace MAP_MechanoidMechanitor
{
    public class CompProperties_AbilityMechHack : CompProperties_AbilityEffect
    {
        public string invalidTargetMessageKey =
            "MAP_MechanoidMechanitor.MechHack.InvalidTarget";

        public int hackDurationTicks = 900;

        public int cooldownTicks = 300000;

        public CompProperties_AbilityMechHack()
        {
            compClass = typeof(CompAbilityEffect_MechHack);
        }
    }
}
