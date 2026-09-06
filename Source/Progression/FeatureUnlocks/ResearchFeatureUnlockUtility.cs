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
        private static ResearchProjectDef? cachedStandardMechtech;
        private static bool standardMechtechMissingLogged;

        public static bool IsStandardMechtechFinished()
        {
            ResearchProjectDef? research = GetStandardMechtech();
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
            if (pawn == null
                || pawn.Destroyed
                || pawn.Dead
                || descriptor == null
                || !IsAbilityUnlocked(descriptor))
            {
                return false;
            }

            if (pawn.Faction == null || !pawn.Faction.IsPlayerSafe())
            {
                return false;
            }

            if (JusticePawnUtility.IsJustice(pawn))
            {
                return true;
            }

            if (GameComponent_MechanoidMechanitorScenarioState.IsEnabled
                && GameComponent_MechanoidMechanitorRegistry.IsMechanicalConsciousnessHost(pawn))
            {
                return true;
            }

            // 轨道数据网络研究完成后，机械意识已通过轨道网络同步给全体已注册机械族机械师，
            // 原本仅供“机械意识载体”使用的已解锁科研能力应分发至所有符合资格的机械族机械师。
            // 资格仍限定在“机械族机械师剧本”（普通剧本绝不因该科研而扩大能力持有范围），
            // 且 Pawn 必须已注册、存活、初始化完成并属于玩家阵营。
            if (IsOrbitalDataNetworkUnlocked()
                && GameComponent_MechanoidMechanitorScenarioState.IsEnabled
                && GameComponent_MechanoidMechanitorRegistry.IsPawnAliveAndInitialized(pawn)
                && MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
            {
                return true;
            }

            return false;
        }
    }
}
