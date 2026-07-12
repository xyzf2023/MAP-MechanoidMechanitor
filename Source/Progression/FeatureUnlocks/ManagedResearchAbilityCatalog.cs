using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MAP_MechanoidMechanitor
{
    public static class ManagedResearchAbilityCatalog
    {
        public const string StandardMechtechDefName = "StandardMechtech";
        public const string MechRecodeAbilityDefName = "MAP_Ability_MechRecode";
        public const string MechReconstructionAbilityDefName = "MAP_Ability_MechReconstruction";
        public const string MechHackAbilityDefName = "MAP_Ability_MechHack";

        public static readonly ManagedResearchAbilityDescriptor MechRecode =
            new ManagedResearchAbilityDescriptor(
                id: "MechRecode",
                abilityDefName: MechRecodeAbilityDefName,
                researchProjectDefName: StandardMechtechDefName);

        // 预留接入点：后续只需加入列表即可复用解锁同步与意识迁移冷却逻辑。
        // public static readonly ManagedResearchAbilityDescriptor MechReconstruction = ...
        // public static readonly ManagedResearchAbilityDescriptor MechHack = ...

        private static readonly ManagedResearchAbilityDescriptor[] allInternal =
        {
            MechRecode
        };

        private static readonly ReadOnlyCollection<ManagedResearchAbilityDescriptor> allReadOnly =
            new ReadOnlyCollection<ManagedResearchAbilityDescriptor>(allInternal);

        public static IReadOnlyList<ManagedResearchAbilityDescriptor> All => allReadOnly;
    }
}
