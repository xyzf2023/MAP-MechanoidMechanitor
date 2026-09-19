using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 评级升级 / 降级信件。仅在评级机制活动（未接管主脑）时发送。
    /// 接管主脑分支中调用方不应进入本路径。
    /// </summary>
    public static class PurgeDirectiveRatingLetterUtility
    {
        public static void SendUpgradeLetter(int prevLevel, int newLevel, int ratingValue)
        {
            if (!PurgeDirectiveRatingUtility.IsRatingSystemActive())
            {
                return;
            }

            PurgeDirectiveRatingConfigDef def = PurgeDirectiveRatingConfigDefOf.MAP_PurgeDirectiveRatingConfig;
            LetterDef letterDef = def.ratingUpgradeLetter ?? LetterDefOf.PositiveEvent;
            TaggedString title = "MAP_PurgeDirectiveRating.Letter.Upgrade.Title".Translate();
            TaggedString text = "MAP_PurgeDirectiveRating.Letter.Upgrade.Text".Translate(
                PurgeDirectiveRatingDisplay.RatingName(prevLevel),
                PurgeDirectiveRatingDisplay.RatingName(newLevel),
                ratingValue,
                PurgeDirectiveRatingDisplay.PermissionsBetween(prevLevel, newLevel));
            Find.LetterStack.ReceiveLetter(title, text, letterDef);
        }

        public static void SendDowngradeLetter(int newLevel, int prevLevel, int ratingValue)
        {
            if (!PurgeDirectiveRatingUtility.IsRatingSystemActive())
            {
                return;
            }

            PurgeDirectiveRatingConfigDef def = PurgeDirectiveRatingConfigDefOf.MAP_PurgeDirectiveRatingConfig;
            LetterDef letterDef = def.ratingDowngradeLetter ?? LetterDefOf.NegativeEvent;
            TaggedString title = "MAP_PurgeDirectiveRating.Letter.Downgrade.Title".Translate();
            TaggedString text = "MAP_PurgeDirectiveRating.Letter.Downgrade.Text".Translate(
                PurgeDirectiveRatingDisplay.RatingName(prevLevel),
                PurgeDirectiveRatingDisplay.RatingName(newLevel),
                ratingValue,
                PurgeDirectiveRatingDisplay.PermissionsBetween(newLevel, prevLevel));
            Find.LetterStack.ReceiveLetter(title, text, letterDef);
        }

        public static void SendQuestCompleteLetter(
            RimWorld.Planet.WorldObject? target,
            PurgeDirectiveTargetType targetType,
            int totalReward)
        {
            SendQuestCompleteLetter(target, targetType, (long)totalReward);
        }

        public static void SendQuestCompleteLetter(
            RimWorld.Planet.WorldObject? target,
            PurgeDirectiveTargetType targetType,
            long totalReward)
        {
            if (!PurgeDirectiveRatingUtility.IsRatingSystemActive())
            {
                return;
            }

            TaggedString title = "MAP_PurgeDirectiveRating.Letter.QuestComplete.Title".Translate();
            string textKey = totalReward >= 0
                ? "MAP_PurgeDirectiveRating.Letter.QuestComplete.Text"
                : "MAP_PurgeDirectiveRating.Letter.QuestComplete.LegacyText";
            TaggedString text = textKey.Translate(
                target != null ? target.LabelCap : "?",
                PurgeDirectiveRatingDisplay.RatingName(
                    PurgeDirectiveRatingUtility.CurrentRatingLevel),
                totalReward);
            Find.LetterStack.ReceiveLetter(title, text, LetterDefOf.PositiveEvent);
        }
    }
}
