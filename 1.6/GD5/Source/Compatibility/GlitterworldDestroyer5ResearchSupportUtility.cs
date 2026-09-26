using System;
using MAP_MechanoidMechanitor.Compatibility.ThirdParty.GlitterworldDestroyer5;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.GD5
{
    /// <summary>
    /// 闪耀世界毁灭者5集群科技支持的共享实现。
    /// 研究完成状态作为科研幂等依据；本次实际完成的科技另行登记支持信件。
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
                // FinishProject 可能在写入完成进度后才由其他回调抛出。
                // 以实际科研状态决定是否已提交，不因通知失败让调用者重复结算。
                if (!research.IsFinished)
                {
                    return false;
                }
            }

            if (!research.IsFinished)
            {
                Log.Error(
                    "[MAP-机械族机械师] 闪耀世界毁灭者5集群科技支持失败："
                    + diagnosticContext + "；" + research.defName
                    + " 在 FinishProject 调用后仍未完成。");
                return false;
            }

            GameComponent_GD5ResearchSupport.QueueSupportLetter(research);
            return true;
        }
    }
}
