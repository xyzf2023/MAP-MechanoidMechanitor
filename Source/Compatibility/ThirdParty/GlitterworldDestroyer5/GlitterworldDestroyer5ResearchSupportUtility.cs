using System;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.GlitterworldDestroyer5
{
    /// <summary>
    /// 闪耀世界毁灭者5集群科技支持的共享实现。
    /// 研究完成状态作为唯一幂等依据；只有本次实际完成目标科技时才发送支持信件。
    /// </summary>
    internal static class GlitterworldDestroyer5ResearchSupportUtility
    {
        internal static bool EnsureResearchCompleted(
            ResearchProjectDef? research,
            string diagnosticContext)
        {
            if (research == null)
            {
                Log.Error(
                    "[MAP-机械族机械师] 闪耀世界毁灭者5集群科技支持失败："
                    + diagnosticContext
                    + " 的研究Def不可用。");
                return false;
            }

            if (research.IsFinished)
            {
                return true;
            }

            ResearchManager? manager = Find.ResearchManager;
            if (manager == null)
            {
                Log.Error(
                    "[MAP-机械族机械师] 闪耀世界毁灭者5集群科技支持失败："
                    + diagnosticContext
                    + " 无法取得ResearchManager。");
                return false;
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
                    Log.Error(
                        "[MAP-机械族机械师] 闪耀世界毁灭者5集群科技支持失败："
                        + diagnosticContext
                        + "；"
                        + research.defName
                        + " 在 FinishProject 调用后仍未完成。");
                    return false;
                }

                SendResearchSupportLetter(research);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 闪耀世界毁灭者5集群科技支持失败："
                    + diagnosticContext
                    + "；"
                    + research.defName
                    + "。\n"
                    + ex);
                return false;
            }
        }

        private static void SendResearchSupportLetter(ResearchProjectDef research)
        {
            TaggedString title = "MAP_GD5.PurgeResearchSupport.Title".Translate();
            TaggedString text = "MAP_GD5.PurgeResearchSupport.Text".Translate(
                research.LabelCap);
            Find.LetterStack.ReceiveLetter(
                title,
                text,
                LetterDefOf.PositiveEvent);
        }
    }
}
