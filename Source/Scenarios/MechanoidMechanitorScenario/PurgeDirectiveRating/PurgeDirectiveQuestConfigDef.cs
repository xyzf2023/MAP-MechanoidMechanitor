using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 肃清评级任务（独立的 Quest）配置。所有任务调度与结算数值集中在此，
    /// 不得使用另一套等级表或在 QuestNode/QuestPart 中写死数值。
    /// </summary>
    public sealed class PurgeDirectiveQuestConfigDef : Def
    {
        /// <summary>
        /// 对应的 QuestScriptDef（根节点为 QuestNode_Root_PurgeDirectiveQuest）。
        /// </summary>
        public string questScriptDefName = "MAP_PurgeDirectiveQuest";

        /// <summary>触发任务所需的最低评级等级（默认 1）。</summary>
        public int minRatingLevel = 1;

        /// <summary>同一时间最多进行中的肃清评级任务数量。</summary>
        public int maxActive = 1;

        /// <summary>玩家抉择期（天），超过则任务按拒绝处理（无处罚）。</summary>
        public int offerTimeoutDays = 3;

        /// <summary>接取后的完成期限（天），超时按任务失败处理（-1000，不重复）。</summary>
        public int operationTimeoutDays = 15;

        /// <summary>无进行中任务且距上次结束满该天数后再尝试生成。</summary>
        public int idleCooldownDays = 4;

        /// <summary>任务成功时世界目标基础奖励（按稳定ID持久化去重）。</summary>
        public int baseSuccessRewardPoints = 200;

        /// <summary>任务失败处罚（评级 -1000，不低于 0）。</summary>
        public int failurePenalty = 1000;

        /// <summary>肃清额度正向奖励结算范围。</summary>
        public int minRewardValue = 600;

        public int maxRewardValue = 1200;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }

            if (questScriptDefName.NullOrEmpty())
            {
                yield return $"{defName}: questScriptDefName is required.";
            }

            if (minRatingLevel < 1 || minRatingLevel > 5)
            {
                yield return $"{defName}: minRatingLevel must be in 1..5.";
            }

            if (maxActive < 1)
            {
                yield return $"{defName}: maxActive must be >= 1.";
            }

            if (baseSuccessRewardPoints < 0)
            {
                yield return $"{defName}: baseSuccessRewardPoints must be >= 0.";
            }

            if (failurePenalty < 0)
            {
                yield return $"{defName}: failurePenalty must be >= 0.";
            }

            if (minRewardValue > maxRewardValue)
            {
                yield return $"{defName}: minRewardValue must be <= maxRewardValue.";
            }
        }
    }

    [DefOf]
    public static class PurgeDirectiveQuestConfigDefOf
    {
        public static PurgeDirectiveQuestConfigDef MAP_PurgeDirectiveQuestConfig = null!;

        static PurgeDirectiveQuestConfigDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(PurgeDirectiveQuestConfigDefOf));
        }
    }
}
