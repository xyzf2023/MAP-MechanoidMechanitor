using System;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 自定义剧情中的普通派系前哨配置。频率控制生成尝试；三项权重只在生成瞬间
    /// 选择当前关系类别，既有前哨之后始终跟随所属 Faction 的真实关系。
    /// </summary>
    public sealed class MechanoidMechanitorStoryComponentWorker_FactionOutpost
        : MechanoidMechanitorStoryComponentWorker
    {
        private const float PanelTopGap = 10f;
        private const float PanelInset = 10f;
        private const float PanelPaddingY = 8f;
        private const float WeightRowHeight = 34f;
        private const float WeightLabelWidth = 145f;
        private const float WeightValueWidth = 34f;
        private const float WeightGap = 8f;

        private static readonly Color PanelBgColor = new Color(0.11f, 0.11f, 0.11f, 1f);
        private static readonly Color PanelOutlineColor = new Color(0.40f, 0.34f, 0.26f, 0.40f);
        private static readonly Color DisabledTextColor = new Color(1f, 1f, 1f, 0.42f);

        private static readonly MechanoidMechanitorFactionOutpostFrequency[] Frequencies =
        {
            MechanoidMechanitorFactionOutpostFrequency.Off,
            MechanoidMechanitorFactionOutpostFrequency.Low,
            MechanoidMechanitorFactionOutpostFrequency.Medium,
            MechanoidMechanitorFactionOutpostFrequency.High
        };

        public override bool ShouldShow(MechanoidMechanitorStoryConfigurationContext context)
        {
            return FactionOutpostFactionUtility.HasAnyEligibleFaction();
        }

        public override string? GetSummaryValue(
            MechanoidMechanitorStoryConfiguration configuration)
        {
            return LabelFor(configuration.factionOutpostFrequency);
        }

        public override float GetHeight(
            MechanoidMechanitorStoryConfigurationContext context,
            float width)
        {
            return MeasureCardHeight(context, width, MeasureWeightPanelHeight());
        }

        public override void Draw(
            Rect rect,
            MechanoidMechanitorStoryConfigurationContext context)
        {
            MechanoidMechanitorStoryConfiguration configuration = context.Configuration;
            float contentY = DrawCardHeaderAndDropdown(
                rect,
                context,
                LabelFor(configuration.factionOutpostFrequency),
                enabled: true,
                () => OpenDropdownMenu(
                    Frequencies,
                    LabelFor,
                    frequency =>
                    {
                        if (configuration.factionOutpostFrequency == frequency)
                        {
                            return;
                        }

                        configuration.factionOutpostFrequency = frequency;
                        NormalizeAfterChange(context);
                    }));

            DrawWeightPanel(
                rect,
                contentY,
                context,
                configuration,
                configuration.factionOutpostFrequency.IsEnabled());
        }

        private static float MeasureWeightPanelHeight()
        {
            return PanelTopGap
                + PanelPaddingY * 2f
                + WeightRowHeight * 3f;
        }

        private void DrawWeightPanel(
            Rect cardRect,
            float contentY,
            MechanoidMechanitorStoryConfigurationContext context,
            MechanoidMechanitorStoryConfiguration configuration,
            bool enabled)
        {
            Rect inner = cardRect.ContractedBy(CardPadding);
            float panelHeight = PanelPaddingY * 2f + WeightRowHeight * 3f;
            Rect panelRect = new Rect(
                inner.x + PanelInset,
                contentY + PanelTopGap,
                Mathf.Max(1f, inner.width - PanelInset * 2f),
                panelHeight);
            Widgets.DrawBoxSolidWithOutline(panelRect, PanelBgColor, PanelOutlineColor);

            string tooltip =
                "MAP_MechanoidMechanitor.Story.FactionOutpost.Weight.Tooltip".Translate();
            float y = panelRect.y + PanelPaddingY;
            DrawWeightRow(
                new Rect(panelRect.x + 8f, y, panelRect.width - 16f, WeightRowHeight),
                "MAP_MechanoidMechanitor.Story.FactionOutpost.Weight.Hostile".Translate(),
                configuration.hostileFactionOutpostWeight,
                enabled,
                tooltip,
                value => configuration.hostileFactionOutpostWeight = value,
                context);
            y += WeightRowHeight;
            DrawWeightRow(
                new Rect(panelRect.x + 8f, y, panelRect.width - 16f, WeightRowHeight),
                "MAP_MechanoidMechanitor.Story.FactionOutpost.Weight.Ally".Translate(),
                configuration.allyFactionOutpostWeight,
                enabled,
                tooltip,
                value => configuration.allyFactionOutpostWeight = value,
                context);
            y += WeightRowHeight;
            DrawWeightRow(
                new Rect(panelRect.x + 8f, y, panelRect.width - 16f, WeightRowHeight),
                "MAP_MechanoidMechanitor.Story.FactionOutpost.Weight.Neutral".Translate(),
                configuration.neutralFactionOutpostWeight,
                enabled,
                tooltip,
                value => configuration.neutralFactionOutpostWeight = value,
                context);
        }

        private void DrawWeightRow(
            Rect rowRect,
            string label,
            int currentValue,
            bool enabled,
            string tooltip,
            Action<int> setter,
            MechanoidMechanitorStoryConfigurationContext context)
        {
            Widgets.DrawHighlightIfMouseover(rowRect);
            TooltipHandler.TipRegion(rowRect, tooltip);

            Rect labelRect = new Rect(rowRect.x, rowRect.y, WeightLabelWidth, rowRect.height);
            Rect valueRect = new Rect(
                rowRect.xMax - WeightValueWidth,
                rowRect.y,
                WeightValueWidth,
                rowRect.height);
            float sliderX = labelRect.xMax + WeightGap;
            Rect sliderRect = new Rect(
                sliderX,
                rowRect.y,
                Mathf.Max(20f, valueRect.x - WeightGap - sliderX),
                rowRect.height);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            bool previousWordWrap = Text.WordWrap;
            Color previousColor = GUI.color;
            bool previousEnabled = GUI.enabled;
            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.WordWrap = false;
                GUI.color = enabled ? Color.white : DisabledTextColor;
                Widgets.Label(labelRect, label.Truncate(labelRect.width));

                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(valueRect, currentValue.ToString());

                GUI.enabled = enabled;
                float newValue = Widgets.HorizontalSlider(
                    sliderRect,
                    currentValue,
                    0f,
                    100f,
                    middleAlignment: true,
                    label: null,
                    leftAlignedLabel: null,
                    rightAlignedLabel: null,
                    roundTo: 1f);
                if (enabled)
                {
                    int rounded = Mathf.Clamp(Mathf.RoundToInt(newValue), 0, 100);
                    if (rounded != currentValue)
                    {
                        setter(rounded);
                        NormalizeAfterChange(context);
                    }
                }
            }
            finally
            {
                GUI.enabled = previousEnabled;
                GUI.color = previousColor;
                Text.Font = previousFont;
                Text.Anchor = previousAnchor;
                Text.WordWrap = previousWordWrap;
            }
        }

        private static string LabelFor(MechanoidMechanitorFactionOutpostFrequency frequency)
        {
            return frequency switch
            {
                MechanoidMechanitorFactionOutpostFrequency.Off =>
                    "MAP_MechanoidMechanitor.Story.FactionOutpostFrequency.Off".Translate(),
                MechanoidMechanitorFactionOutpostFrequency.Low =>
                    "MAP_MechanoidMechanitor.Story.FactionOutpostFrequency.Low".Translate(),
                MechanoidMechanitorFactionOutpostFrequency.Medium =>
                    "MAP_MechanoidMechanitor.Story.FactionOutpostFrequency.Medium".Translate(),
                MechanoidMechanitorFactionOutpostFrequency.High =>
                    "MAP_MechanoidMechanitor.Story.FactionOutpostFrequency.High".Translate(),
                _ => frequency.ToString()
            };
        }
    }
}
