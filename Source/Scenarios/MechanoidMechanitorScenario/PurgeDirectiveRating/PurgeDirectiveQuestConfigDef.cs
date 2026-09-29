using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 肃清评级任务配置。所有时间、奖励、处罚、等级、折扣数值均为「唯一权威源」，
    /// 集中在 PurgeDirectiveRatingConfigDef；本 Def 只保存非数值信息（如 QuestScriptDef 名称）。
    /// 不得在此再维护一套与 PurgeDirectiveRatingConfigDef 重复或互相矛盾的数值配置。
    /// </summary>
    public sealed class PurgeDirectiveQuestConfigDef : Def
    {
        /// <summary>对应 QuestScriptDef 的 defName（任务生成入口）。</summary>
        public string? questScriptDef;

        public QuestScriptDef? QuestScript
        {
            get
            {
                if (questScriptDef == null) return null;
                return DefDatabase<QuestScriptDef>.GetNamed(questScriptDef, false);
            }
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }

            if (questScriptDef == null)
            {
                yield return $"{defName}: 必须设置 questScriptDef。";
            }
            else if (DefDatabase<QuestScriptDef>.GetNamed(questScriptDef, false) == null)
            {
                yield return $"{defName}: 在 QuestScriptDef 定义库中未找到 questScriptDef '{questScriptDef}'。";
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
