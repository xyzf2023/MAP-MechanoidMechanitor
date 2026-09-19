using System;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.GlitterworldDestroyer5
{
    /// <summary>
    /// 在主脑接管成功时立即尝试，失败后由兼容状态组件按已持久化的接管状态补做。
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

            EnsureTakeoverResearchCompleted();
        }

        internal static void EnsureTakeoverResearchCompleted()
        {
            if (!GameComponent_CerebrexTakeoverState.IsActive)
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
                if (!research.IsFinished)
                {
                    Log.Warning(
                        "[MAP-机械族机械师] 主脑接管集群科技尚未完成，将在后续安全 tick 重试。");
                }
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
