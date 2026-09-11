using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MAP_MechanoidMechanitor
{
    public static class ManagedResearchAbilityCatalog
    {
        public const string StandardMechtechDefName = "StandardMechtech";
        public const string HighMechtechDefName = "HighMechtech";
        public const string UltraMechtechDefName = "UltraMechtech";

        public const string MechRecodeAbilityDefName = "MAP_Ability_MechRecode";
        public const string MechReconstructionAbilityDefName = "MAP_Ability_MechReconstruction";
        public const string MechHackAbilityDefName = "MAP_Ability_MechHack";
        public const string BuildingConversionAbilityDefName =
            "MAP_Ability_MechBuildingConversion";

        public const string MechRecodeDescriptionKey =
            "MAP_MechanoidMechanitor.Justice.Unlock.Ability.MechRecode.Description";
        public const string MechReconstructionDescriptionKey =
            "MAP_MechanoidMechanitor.Justice.Unlock.Ability.MechReconstruction.Description";
        public const string MechHackDescriptionKey =
            "MAP_MechanoidMechanitor.Justice.Unlock.Ability.MechHack.Description";

        public static readonly ManagedResearchAbilityDescriptor MechRecode =
            new ManagedResearchAbilityDescriptor(
                id: "MechRecode",
                abilityDefName: MechRecodeAbilityDefName,
                researchProjectDefName: StandardMechtechDefName,
                unlockLetterDescriptionKey: MechRecodeDescriptionKey);

        public static readonly ManagedResearchAbilityDescriptor MechReconstruction =
            new ManagedResearchAbilityDescriptor(
                id: "MechReconstruction",
                abilityDefName: MechReconstructionAbilityDefName,
                researchProjectDefName: HighMechtechDefName,
                unlockLetterDescriptionKey: MechReconstructionDescriptionKey);

        public static readonly ManagedResearchAbilityDescriptor MechHack =
            new ManagedResearchAbilityDescriptor(
                id: "MechHack",
                abilityDefName: MechHackAbilityDefName,
                researchProjectDefName: UltraMechtechDefName,
                unlockLetterDescriptionKey: MechHackDescriptionKey);

        public static readonly ManagedResearchAbilityDescriptor BuildingConversion =
            new ManagedResearchAbilityDescriptor(
                id: "BuildingConversion",
                abilityDefName: BuildingConversionAbilityDefName,
                researchProjectDefName: null,
                unlockLetterDescriptionKey: null,
                grantPolicy: ManagedAbilityGrantPolicy.Capability,
                transferPolicy: ManagedAbilityTransferPolicy.ReevaluateTargetChassis,
                requiredCapabilityId: MechCapabilityIds.BuildingConversion,
                sendResearchUnlockLetter: false);

        // 三个科研能力均已接入统一解锁同步与意识迁移冷却逻辑。
        private static readonly ManagedResearchAbilityDescriptor[] allInternal =
        {
            MechRecode,
            MechReconstruction,
            MechHack,
            BuildingConversion
        };

        private static readonly ReadOnlyCollection<ManagedResearchAbilityDescriptor> allReadOnly =
            new ReadOnlyCollection<ManagedResearchAbilityDescriptor>(allInternal);

        public static IReadOnlyList<ManagedResearchAbilityDescriptor> All => allReadOnly;
    }
}
