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
                && !GlitterworldDestroyer5ResearchSupportUtility.EnsureResearchCompleted(
                    mediumResearch,
                    "肃清评级L2科技支持"))
            {
                return;
            }

            if (newLevel >= 4
                && !GlitterworldDestroyer5ResearchSupportUtility.EnsureResearchCompleted(
                    largeResearch,
                    "肃清评级L4科技支持"))
            {
                return;
            }

            if (newLevel >= 5)
            {
                GlitterworldDestroyer5ResearchSupportUtility.EnsureResearchCompleted(
                    ultraResearch,
                    "肃清评级L5科技支持");
            }
        }
    }
}
