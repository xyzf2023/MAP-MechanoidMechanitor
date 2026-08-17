using System;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 自定义剧情中的普通派系前哨配置。频率控制生成尝试；三项权重只在生成瞬间
    /// 选择当前关系类别，既有前哨之后始终跟随所属 Faction 的真实关系。
    /// 战略效果区域暴露单前哨袭击/支援概率，供玩家分别调整。
    /// </summary>
    public sealed class MechanoidMechanitorStoryComponentWorker_FactionOutpost
        : MechanoidMechanitorStoryComponentWorker
    {
        private const float PanelTopGap = 10f;
        private const float PanelInset = 10f;
        private const float PanelPaddingY = 8f;
        private const float SectionHeaderHeight = 24f;
        private const float SectionGap = 8f;
        private const float SliderRowHeight = 34f;
        private const float LabelWidth = 170f;
        private const float ValueWidth = 48f;
        private const float SliderGap = 8f;

        private static readonly Color PanelBgColor = new Color(0.11f, 0.11f, 0.11f, 1f);
        private static readonly Color PanelOutlineColor = new Color(0.40f, 0.34f, 0.26f, 0.40f);
        private static readonly Color DisabledTextColor = new Color(1f, 1f, 1f, 0.42f);
        private static readonly Color SectionHeaderColor = new Color(0.74f, 0.68f, 0.56f, 1f);

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
            return MeasureCardHeight(context, width, MeasureSettingsPanelHeight());
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

            DrawSettingsPanel(
                rect,
                contentY,
                context,
                configuration,
                configuration.factionOutpostFrequency.IsEnabled());
        }

        private static float MeasureSettingsPanelHeight()
        {
            return PanelTopGap
                + PanelPaddingY * 2f
                + SectionHeaderHeight
                + SliderRowHeight * 3f
                + SectionGap
                + SectionHeaderHeight
                + SliderRowHeight * 2f;
        }

        private void DrawSettingsPanel(
            Rect cardRect,
            float contentY,
            MechanoidMechanitorStoryConfigurationContext context,
            MechanoidMechanitorStoryConfiguration configuration,
            bool enabled)
        {
            Rect inner = cardRect.ContractedBy(CardPadding);
            float panelHeight = MeasureSettingsPanelHeight();
            Rect panelRect = new Rect(
                inner.x + PanelInset,
                contentY + PanelTopGap,
                Mathf.Max(1f, inner.width - PanelInset * 2f),
                panelHeight);
            Widgets.DrawBoxSolidWithOutline(panelRect, PanelBgColor, PanelOutlineColor);

            float y = panelRect.y + PanelPaddingY;

            // 第一段：生成关系权重
            DrawSectionHeader(
                new Rect(panelRect.x + 8f, y, panelRect.width - 16f, SectionHeaderHeight),
                "MAP_MechanoidMechanitor.Story.FactionOutpost.Weight.SectionTitle".Translate());
            y += SectionHeaderHeight;

            string weightTooltip =
                "MAP_MechanoidMechanitor.Story.FactionOutpost.Weight.Tooltip".Translate();
            DrawSliderRow(
                new Rect(panelRect.x + 8f, y, panelRect.width - 16f, SliderRowHeight),
                "MAP_MechanoidMechanitor.Story.FactionOutpost.Weight.Hostile".Translate(),
                configuration.hostileFactionOutpostWeight,
                enabled,
                weightTooltip,
                showPercent: false,
                value => configuration.hostileFactionOutpostWeight = value,
                context);
            y += SliderRowHeight;
            DrawSliderRow(
                new Rect(panelRect.x + 8f, y, panelRect.width - 16f, SliderRowHeight),
                "MAP_MechanoidMechanitor.Story.FactionOutpost.Weight.Ally".Translate(),
                configuration.allyFactionOutpostWeight,
                enabled,
                weightTooltip,
                showPercent: false,
                value => configuration.allyFactionOutpostWeight = value,
                context);
            y += SliderRowHeight;
            DrawSliderRow(
                new Rect(panelRect.x + 8f, y, panelRect.width - 16f, SliderRowHeight),
                "MAP_MechanoidMechanitor.Story.FactionOutpost.Weight.Neutral".Translate(),
                configuration.neutralFactionOutpostWeight,
                enabled,
                weightTooltip,
                showPercent: false,
                value => configuration.neutralFactionOutpostWeight = value,
                context);
            y += SliderRowHeight + SectionGap;

            // 第二段：战略效果
            DrawSectionHeader(
                new Rect(panelRect.x + 8f, y, panelRect.width - 16f, SectionHeaderHeight),
                "MAP_MechanoidMechanitor.Story.FactionOutpost.Strategic.SectionTitle".Translate());
            y += SectionHeaderHeight;

            string strategicTooltip =
                "MAP_MechanoidMechanitor.Story.FactionOutpost.Strategic.Tooltip".Translate();
            DrawSliderRow(
                new Rect(panelRect.x + 8f, y, panelRect.width - 16f, SliderRowHeight),
                "MAP_MechanoidMechanitor.Story.FactionOutpost.Strategic.RaidChance".Translate(),
                configuration.factionOutpostRaidChancePercent,
                enabled,
                strategicTooltip,
                showPercent: true,
                value => configuration.factionOutpostRaidChancePercent = value,
                context);
            y += SliderRowHeight;
            DrawSliderRow(
                new Rect(panelRect.x + 8f, y, panelRect.width - 16f, SliderRowHeight),
                "MAP_MechanoidMechanitor.Story.FactionOutpost.Strategic.SupportChance".Translate(),
                configuration.factionOutpostSupportChancePercent,
                enabled,
                strategicTooltip,
                showPercent: true,
                value => configuration.factionOutpostSupportChancePercent = value,
                context);
        }

        private static void DrawSectionHeader(Rect rect, string label)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            bool previousWordWrap = Text.WordWrap;
            Color previousColor = GUI.color;
            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.WordWrap = false;
                GUI.color = SectionHeaderColor;
                Widgets.Label(rect, label.Truncate(rect.width));
            }
            finally
            {
                GUI.color = previousColor;
                Text.Font = previousFont;
                Text.Anchor = previousAnchor;
                Text.WordWrap = previousWordWrap;
            }
        }

        private static void DrawSliderRow(
            Rect rowRect,
            string label,
            int currentValue,
            bool enabled,
            string tooltip,
            bool showPercent,
            Action<int> setter,
            MechanoidMechanitorStoryConfigurationContext context)
        {
            Widgets.DrawHighlightIfMouseover(rowRect);
            TooltipHandler.TipRegion(rowRect, tooltip);

            Rect labelRect = new Rect(rowRect.x, rowRect.y, LabelWidth, rowRect.height);
            string valueText = showPercent ? currentValue.ToString() + "%" : currentValue.ToString();
            Rect valueRect = new Rect(
                rowRect.xMax - ValueWidth,
                rowRect.y,
                ValueWidth,
                rowRect.height);
            float sliderX = labelRect.xMax + SliderGap;
            Rect sliderRect = new Rect(
                sliderX,
                rowRect.y,
                Mathf.Max(20f, valueRect.x - SliderGap - sliderX),
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
                Widgets.Label(valueRect, valueText);

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
