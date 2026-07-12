using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MAP_MechanoidMechanitor
{
    public static class ManagedResearchFeatureCatalog
    {
        public const string AutonomousDirectiveOptimizationResearchDefName =
            "MAP_AutonomousDirectiveOptimization";

        public const string MechanicalConsciousnessTransferResearchDefName =
            "MAP_MechanicalConsciousnessTransfer";

        public const string DataProcessingAllocationResearchDefName =
            "MAP_DataProcessingAllocation";

        public const string SelfDirectiveFocusResearchDefName =
            "MAP_SelfDirectiveFocus";

        public static readonly ManagedResearchFeatureDescriptor AutonomousDirectiveOptimization =
            new ManagedResearchFeatureDescriptor(
                id: "AutonomousDirectiveOptimization",
                researchProjectDefName: AutonomousDirectiveOptimizationResearchDefName);

        public static readonly ManagedResearchFeatureDescriptor MechanicalConsciousnessTransfer =
            new ManagedResearchFeatureDescriptor(
                id: "MechanicalConsciousnessTransfer",
                researchProjectDefName: MechanicalConsciousnessTransferResearchDefName);

        public static readonly ManagedResearchFeatureDescriptor DataProcessingAllocation =
            new ManagedResearchFeatureDescriptor(
                id: "DataProcessingAllocation",
                researchProjectDefName: DataProcessingAllocationResearchDefName);

        public static readonly ManagedResearchFeatureDescriptor SelfDirectiveFocus =
            new ManagedResearchFeatureDescriptor(
                id: "SelfDirectiveFocus",
                researchProjectDefName: SelfDirectiveFocusResearchDefName);

        private static readonly ManagedResearchFeatureDescriptor[] allInternal =
        {
            AutonomousDirectiveOptimization,
            MechanicalConsciousnessTransfer,
            DataProcessingAllocation,
            SelfDirectiveFocus
        };

        private static readonly ReadOnlyCollection<ManagedResearchFeatureDescriptor> allReadOnly =
            new ReadOnlyCollection<ManagedResearchFeatureDescriptor>(allInternal);

        public static IReadOnlyList<ManagedResearchFeatureDescriptor> All => allReadOnly;
    }
}
