using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 受科研管理的能力描述。后续可追加 Reconstruction / Hack 等项，无需复制迁移逻辑。
    /// </summary>
    public sealed class ManagedResearchAbilityDescriptor
    {
        public ManagedResearchAbilityDescriptor(
            string id,
            string abilityDefName,
            string researchProjectDefName)
        {
            Id = id;
            AbilityDefName = abilityDefName;
            ResearchProjectDefName = researchProjectDefName;
        }

        public string Id { get; }

        public string AbilityDefName { get; }

        public string ResearchProjectDefName { get; }

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
                if (cachedResearchDef != null)
                {
                    return cachedResearchDef;
                }

                cachedResearchDef =
                    DefDatabase<ResearchProjectDef>.GetNamedSilentFail(ResearchProjectDefName);
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
    }
}
