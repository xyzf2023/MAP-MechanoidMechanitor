using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechanoidOvermindPage_Home
    {
        private const float TargetRowHeight = 58f;

        private const float RowGap = 8f;

        private const int MenuCount = 4;

        private const float OverviewHeight = 80f;

        public MechanoidOvermindPageKind? Draw(Rect inRect, bool inputEnabled)
        {
            using (MechanoidOvermindUiStyle.Push())
            {
                bool ratingActive = PurgeDirectiveRatingUtility.IsRatingSystemActive();
                bool takeoverActive = GameComponent_CerebrexTakeoverState.IsActive;
                float overviewH = (ratingActive || takeoverActive) ? OverviewHeight : 0f;
                float overviewGap = overviewH > 0f ? 10f : 0f;

                if (ratingActive)
                {
                    DrawRatingOverview(new Rect(inRect.x, inRect.y, inRect.width, overviewH));
                }
                else if (takeoverActive)
                {
                    DrawTakeoverOverview(new Rect(inRect.x, inRect.y, inRect.width, overviewH));
                }

                Rect cardsRect = new Rect(
                    inRect.x,
                    inRect.y + overviewH + overviewGap,
                    inRect.width,
                    Mathf.Max(0f, inRect.height - overviewH - overviewGap));

                float needed = MenuCount * TargetRowHeight + (MenuCount - 1) * RowGap;
                float rowH = TargetRowHeight;
                if (cardsRect.height < needed && needed > 0f)
                {
                    float gaps = (MenuCount - 1) * RowGap;
                    rowH = Mathf.Max(1f, (cardsRect.height - gaps) / MenuCount);
                    rowH = Mathf.Min(rowH, TargetRowHeight);
                }

                MechanoidOvermindPageKind? selected = null;
                float y = cardsRect.y;

                if (DrawCard(
                        new Rect(cardsRect.x, y, cardsRect.width, rowH),
                        "01",
                        "MAP_MechanoidMechanitor.PurgeDirective.Communication.Nav.Communication".Translate(),
                        RatingSummaryCommunication(),
                        inputEnabled))
                {
                    selected = MechanoidOvermindPageKind.Communication;
                }

                y += rowH + RowGap;
                if (DrawCard(
                        new Rect(cardsRect.x, y, cardsRect.width, rowH),
                        "02",
                        "MAP_MechanoidMechanitor.PurgeDirective.Communication.Nav.Mechs".Translate(),
                        RatingSummaryMechs(),
                        inputEnabled))
                {
                    selected = MechanoidOvermindPageKind.Mechs;
                }

                y += rowH + RowGap;
                if (DrawCard(
                        new Rect(cardsRect.x, y, cardsRect.width, rowH),
                        "03",
                        "MAP_MechanoidMechanitor.PurgeDirective.Communication.Nav.Goods".Translate(),
                        RatingSummaryGoods(),
                        inputEnabled))
                {
                    selected = MechanoidOvermindPageKind.Goods;
                }

                y += rowH + RowGap;
                if (DrawCard(
                        new Rect(cardsRect.x, y, cardsRect.width, rowH),
                        "04",
                        "MAP_MechanoidMechanitor.PurgeDirective.Communication.Nav.SpecialProtocols".Translate(),
                        RatingSummarySpecialProtocols(),
                        inputEnabled))
                {
                    selected = MechanoidOvermindPageKind.SpecialProtocols;
                }

                return selected;
            }
        }

        private static void DrawRatingOverview(Rect rect)
        {
            MechanoidOvermindUiStyle.DrawPanel(rect);
            Rect inner = rect.ContractedBy(8f);

            int level = PurgeDirectiveRatingUtility.CurrentRatingLevel;
            int value = PurgeDirectiveRatingUtility.CurrentRatingValue();
            int levelStart = PurgeDirectiveRatingUtility.Config.GetLevelStart(level);
            int next = PurgeDirectiveRatingUtility.Config.GetNextLevelStart(level);

            string title = PurgeDirectiveRatingUtility.IsMaxRatingLevel()
                ? "MAP_PurgeDirectiveRating.Home.Overview.Maxed".Translate(
                    PurgeDirectiveRatingDisplay.RatingName(level), value)
                : "MAP_PurgeDirectiveRating.Home.Overview.Level".Translate(
                    PurgeDirectiveRatingDisplay.RatingName(level), value, next);
            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(inner.x, inner.y, inner.width, 22f),
                title,
                GameFont.Small,
                TextAnchor.MiddleLeft,
                MechanoidOvermindUiStyle.AccentBright);

            float barY = inner.y + 26f;
            float barH = 12f;
            float span = next - levelStart;
            float ratio = span <= 0f
                ? 1f
                : Mathf.Clamp01((float)(value - levelStart) / span);
            Rect barRect = new Rect(inner.x, barY, inner.width, barH);
            Widgets.FillableBar(barRect, ratio);

            int discountPercent = Mathf.RoundToInt(
                PurgeDirectiveRatingUtility.GetDiscountRate() * 100f);
            string hint = "MAP_PurgeDirectiveRating.Home.Overview.Hint".Translate(
                discountPercent);
            MechanoidOvermindUiStyle.DrawSecondaryLabel(
                new Rect(inner.x, barY + barH + 2f, inner.width, 16f),
                hint,
                TextAnchor.MiddleLeft);

            PurgeDirectiveRatingDisplay display = PurgeDirectiveRatingDisplay.Build();
            string questSummary = display.CurrentQuestTargetLabel.NullOrEmpty()
                ? "MAP_PurgeDirectiveRating.Home.Overview.QuestIdle".Translate(
                    display.CurrentQuestStage)
                : "MAP_PurgeDirectiveRating.Home.Overview.QuestActive".Translate(
                    display.CurrentQuestStage,
                    display.CurrentQuestTargetLabel,
                    display.OfferOrActionRemainingTicks.ToStringTicksToPeriod());
            MechanoidOvermindUiStyle.DrawSecondaryLabel(
                new Rect(inner.x, barY + barH + 18f, inner.width, 16f),
                questSummary,
                TextAnchor.MiddleLeft);
        }

        private static void DrawTakeoverOverview(Rect rect)
        {
            MechanoidOvermindUiStyle.DrawPanel(rect);
            Rect inner = rect.ContractedBy(8f);
            MechanoidOvermindUiStyle.DrawLabel(
                new Rect(inner.x, inner.y, inner.width, 22f),
                "MAP_PurgeDirectiveRating.Takeover.Name".Translate(),
                GameFont.Small,
                TextAnchor.MiddleLeft,
                MechanoidOvermindUiStyle.AccentBright);
            MechanoidOvermindUiStyle.DrawSecondaryLabel(
                new Rect(inner.x, inner.y + 26f, inner.width, 16f),
                "MAP_PurgeDirectiveRating.Takeover.HomeHint".Translate(),
                TextAnchor.MiddleLeft);
        }

        private static string RatingSummaryCommunication()
        {
            if (!PurgeDirectiveRatingUtility.IsRatingSystemActive())
            {
                return string.Empty;
            }

            int level = PurgeDirectiveRatingUtility.CurrentRatingLevel;
            int value = PurgeDirectiveRatingUtility.CurrentRatingValue();
            int next = PurgeDirectiveRatingUtility.Config.GetNextLevelStart(level);
            return PurgeDirectiveRatingUtility.IsMaxRatingLevel()
                ? "MAP_PurgeDirectiveRating.Home.Card.Communication.Maxed".Translate(
                    PurgeDirectiveRatingDisplay.RatingName(level), value)
                : "MAP_PurgeDirectiveRating.Home.Card.Communication.Level".Translate(
                    PurgeDirectiveRatingDisplay.RatingName(level), value, next);
        }

        private static string RatingSummaryMechs()
        {
            if (!PurgeDirectiveRatingUtility.IsRatingSystemActive())
            {
                return string.Empty;
            }

            return "MAP_PurgeDirectiveRating.Home.Card.Mechs".Translate(
                WeightsUnlockedLabel(PurgeDirectiveRatingUtility.CurrentRatingLevel));
        }

        private static string RatingSummaryGoods()
        {
            if (!PurgeDirectiveRatingUtility.IsRatingSystemActive())
            {
                return string.Empty;
            }

            int level = PurgeDirectiveRatingUtility.CurrentRatingLevel;
            int goodsLevel = Mathf.Min(level, 3);
            string tier = goodsLevel >= 3
                ? "MAP_PurgeDirectiveRating.Goods.Tier3".Translate()
                : goodsLevel >= 2
                    ? "MAP_PurgeDirectiveRating.Goods.Tier2".Translate()
                    : "MAP_PurgeDirectiveRating.Goods.Tier1".Translate();
            float discount = PurgeDirectiveRatingUtility.GetDiscountRate();
            return "MAP_PurgeDirectiveRating.Home.Card.Goods".Translate(
                tier,
                Mathf.RoundToInt(discount * 100f));
        }

        private static string RatingSummarySpecialProtocols()
        {
            if (!PurgeDirectiveRatingUtility.IsRatingSystemActive())
            {
                return string.Empty;
            }

            int level = PurgeDirectiveRatingUtility.CurrentRatingLevel;
            bool cluster = PurgeDirectiveRatingUtility.IsClusterAvailable();
            bool force = PurgeDirectiveRatingUtility.IsForceSupportAvailable();
            return "MAP_PurgeDirectiveRating.Home.Card.SpecialProtocols".Translate(
                PurgeDirectiveRatingDisplay.RatingName(level),
                cluster ? "MAP_PurgeDirectiveRating.Word.Available".Translate()
                    : "MAP_PurgeDirectiveRating.Word.Locked".Translate(),
                force ? "MAP_PurgeDirectiveRating.Word.Available".Translate()
                    : "MAP_PurgeDirectiveRating.Word.Locked".Translate());
        }

        private static string WeightsUnlockedLabel(int level)
        {
            switch (level)
            {
                case 0: return "MAP_PurgeDirectiveRating.Word.None".Translate();
                case 1: return "MAP_PurgeDirectiveRating.Weight.Light".Translate();
                case 2: return "MAP_PurgeDirectiveRating.Weight.LightMedium".Translate();
                case 3: return "MAP_PurgeDirectiveRating.Weight.LightMediumHeavy".Translate();
                case 4: return "MAP_PurgeDirectiveRating.Weight.LightMediumHeavyUltra".Translate();
                default: return "MAP_PurgeDirectiveRating.Word.All".Translate();
            }
        }

        private static bool DrawCard(
            Rect rect,
            string node,
            string title,
            string summary,
            bool inputEnabled)
        {
            string nodeLabel =
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.Home.Node".Translate(node);
            bool clicked = MechanoidOvermindUiStyle.DrawMenuCard(
                rect,
                nodeLabel,
                title,
                inputEnabled,
                compactLayout: true);

            if (!summary.NullOrEmpty())
            {
                float lineH = 16f;
                MechanoidOvermindUiStyle.DrawSecondaryLabel(
                    new Rect(rect.x + 14f, rect.yMax - lineH - 6f, rect.width - 28f, lineH),
                    summary,
                    TextAnchor.MiddleLeft);
            }

            return clicked;
        }
    }
}
