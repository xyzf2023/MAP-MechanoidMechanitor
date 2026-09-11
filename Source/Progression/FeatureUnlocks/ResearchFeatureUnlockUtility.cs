using System.Collections.Generic;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 集中科研解锁判定。外部代码不得各自硬编码科研判断。
    /// 权威来源始终是 ResearchProjectDef.IsFinished。
    /// </summary>
    public static class ResearchFeatureUnlockUtility
    {
        public const string MindMappingResearchDefName = "MAP_MindMapping";

        private static ResearchProjectDef? cachedStandardMechtech;
        private static bool standardMechtechMissingLogged;
        private static ResearchProjectDef? cachedMindMapping;
        private static bool mindMappingMissingLogged;

        public static bool IsStandardMechtechFinished()
        {
            ResearchProjectDef? research = GetStandardMechtech();
            return research != null && research.IsFinished;
        }

        public static bool IsMindMappingUnlocked()
        {
            ResearchProjectDef? research = GetMindMappingResearch();
            return research != null && research.IsFinished;
        }

        public static ResearchProjectDef? GetStandardMechtech()
        {
            if (cachedStandardMechtech != null)
            {
                return cachedStandardMechtech;
            }

            cachedStandardMechtech =
                DefDatabase<ResearchProjectDef>.GetNamedSilentFail(
                    ManagedResearchAbilityCatalog.StandardMechtechDefName);
            if (cachedStandardMechtech == null && !standardMechtechMissingLogged)
            {
                standardMechtechMissingLogged = true;
                Log.Error(
                    "[MAP-机械族机械师] 找不到 StandardMechtech 科研 Def。");
            }

            return cachedStandardMechtech;
        }

        private static ResearchProjectDef? GetMindMappingResearch()
        {
            if (cachedMindMapping != null)
            {
                return cachedMindMapping;
            }

            cachedMindMapping =
                DefDatabase<ResearchProjectDef>.GetNamedSilentFail(
                    MindMappingResearchDefName);
            if (cachedMindMapping == null && !mindMappingMissingLogged)
            {
                mindMappingMissingLogged = true;
                Log.Error(
                    "[MAP-机械族机械师] 找不到 MindMapping 科研 Def。");
            }

            return cachedMindMapping;
        }

        public static bool IsAbilityUnlocked(ManagedResearchAbilityDescriptor descriptor)
        {
            return descriptor != null && descriptor.IsResearchFinished();
        }

        public static bool IsAbilityUnlocked(AbilityDef? abilityDef)
        {
            if (abilityDef == null)
            {
                return false;
            }

            IReadOnlyList<ManagedResearchAbilityDescriptor> all =
                ManagedResearchAbilityCatalog.All;
            for (int i = 0; i < all.Count; i++)
            {
                ManagedResearchAbilityDescriptor descriptor = all[i];
                if (descriptor.AbilityDef == abilityDef)
                {
                    return IsAbilityUnlocked(descriptor);
                }
            }

            return false;
        }

        public static bool IsFeatureUnlocked(ManagedResearchFeatureDescriptor descriptor)
        {
            return descriptor != null && descriptor.IsResearchFinished();
        }

        public static bool IsAutonomousDirectiveOptimizationUnlocked() =>
            IsFeatureUnlocked(ManagedResearchFeatureCatalog.AutonomousDirectiveOptimization);

        public static bool IsMechanicalConsciousnessTransferUnlocked() =>
            IsFeatureUnlocked(ManagedResearchFeatureCatalog.MechanicalConsciousnessTransfer);

        public static bool IsOrbitalDataNetworkUnlocked() =>
            IsFeatureUnlocked(ManagedResearchFeatureCatalog.OrbitalDataNetwork);

        public static bool IsDataProcessingAllocationUnlocked() =>
            IsFeatureUnlocked(ManagedResearchFeatureCatalog.DataProcessingAllocation);

        public static bool IsSelfDirectiveFocusUnlocked() =>
            IsFeatureUnlocked(ManagedResearchFeatureCatalog.SelfDirectiveFocus);

        public static bool IsDataStreamReorganizationUnlocked() =>
            IsFeatureUnlocked(ManagedResearchFeatureCatalog.DataStreamReorganization);

        public static bool IsParallelThoughtMatrixUnlocked() =>
            IsFeatureUnlocked(ManagedResearchFeatureCatalog.ParallelThoughtMatrix);

        public static bool ShouldPawnHaveAbility(
            Pawn? pawn,
            ManagedResearchAbilityDescriptor descriptor)
        {
            return ManagedAbilityEligibilityUtility.ShouldPawnHaveAbility(
                pawn,
                descriptor);
        }
    }
}
