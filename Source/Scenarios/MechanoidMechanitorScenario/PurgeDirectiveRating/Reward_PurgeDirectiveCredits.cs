using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>仅展示肃清额度，使用原版未知奖励图标；不参与奖励发放。</summary>
    [StaticConstructorOnStartup]
    public sealed class Reward_PurgeDirectiveCredits : Reward_Unknown
    {
        private static readonly Texture2D Icon =
            ContentFinder<Texture2D>.Get("UI/Overlays/QuestionMark");

        public override IEnumerable<GenUI.AnonymousStackElement> StackElements
        {
            get
            {
                yield return QuestPartUtility.GetStandardRewardStackElement(
                    "MAP_PurgeDirectiveRating.Quest.Reward".Translate(), Icon,
                    () => GetDescription(default(RewardsGeneratorParams)));
            }
        }

        public override string GetDescription(RewardsGeneratorParams parms)
        {
            return "MAP_PurgeDirectiveRating.Quest.Reward".Translate();
        }
    }
}
