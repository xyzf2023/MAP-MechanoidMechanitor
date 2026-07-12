using RimWorld;

namespace MAP_MechanoidMechanitor
{
    public readonly struct ManagedResearchAbilityTransferSnapshot
    {
        public ManagedResearchAbilityTransferSnapshot(
            AbilityDef abilityDef,
            bool unlocked,
            bool sourceIsJustice,
            bool targetIsJustice,
            bool sourceHadAbility,
            int sourceRemainingCooldown,
            bool targetHadAbility,
            int targetRemainingCooldown)
        {
            AbilityDef = abilityDef;
            Unlocked = unlocked;
            SourceIsJustice = sourceIsJustice;
            TargetIsJustice = targetIsJustice;
            SourceHadAbility = sourceHadAbility;
            SourceRemainingCooldown = sourceRemainingCooldown;
            TargetHadAbility = targetHadAbility;
            TargetRemainingCooldown = targetRemainingCooldown;
        }

        public AbilityDef AbilityDef { get; }

        public bool Unlocked { get; }

        public bool SourceIsJustice { get; }

        public bool TargetIsJustice { get; }

        public bool SourceHadAbility { get; }

        public int SourceRemainingCooldown { get; }

        public bool TargetHadAbility { get; }

        public int TargetRemainingCooldown { get; }
    }
}
