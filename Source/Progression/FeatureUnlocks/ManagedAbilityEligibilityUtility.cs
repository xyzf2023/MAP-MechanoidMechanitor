using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 统一能力资格入口。任何能力同步代码都不应自行拼接科研、机体或模块判断。
    /// </summary>
    public static class ManagedAbilityEligibilityUtility
    {
        public static bool ShouldPawnHaveAbility(
            Pawn? pawn,
            ManagedResearchAbilityDescriptor descriptor)
        {
            if (pawn == null
                || pawn.Destroyed
                || pawn.Dead
                || descriptor == null
                || pawn.Faction == null
                || !pawn.Faction.IsPlayerSafe())
            {
                return false;
            }

            bool hasCapability = MechCapabilityUtility.HasCapability(
                pawn,
                descriptor.RequiredCapabilityId);

            switch (descriptor.GrantPolicy)
            {
                case ManagedAbilityGrantPolicy.ResearchConsciousness:
                    return descriptor.IsResearchRequirementSatisfied()
                        && IsResearchConsciousnessRecipient(pawn);

                case ManagedAbilityGrantPolicy.Capability:
                    return hasCapability;

                case ManagedAbilityGrantPolicy.ResearchAndCapability:
                    return descriptor.IsResearchRequirementSatisfied()
                        && hasCapability;

                default:
                    return false;
            }
        }

        private static bool IsResearchConsciousnessRecipient(Pawn? pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            if (JusticePawnUtility.IsJustice(pawn))
            {
                return true;
            }

            if (GameComponent_MechanoidMechanitorScenarioState.IsEnabled
                && GameComponent_MechanoidMechanitorRegistry
                    .IsMechanicalConsciousnessHost(pawn))
            {
                return true;
            }

            return ResearchFeatureUnlockUtility.IsOrbitalDataNetworkUnlocked()
                && GameComponent_MechanoidMechanitorScenarioState.IsEnabled
                && GameComponent_MechanoidMechanitorRegistry
                    .IsPawnAliveAndInitialized(pawn)
                && MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn);
        }
    }
}
