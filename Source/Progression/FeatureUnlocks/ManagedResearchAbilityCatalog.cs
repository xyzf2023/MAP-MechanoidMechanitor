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

        public static readonly ManagedResearchAbilityDescriptor MechRecode =
            new ManagedResearchAbilityDescriptor(
                id: "MechRecode",
                abilityDefName: MechRecodeAbilityDefName,
                researchProjectDefName: StandardMechtechDefName);

        public static readonly ManagedResearchAbilityDescriptor MechReconstruction =
            new ManagedResearchAbilityDescriptor(
                id: "MechReconstruction",
                abilityDefName: MechReconstructionAbilityDefName,
                researchProjectDefName: HighMechtechDefName);

        public static readonly ManagedResearchAbilityDescriptor MechHack =
            new ManagedResearchAbilityDescriptor(
                id: "MechHack",
                abilityDefName: MechHackAbilityDefName,
                researchProjectDefName: UltraMechtechDefName);

        // 三个科研能力均已接入统一解锁同步与意识迁移冷却逻辑。
        private static readonly ManagedResearchAbilityDescriptor[] allInternal =
        {
            MechRecode,
            MechReconstruction,
            MechHack
        };

        private static readonly ReadOnlyCollection<ManagedResearchAbilityDescriptor> allReadOnly =
            new ReadOnlyCollection<ManagedResearchAbilityDescriptor>(allInternal);

        public static IReadOnlyList<ManagedResearchAbilityDescriptor> All => allReadOnly;
    }
}
