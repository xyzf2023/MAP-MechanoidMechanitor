using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 受科研管理的功能特性描述（非 Ability）。
    /// </summary>
    public sealed class ManagedResearchFeatureDescriptor
    {
        public ManagedResearchFeatureDescriptor(string id, string researchProjectDefName)
        {
            Id = id;
            ResearchProjectDefName = researchProjectDefName;
        }

        public string Id { get; }

        public string ResearchProjectDefName { get; }

        private ResearchProjectDef? cachedResearchDef;
        private bool researchDefMissingLogged;

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
                        $"[MAP-机械族机械师] 找不到科研 Def：{ResearchProjectDefName}（特性 {Id}）。");
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
