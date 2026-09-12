using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 由统一能力层管理的能力描述。
    /// 名称为兼容既有代码而保留；除科研能力外，也支持机体能力及“科研+机体”联合资格。
    /// </summary>
    public sealed class ManagedResearchAbilityDescriptor
    {
        public ManagedResearchAbilityDescriptor(
            string id,
            string abilityDefName,
            string? researchProjectDefName,
            string? unlockLetterDescriptionKey,
            ManagedAbilityGrantPolicy grantPolicy =
                ManagedAbilityGrantPolicy.ResearchConsciousness,
            ManagedAbilityTransferPolicy transferPolicy =
                ManagedAbilityTransferPolicy.TransferWithConsciousness,
            MechanoidMechanitorCapability requiredCapability =
                MechanoidMechanitorCapability.None,
            bool sendResearchUnlockLetter = true,
            bool removeWhenIneligible = true)
        {
            Id = id;
            AbilityDefName = abilityDefName;
            ResearchProjectDefName = researchProjectDefName;
            UnlockLetterDescriptionKey = unlockLetterDescriptionKey;
            GrantPolicy = grantPolicy;
            TransferPolicy = transferPolicy;
            RequiredCapability = requiredCapability;
            SendResearchUnlockLetter = sendResearchUnlockLetter;
            RemoveWhenIneligible = removeWhenIneligible;
        }

        public string Id { get; }

        public string AbilityDefName { get; }

        public string? ResearchProjectDefName { get; }

        public string? UnlockLetterDescriptionKey { get; }

        public ManagedAbilityGrantPolicy GrantPolicy { get; }

        public ManagedAbilityTransferPolicy TransferPolicy { get; }

        public MechanoidMechanitorCapability RequiredCapability { get; }

        public bool SendResearchUnlockLetter { get; }

        public bool RemoveWhenIneligible { get; }

        public bool ParticipatesInResearchUnlockNotifications =>
            SendResearchUnlockLetter
            && !string.IsNullOrEmpty(ResearchProjectDefName)
            && !string.IsNullOrEmpty(UnlockLetterDescriptionKey);

        private AbilityDef? cachedAbilityDef;
        private ResearchProjectDef? cachedResearchDef;
        private bool abilityDefMissingLogged;
        private bool researchDefMissingLogged;

        public AbilityDef? AbilityDef
        {
            get
            {
                if (cachedAbilityDef != null)
                {
                    return cachedAbilityDef;
                }

                cachedAbilityDef =
                    DefDatabase<AbilityDef>.GetNamedSilentFail(AbilityDefName);
                if (cachedAbilityDef == null && !abilityDefMissingLogged)
                {
                    abilityDefMissingLogged = true;
                    Log.Error(
                        $"[MAP-机械族机械师] 找不到受管理能力 Def：{AbilityDefName}（功能 {Id}）。");
                }

                return cachedAbilityDef;
            }
        }

        public ResearchProjectDef? ResearchProjectDef
        {
            get
            {
                if (string.IsNullOrEmpty(ResearchProjectDefName))
                {
                    return null;
                }

                if (cachedResearchDef != null)
                {
                    return cachedResearchDef;
                }

                cachedResearchDef =
                    DefDatabase<ResearchProjectDef>.GetNamedSilentFail(
                        ResearchProjectDefName);
                if (cachedResearchDef == null && !researchDefMissingLogged)
                {
                    researchDefMissingLogged = true;
                    Log.Error(
                        $"[MAP-机械族机械师] 找不到科研 Def：{ResearchProjectDefName}（功能 {Id}）。");
                }

                return cachedResearchDef;
            }
        }

        public bool IsResearchFinished()
        {
            ResearchProjectDef? research = ResearchProjectDef;
            return research != null && research.IsFinished;
        }

        public bool IsResearchRequirementSatisfied()
        {
            return string.IsNullOrEmpty(ResearchProjectDefName)
                || IsResearchFinished();
        }
    }
}
