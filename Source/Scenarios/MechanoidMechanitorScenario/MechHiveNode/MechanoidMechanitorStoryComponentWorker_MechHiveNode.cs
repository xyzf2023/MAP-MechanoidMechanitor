using System;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 自定义风格中的“是否生成机械巢节点”配置卡片。仅在存在机械巢时显示。
    /// 战略效果区域暴露单节点袭击/支援概率，供玩家分别调整。
    /// </summary>
    public sealed class MechanoidMechanitorStoryComponentWorker_MechHiveNode
        : MechanoidMechanitorStoryComponentWorker
    {
        private const float PanelTopGap = 10f;
        private const float PanelInset = 10f;
        private const float PanelPaddingY = 8f;
        private const float SectionHeaderHeight = 24f;
        private const float SliderRowHeight = 34f;
        private const float LabelWidth = 170f;
        private const float ValueWidth = 48f;
        private const float SliderGap = 8f;

        private static readonly Color PanelBgColor = new Color(0.11f, 0.11f, 0.11f, 1f);
        private static readonly Color PanelOutlineColor = new Color(0.40f, 0.34f, 0.26f, 0.40f);
        private static readonly Color DisabledTextColor = new Color(1f, 1f, 1f, 0.42f);
        private static readonly Color SectionHeaderColor = new Color(0.74f, 0.68f, 0.56f, 1f);

        private static readonly MechanoidMechanitorMechHiveNodeFrequency[] Frequencies =
        {
            MechanoidMechanitorMechHiveNodeFrequency.Off,
            MechanoidMechanitorMechHiveNodeFrequency.Low,
            MechanoidMechanitorMechHiveNodeFrequency.Medium,
            MechanoidMechanitorMechHiveNodeFrequency.High
        };

        public override bool ShouldShow(MechanoidMechanitorStoryConfigurationContext context)
        {
            return context.HasMechHive;
        }

        public override string? GetSummaryValue(
            MechanoidMechanitorStoryConfiguration configuration)
        {
            return MechanoidMechanitorStoryConfigurationLabels.LabelFor(
                configuration.mechHiveNodeFrequency);
        }

        public override float GetHeight(
            MechanoidMechanitorStoryConfigurationContext context,
            float width)
        {
            return MeasureCardHeight(context, width, MeasureStrategicPanelHeight());
        }

        public override void Draw(
            Rect rect,
            MechanoidMechanitorStoryConfigurationContext context)
        {
            MechanoidMechanitorStoryConfiguration configuration = context.Configuration;
            float contentY = DrawCardHeaderAndDropdown(
                rect,
                context,
                MechanoidMechanitorStoryConfigurationLabels.LabelFor(
                    configuration.mechHiveNodeFrequency),
                enabled: true,
                () => OpenDropdownMenu(
                    Frequencies,
                    MechanoidMechanitorStoryConfigurationLabels.LabelFor,
                    frequency =>
                    {
                        if (configuration.mechHiveNodeFrequency == frequency)
                        {
                            return;
                        }

                        configuration.mechHiveNodeFrequency = frequency;
                        NormalizeAfterChange(context);
                    }));

            DrawStrategicPanel(
                rect,
                contentY,
                context,
                configuration,
                configuration.mechHiveNodeFrequency.IsEnabled());
        }

        private static float MeasureStrategicPanelHeight()
        {
            return PanelTopGap
                + PanelPaddingY * 2f
                + SectionHeaderHeight
                + SliderRowHeight * 2f;
        }

        private void DrawStrategicPanel(
            Rect cardRect,
            float contentY,
            MechanoidMechanitorStoryConfigurationContext context,
            MechanoidMechanitorStoryConfiguration configuration,
            bool enabled)
        {
            Rect inner = cardRect.ContractedBy(CardPadding);
            float panelHeight = MeasureStrategicPanelHeight();
            Rect panelRect = new Rect(
                inner.x + PanelInset,
                contentY + PanelTopGap,
                Mathf.Max(1f, inner.width - PanelInset * 2f),
                panelHeight);
            Widgets.DrawBoxSolidWithOutline(panelRect, PanelBgColor, PanelOutlineColor);

            float y = panelRect.y + PanelPaddingY;

            DrawSectionHeader(
                new Rect(panelRect.x + 8f, y, panelRect.width - 16f, SectionHeaderHeight),
                "MAP_MechanoidMechanitor.Story.MechHiveNode.Strategic.SectionTitle".Translate());
            y += SectionHeaderHeight;

            string strategicTooltip =
                "MAP_MechanoidMechanitor.Story.MechHiveNode.Strategic.Tooltip".Translate();
            DrawSliderRow(
                new Rect(panelRect.x + 8f, y, panelRect.width - 16f, SliderRowHeight),
                "MAP_MechanoidMechanitor.Story.MechHiveNode.Strategic.RaidChance".Translate(),
                configuration.mechHiveNodeRaidChancePercent,
                enabled,
                strategicTooltip,
                value => configuration.mechHiveNodeRaidChancePercent = value,
                context);
            y += SliderRowHeight;
            DrawSliderRow(
                new Rect(panelRect.x + 8f, y, panelRect.width - 16f, SliderRowHeight),
                "MAP_MechanoidMechanitor.Story.MechHiveNode.Strategic.SupportChance".Translate(),
                configuration.mechHiveNodeSupportChancePercent,
                enabled,
                strategicTooltip,
                value => configuration.mechHiveNodeSupportChancePercent = value,
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
            Action<int> setter,
            MechanoidMechanitorStoryConfigurationContext context)
        {
            Widgets.DrawHighlightIfMouseover(rowRect);
            TooltipHandler.TipRegion(rowRect, tooltip);

            Rect labelRect = new Rect(rowRect.x, rowRect.y, LabelWidth, rowRect.height);
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
                Widgets.Label(valueRect, currentValue.ToString() + "%");

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
    }
}
