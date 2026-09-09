using System;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.GlitterworldDestroyer5
{
    /// <summary>
    /// 每次肃清评级等级变化后，按变化后的当前等级检查应拥有的集群科技。
    /// 已完成科技保持静默；仅当本次实际完成目标科技时发送主脑科技支持信件。
    /// </summary>
    internal static class PurgeDirectiveResearchCompatibilityPatch
    {
        private static ResearchProjectDef? mediumResearch;
        private static ResearchProjectDef? largeResearch;
        private static ResearchProjectDef? ultraResearch;

        internal static void Configure(
            ResearchProjectDef medium,
            ResearchProjectDef large,
            ResearchProjectDef ultra)
        {
            mediumResearch = medium;
            largeResearch = large;
            ultraResearch = ultra;
        }

        internal static void Postfix(int before, int after)
        {
            if (!PurgeDirectiveRatingUtility.IsRatingSystemActive())
            {
                return;
            }

            int oldLevel = PurgeDirectiveRatingUtility.Config.GetRatingLevel(before);
            int newLevel = PurgeDirectiveRatingUtility.Config.GetRatingLevel(after);
            if (oldLevel == newLevel)
            {
                return;
            }

            if (newLevel >= 2
                && !EnsureResearchCompleted(mediumResearch))
            {
                return;
            }

            if (newLevel >= 4
                && !EnsureResearchCompleted(largeResearch))
            {
                return;
            }

            if (newLevel >= 5)
            {
                EnsureResearchCompleted(ultraResearch);
            }
        }

        /// <summary>
        /// 返回 true 表示该等级奖励已满足或本次成功补齐，可继续检查后续科技；
        /// 返回 false 表示发生异常或目标不可用，本次停止继续补齐更高阶科技。
        /// </summary>
        private static bool EnsureResearchCompleted(ResearchProjectDef? research)
        {
            if (research == null)
            {
                return false;
            }

            if (research.IsFinished)
            {
                return true;
            }

            ResearchManager? manager = Find.ResearchManager;
            if (manager == null)
            {
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
                        "[MAP-机械族机械师] 闪耀世界毁灭者5肃清评级科技支持失败："
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
                    "[MAP-机械族机械师] 闪耀世界毁灭者5肃清评级科技支持失败："
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
