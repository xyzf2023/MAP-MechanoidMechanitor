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

        public const string OrbitalDataNetworkResearchDefName =
            "MAP_OrbitalDataNetwork";

        public const string DataProcessingAllocationResearchDefName =
            "MAP_DataProcessingAllocation";

        public const string SelfDirectiveFocusResearchDefName =
            "MAP_SelfDirectiveFocus";

        public const string DataStreamReorganizationResearchDefName =
            "MAP_DataStreamReorganization";

        public const string ParallelThoughtMatrixResearchDefName =
            "MAP_ParallelThoughtMatrix";

        public const string QuantumTaskComputationDescriptionKey =
            "MAP_MechanoidMechanitor.Justice.Unlock.Feature.QuantumTaskComputation.Description";

        public const string AutonomousDirectiveOptimizationDescriptionKey =
            "MAP_MechanoidMechanitor.Justice.Unlock.Feature.AutonomousDirectiveOptimization.Description";

        public const string MechanicalConsciousnessTransferDescriptionKey =
            "MAP_MechanoidMechanitor.Justice.Unlock.Feature.MechanicalConsciousnessTransfer.Description";

        public const string OrbitalDataNetworkDescriptionKey =
            "MAP_MechanoidMechanitor.Justice.Unlock.Feature.OrbitalDataNetwork.Description";

        public const string DataProcessingAllocationDescriptionKey =
            "MAP_MechanoidMechanitor.Justice.Unlock.Feature.DataProcessingAllocation.Description";

        public const string SelfDirectiveFocusDescriptionKey =
            "MAP_MechanoidMechanitor.Justice.Unlock.Feature.SelfDirectiveFocus.Description";

        public const string DataStreamReorganizationDescriptionKey =
            "MAP_MechanoidMechanitor.Justice.Unlock.Feature.DataStreamReorganization.Description";

        public const string ParallelThoughtMatrixDescriptionKey =
            "MAP_MechanoidMechanitor.Justice.Unlock.Feature.ParallelThoughtMatrix.Description";

        public static readonly ManagedResearchFeatureDescriptor QuantumTaskComputation =
            new ManagedResearchFeatureDescriptor(
                id: "QuantumTaskComputation",
                researchProjectDefName: ResearchFeatureUnlockUtility.QuantumTaskComputationResearchDefName,
                unlockLetterDescriptionKey: QuantumTaskComputationDescriptionKey);

        public static readonly ManagedResearchFeatureDescriptor AutonomousDirectiveOptimization =
            new ManagedResearchFeatureDescriptor(
                id: "AutonomousDirectiveOptimization",
                researchProjectDefName: AutonomousDirectiveOptimizationResearchDefName,
                unlockLetterDescriptionKey: AutonomousDirectiveOptimizationDescriptionKey);

        public static readonly ManagedResearchFeatureDescriptor MechanicalConsciousnessTransfer =
            new ManagedResearchFeatureDescriptor(
                id: "MechanicalConsciousnessTransfer",
                researchProjectDefName: MechanicalConsciousnessTransferResearchDefName,
                unlockLetterDescriptionKey: MechanicalConsciousnessTransferDescriptionKey);

        public static readonly ManagedResearchFeatureDescriptor OrbitalDataNetwork =
            new ManagedResearchFeatureDescriptor(
                id: "OrbitalDataNetwork",
                researchProjectDefName: OrbitalDataNetworkResearchDefName,
                unlockLetterDescriptionKey: OrbitalDataNetworkDescriptionKey);

        public static readonly ManagedResearchFeatureDescriptor DataProcessingAllocation =
            new ManagedResearchFeatureDescriptor(
                id: "DataProcessingAllocation",
                researchProjectDefName: DataProcessingAllocationResearchDefName,
                unlockLetterDescriptionKey: DataProcessingAllocationDescriptionKey);

        public static readonly ManagedResearchFeatureDescriptor SelfDirectiveFocus =
            new ManagedResearchFeatureDescriptor(
                id: "SelfDirectiveFocus",
                researchProjectDefName: SelfDirectiveFocusResearchDefName,
                unlockLetterDescriptionKey: SelfDirectiveFocusDescriptionKey);

        public static readonly ManagedResearchFeatureDescriptor DataStreamReorganization =
            new ManagedResearchFeatureDescriptor(
                id: "DataStreamReorganization",
                researchProjectDefName: DataStreamReorganizationResearchDefName,
                unlockLetterDescriptionKey: DataStreamReorganizationDescriptionKey);

        public static readonly ManagedResearchFeatureDescriptor ParallelThoughtMatrix =
            new ManagedResearchFeatureDescriptor(
                id: "ParallelThoughtMatrix",
                researchProjectDefName: ParallelThoughtMatrixResearchDefName,
                unlockLetterDescriptionKey: ParallelThoughtMatrixDescriptionKey);

        private static readonly ManagedResearchFeatureDescriptor[] allInternal =
        {
            QuantumTaskComputation,
            AutonomousDirectiveOptimization,
            MechanicalConsciousnessTransfer,
            OrbitalDataNetwork,
            DataProcessingAllocation,
            SelfDirectiveFocus,
            DataStreamReorganization,
            ParallelThoughtMatrix
        };

        private static readonly ReadOnlyCollection<ManagedResearchFeatureDescriptor> allReadOnly =
            new ReadOnlyCollection<ManagedResearchFeatureDescriptor>(allInternal);

        public static IReadOnlyList<ManagedResearchFeatureDescriptor> All => allReadOnly;
    }
}
