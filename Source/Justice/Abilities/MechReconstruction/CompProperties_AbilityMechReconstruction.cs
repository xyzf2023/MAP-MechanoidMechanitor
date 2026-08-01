using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class CompProperties_AbilityMechReconstruction : CompProperties_AbilityEffect
    {
        public string invalidTargetMessageKey =
            "MAP_MechanoidMechanitor.Justice.Ability.MechReconstruction.InvalidTarget";

        public int reconstructionDurationTicks = 900;

        public EffecterDef? appliedEffecterDef;

        public CompProperties_AbilityMechReconstruction()
        {
            compClass = typeof(CompAbilityEffect_MechReconstruction);
        }
    }
}
