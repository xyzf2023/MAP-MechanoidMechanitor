using System;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.GlitterworldDestroyer5
{
    /// <summary>
    /// 仅在主脑接管从“未接管”成功转换为“已接管”时触发一次。
    /// 研究完成失败不得中断主脑接管流程。
    /// </summary>
    internal static class CerebrexTakeoverResearchCompatibilityPatch
    {
        private static ResearchProjectDef? targetResearch;

        internal static void Configure(ResearchProjectDef research)
        {
            targetResearch = research;
        }

        internal static void Prefix(out bool __state)
        {
            __state = GameComponent_CerebrexTakeoverState.IsActive;
        }

        internal static void Postfix(bool __state, bool __result)
        {
            if (__state
                || !__result
                || !GameComponent_CerebrexTakeoverState.IsActive)
            {
                return;
            }

            ResearchProjectDef? research = targetResearch;
            ResearchManager? manager = Find.ResearchManager;
            if (research == null || manager == null || research.IsFinished)
            {
                return;
            }

            try
            {
                manager.FinishProject(
                    research,
                    doCompletionDialog: false,
                    researcher: null,
                    doCompletionLetter: false);
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 闪耀世界毁灭者5主脑接管集群科技自动完成失败。\n"
                    + ex);
            }
        }
    }
}
