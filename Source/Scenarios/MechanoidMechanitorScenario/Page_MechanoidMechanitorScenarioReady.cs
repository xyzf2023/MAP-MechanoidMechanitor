using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class Page_MechanoidMechanitorScenarioReady : Page
    {
        private const float DescriptionGap = 10f;
        private const float CardsToIdeoGap = 14f;

        // 剧情风格卡片
        private const float StoryStyleCardGap = 18f;
        private const float StoryStyleCardMaxWidth = 190f;
        private const float StoryStyleCardMinWidth = 120f;
        private const float StoryStyleCardPadding = 8f;
        private const float StoryStyleIconSize = 48f;
        private const float StoryStyleTitleIconGap = 4f;
        private const float StoryStyleIconDescGap = 6f;
        private const int StoryStyleDescLines = 3;
        private const float StoryStyleDescScrollSpeed = 10f;
        private const float StoryStyleDescScrollTopPause = 1f;
        private const float StoryStyleDescScrollBottomPause = 1.5f;

        // 文化适配横向卡片
        private const float IdeoCardPadding = 12f;
        private const float IdeoTitleDescGap = 4f;
        private const float IdeoDescButtonsGap = 10f;
        private const float IdeoButtonRowHeight = 32f;
        private const float IdeoButtonGap = 8f;

        // 文化适配按钮 Tooltip 的稳定基础 ID，加上枚举值得到每个按钮独立且稳定的 ID。
        // 与 ScenPart_MechanoidMechanitor.TooltipId(684272) 等现有 ID 不冲突。
        private const int AdaptationTooltipIdBase = 748120;

        private static readonly Color CardBgColor = new Color(0.14f, 0.14f, 0.14f, 0.85f);
        private static readonly Color CardBgSelectedColor = new Color(0.20f, 0.18f, 0.14f, 0.92f);
        private static readonly Color CardOutlineColor = new Color(0.42f, 0.37f, 0.30f, 0.40f);
        private static readonly Color CardOutlineSelectedColor = new Color(0.70f, 0.56f, 0.36f, 0.92f);
        private static readonly Color CardDescriptionColor = new Color(0.74f, 0.74f, 0.74f, 1f);

        private static readonly Color IdeoCardBgColor = new Color(0.12f, 0.12f, 0.12f, 0.85f);
        private static readonly Color IdeoCardOutlineColor = new Color(0.48f, 0.40f, 0.28f, 0.55f);
        private static readonly Color IdeoDescriptionColor = new Color(0.76f, 0.76f, 0.76f, 1f);
        private static readonly Color IdeoSegmentBgColor = new Color(0.17f, 0.15f, 0.13f, 1f);
        private static readonly Color IdeoSegmentBgHoverColor = new Color(0.24f, 0.21f, 0.17f, 1f);
        private static readonly Color IdeoSegmentBgSelectedColor = new Color(0.30f, 0.25f, 0.17f, 1f);
        private static readonly Color IdeoSegmentOutlineColor = new Color(0.46f, 0.39f, 0.28f, 0.55f);
        private static readonly Color IdeoSegmentOutlineSelectedColor = new Color(0.78f, 0.62f, 0.38f, 0.95f);

        private static readonly MechanoidMechanitorIdeologyAdaptationLevel[] AdaptationOptions =
        {
            MechanoidMechanitorIdeologyAdaptationLevel.Disabled,
            MechanoidMechanitorIdeologyAdaptationLevel.Basic,
            MechanoidMechanitorIdeologyAdaptationLevel.Partial,
            MechanoidMechanitorIdeologyAdaptationLevel.Full
        };

        private MechanoidMechanitorStoryStyleDef? selectedStoryStyle;
        private bool loggedNoStylesAvailable;

        // 页面打开时刻的现实时间锚点；描述滚动用 realtimeSinceStartup，不受暂停与游戏速度影响。
        private readonly float descriptionScrollStartRealTime;

        // 该草稿同时是本页“文化适配等级”的唯一选择状态，往返自定义页面时保持不变。
        private readonly MechanoidMechanitorStoryConfiguration customConfigurationDraft =
            MechanoidMechanitorStoryConfiguration.CreateDefault();

        public Page_MechanoidMechanitorScenarioReady()
        {
            descriptionScrollStartRealTime = Time.realtimeSinceStartup;
            EnsureValidSelectedStoryStyle(GetAvailableStoryStylesSorted());
        }

        public override string PageTitle =>
            "MAP_MechanoidMechanitor.Scenario.ReadyPage.Title".Translate();

        public override void DoWindowContents(Rect inRect)
        {
            DrawPageTitle(inRect);

            Rect mainRect = GetMainRect(inRect);
            TaggedString description =
                "MAP_MechanoidMechanitor.Scenario.ReadyPage.Text".Translate();
            float descriptionHeight = Text.CalcHeight(description, mainRect.width);
            Widgets.Label(
                new Rect(mainRect.x, mainRect.y, mainRect.width, descriptionHeight),
                description);

            bool showIdeoArea = ModsConfig.IdeologyActive;
            float ideoAreaHeight = showIdeoArea
                ? GetIdeologyAdaptationAreaHeight(mainRect.width)
                : 0f;

            float cardsAreaTop = mainRect.y + descriptionHeight + DescriptionGap;
            float cardsAreaBottom = showIdeoArea
                ? mainRect.yMax - ideoAreaHeight - CardsToIdeoGap
                : mainRect.yMax;
            float cardsAreaHeight = Mathf.Max(0f, cardsAreaBottom - cardsAreaTop);

            DrawStoryStyleCards(
                new Rect(mainRect.x, cardsAreaTop, mainRect.width, cardsAreaHeight));

            if (showIdeoArea)
            {
                DrawIdeologyAdaptationArea(
                    new Rect(
                        mainRect.x,
                        mainRect.yMax - ideoAreaHeight,
                        mainRect.width,
                        ideoAreaHeight));
            }

            DoBottomButtons(inRect, nextLabel: GetNextButtonLabel());
        }

        private string GetNextButtonLabel()
        {
            if (selectedStoryStyle != null && selectedStoryStyle.opensCustomizePage)
            {
                return "MAP_MechanoidMechanitor.Scenario.ReadyPage.Continue".Translate();
            }

            return "MAP_MechanoidMechanitor.Scenario.ReadyPage.StartGame".Translate();
        }

        protected override bool CanDoNext()
        {
            if (!base.CanDoNext())
            {
                return false;
            }

            return selectedStoryStyle != null;
        }

        protected override void DoNext()
        {
            List<MechanoidMechanitorStoryStyleDef> availableStyles =
                GetAvailableStoryStylesSorted();
            EnsureValidSelectedStoryStyle(availableStyles);

            if (availableStyles.Count == 0)
            {
                Log.Error(
                    "[MAP-机械族机械师] 无法保存剧情风格：未找到可用的剧情风格 Def。");
                Messages.Message(
                    "MAP_MechanoidMechanitor.Scenario.ReadyPage.NoStylesAvailable".Translate(),
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            if (selectedStoryStyle == null
                || !availableStyles.Contains(selectedStoryStyle))
            {
                Log.Error(
                    "[MAP-机械族机械师] 无法保存剧情风格：当前页面未选择有效的剧情风格。");
                Messages.Message(
                    "MAP_MechanoidMechanitor.Scenario.ReadyPage.SaveFailed".Translate(),
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            MechanoidMechanitorStoryStyleDef storyStyle = selectedStoryStyle;

            if (storyStyle.opensCustomizePage)
            {
                OpenCustomizePage();
                return;
            }

            MechanoidMechanitorScenarioStartConfirmationUtility.Show(
                () => ConfirmPresetAndStart(storyStyle));
        }

        private void ConfirmPresetAndStart(MechanoidMechanitorStoryStyleDef storyStyle)
        {
            if (!TryCommitPresetConfiguration(storyStyle))
            {
                return;
            }

            base.DoNext();
        }

        private void OpenCustomizePage()
        {
            selectedStoryStyle = MechanoidMechanitorStoryStyleDefOf.MAP_StoryStyle_Custom
                ?? selectedStoryStyle;

            // 剧情风格页面选择的文化适配等级已保存在 customConfigurationDraft 上（即传递给自定义
            // 页面的同一份草稿），自定义页面不再绘制或修改该字段，最终提交时随完整配置正常保存。
            MechanoidMechanitorStoryConfigurationContext context =
                MechanoidMechanitorStoryConfigurationContext.Create(customConfigurationDraft);
            customConfigurationDraft.SyncOrdinaryFactionEntries(context);
            customConfigurationDraft.Normalize(context);

            Page_MechanoidMechanitorStoryCustomize customizePage =
                new Page_MechanoidMechanitorStoryCustomize(customConfigurationDraft)
                {
                    prev = this,
                    next = next,
                    nextAct = nextAct
                };

            Find.WindowStack.Add(customizePage);
            Close();
        }

        private bool TryCommitPresetConfiguration(MechanoidMechanitorStoryStyleDef storyStyle)
        {
            if (Current.Game == null)
            {
                Log.Error(
                    "[MAP-机械族机械师] 无法保存剧情风格：当前没有有效的 Game 实例。");
                Messages.Message(
                    "MAP_MechanoidMechanitor.Scenario.ReadyPage.SaveFailed".Translate(),
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return false;
            }

            GameComponent_MechanoidMechanitorStoryState? component =
                Current.Game.GetComponent<GameComponent_MechanoidMechanitorStoryState>();
            if (component == null)
            {
                Log.Error(
                    "[MAP-机械族机械师] 无法保存剧情风格：缺少 GameComponent_MechanoidMechanitorStoryState 组件。");
                Messages.Message(
                    "MAP_MechanoidMechanitor.Scenario.ReadyPage.SaveFailed".Translate(),
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return false;
            }

            // 先根据剧情风格创建预设配置快照，再写入本页选择的文化适配等级，然后 Normalize 并提交。
            MechanoidMechanitorStoryConfiguration snapshot =
                storyStyle.CreateConfigurationSnapshot();
            snapshot.ideologyAdaptationLevel = customConfigurationDraft.ideologyAdaptationLevel;
            MechanoidMechanitorStoryConfigurationContext context =
                MechanoidMechanitorStoryConfigurationContext.Create(snapshot);
            snapshot.Normalize(context);
            component.SetStoryStyleForNewGame(storyStyle, snapshot);
            return true;
        }

        private void DrawStoryStyleCards(Rect area)
        {
            List<MechanoidMechanitorStoryStyleDef> styles = GetAvailableStoryStylesSorted();
            EnsureValidSelectedStoryStyle(styles);

            if (styles.Count == 0)
            {
                TextAnchor previousAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(
                    area,
                    "MAP_MechanoidMechanitor.Scenario.ReadyPage.NoStylesAvailable".Translate());
                Text.Anchor = previousAnchor;
                return;
            }

            int count = styles.Count;

            // 每行在不低于最小宽度前提下最多容纳的卡片数（至少 1）；卡片总数较少时按实际数量。
            int maxColumnsByWidth = Mathf.FloorToInt(
                (area.width + StoryStyleCardGap)
                / (StoryStyleCardMinWidth + StoryStyleCardGap));
            int columns = Mathf.Clamp(maxColumnsByWidth, 1, count);
            int rows = Mathf.CeilToInt((float)count / columns);

            float cardWidth = Mathf.Clamp(
                (area.width - StoryStyleCardGap * (columns - 1)) / columns,
                StoryStyleCardMinWidth,
                StoryStyleCardMaxWidth);

            // 行高按可用高度分配：单行时使用理想高度；多行或高度不足时压缩（说明区域随高度自适应）。
            float preferredCardHeight = GetStoryStyleCardHeight();
            float availableForRows = area.height - StoryStyleCardGap * (rows - 1);
            float cardHeight = Mathf.Min(
                preferredCardHeight,
                Mathf.Max(1f, availableForRows / rows));

            float totalBlockHeight = cardHeight * rows + StoryStyleCardGap * (rows - 1);
            float startY = area.y + Mathf.Max(0f, (area.height - totalBlockHeight) / 2f);

            int index = 0;
            for (int r = 0; r < rows && index < count; r++)
            {
                int itemsThisRow = Mathf.Min(columns, count - index);
                float rowWidth =
                    cardWidth * itemsThisRow + StoryStyleCardGap * (itemsThisRow - 1);
                float rowX = area.x + Mathf.Max(0f, (area.width - rowWidth) / 2f);
                float rowY = startY + r * (cardHeight + StoryStyleCardGap);

                for (int c = 0; c < itemsThisRow; c++)
                {
                    Rect cardRect = new Rect(
                        rowX + c * (cardWidth + StoryStyleCardGap),
                        rowY,
                        cardWidth,
                        cardHeight);
                    DrawStoryStyle(cardRect, styles[index]);
                    index++;
                }
            }
        }

        private static float GetStoryStyleCardHeight()
        {
            float titleHeight;
            float descHeight;
            GameFont previousFont = Text.Font;
            try
            {
                Text.Font = GameFont.Small;
                titleHeight = Text.LineHeight;
                descHeight = Text.LineHeight * StoryStyleDescLines;
            }
            finally
            {
                Text.Font = previousFont;
            }

            return StoryStyleCardPadding
                + titleHeight
                + StoryStyleTitleIconGap
                + StoryStyleIconSize
                + StoryStyleIconDescGap
                + descHeight
                + StoryStyleCardPadding;
        }

        private void DrawStoryStyle(Rect rect, MechanoidMechanitorStoryStyleDef style)
        {
            bool selected = selectedStoryStyle == style;

            Widgets.DrawBoxSolid(rect, selected ? CardBgSelectedColor : CardBgColor);
            Widgets.DrawHighlightIfMouseover(rect);

            Color previousColor = GUI.color;
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            bool previousWordWrap = Text.WordWrap;
            try
            {
                GUI.color = selected ? CardOutlineSelectedColor : CardOutlineColor;
                Widgets.DrawBox(rect, selected ? 2 : 1);
                GUI.color = previousColor;

                float innerX = rect.x + StoryStyleCardPadding;
                float innerWidth = rect.width - StoryStyleCardPadding * 2f;

                Text.Font = GameFont.Small;
                Text.WordWrap = false;
                Text.Anchor = TextAnchor.UpperCenter;
                GUI.color = selected ? Color.white : new Color(0.88f, 0.88f, 0.88f, 1f);
                float titleY = rect.y + StoryStyleCardPadding;
                float titleHeight = Text.LineHeight;
                Widgets.Label(
                    new Rect(innerX, titleY, innerWidth, titleHeight),
                    style.LabelCap);

                float iconY = titleY + titleHeight + StoryStyleTitleIconGap;
                // 卡片被压缩时自适应缩小图标，保证标题+图标始终落在卡片内、不溢出边界。
                float iconSpace = rect.yMax - StoryStyleCardPadding - iconY;
                float iconSize = Mathf.Clamp(iconSpace, 0f, StoryStyleIconSize);
                Rect iconRect = new Rect(
                    rect.x + (rect.width - iconSize) / 2f,
                    iconY,
                    iconSize,
                    iconSize);
                GUI.color = Color.white;
                Texture2D? customIcon = style.IconTexture;
                if (customIcon != null)
                {
                    Widgets.DrawTextureFitted(iconRect, customIcon, 1f);
                }
                else if (style.iconThingDef != null)
                {
                    Widgets.ThingIcon(iconRect, style.iconThingDef);
                }

                float descY = iconY + iconSize + StoryStyleIconDescGap;
                float descHeight = rect.yMax - StoryStyleCardPadding - descY;
                if (descHeight > 0f && !style.description.NullOrEmpty())
                {
                    DrawAutoScrollingDescription(
                        new Rect(innerX, descY, innerWidth, descHeight),
                        style.description);
                }
            }
            finally
            {
                GUI.color = previousColor;
                Text.Font = previousFont;
                Text.Anchor = previousAnchor;
                Text.WordWrap = previousWordWrap;
            }

            TooltipHandler.TipRegion(rect, GetStoryStyleTooltip(style));

            if (Widgets.ButtonInvisible(rect))
            {
                if (selectedStoryStyle != style)
                {
                    selectedStoryStyle = style;
                    SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
                }
            }
        }

        private void DrawAutoScrollingDescription(Rect rect, string description)
        {
            Color previousColor = GUI.color;
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            bool previousWordWrap = Text.WordWrap;
            try
            {
                Text.Font = GameFont.Small;
                Text.WordWrap = true;
                Text.Anchor = TextAnchor.UpperCenter;
                GUI.color = CardDescriptionColor;

                float fullTextHeight = Text.CalcHeight(description, rect.width);
                if (fullTextHeight <= rect.height)
                {
                    Widgets.Label(rect, description);
                    return;
                }

                float overflowHeight = fullTextHeight - rect.height;
                float travelDuration = overflowHeight / StoryStyleDescScrollSpeed;
                float cycleDuration = StoryStyleDescScrollTopPause
                    + travelDuration
                    + StoryStyleDescScrollBottomPause;
                float elapsedInCycle =
                    (Time.realtimeSinceStartup - descriptionScrollStartRealTime)
                    % cycleDuration;
                float offset;
                if (elapsedInCycle < StoryStyleDescScrollTopPause)
                {
                    offset = 0f;
                }
                else if (elapsedInCycle < StoryStyleDescScrollTopPause + travelDuration)
                {
                    offset = (elapsedInCycle - StoryStyleDescScrollTopPause)
                        * StoryStyleDescScrollSpeed;
                }
                else
                {
                    offset = overflowHeight;
                }

                Widgets.BeginGroup(rect);
                try
                {
                    Widgets.Label(
                        new Rect(0f, -offset, rect.width, fullTextHeight),
                        description);
                }
                finally
                {
                    Widgets.EndGroup();
                }
            }
            finally
            {
                GUI.color = previousColor;
                Text.Font = previousFont;
                Text.Anchor = previousAnchor;
                Text.WordWrap = previousWordWrap;
            }
        }

        private static string GetStoryStyleTooltip(MechanoidMechanitorStoryStyleDef style)
        {
            if (style.opensCustomizePage || style.presetConfiguration == null)
            {
                return BuildNameDescriptionTooltip(style);
            }

            MechanoidMechanitorStoryConfiguration snapshot =
                style.CreateConfigurationSnapshot();
            string? summary = BuildPresetConfigurationSummary(snapshot);
            if (summary.NullOrEmpty())
            {
                return BuildNameDescriptionTooltip(style);
            }

            return style.LabelCap.Colorize(ColoredText.TipSectionTitleColor)
                + "\n\n"
                + summary;
        }

        private static string BuildNameDescriptionTooltip(
            MechanoidMechanitorStoryStyleDef style)
        {
            return style.LabelCap.Colorize(ColoredText.TipSectionTitleColor)
                + "\n\n"
                + style.description;
        }

        private static string? BuildPresetConfigurationSummary(
            MechanoidMechanitorStoryConfiguration configuration)
        {
            List<MechanoidMechanitorStoryComponentDef> components =
                DefDatabase<MechanoidMechanitorStoryComponentDef>
                    .AllDefsListForReading
                    .OrderBy(def => def.displayOrder)
                    .ThenBy(def => def.defName, StringComparer.Ordinal)
                    .ToList();

            StringBuilder? builder = null;
            for (int i = 0; i < components.Count; i++)
            {
                MechanoidMechanitorStoryComponentDef component = components[i];
                MechanoidMechanitorStoryComponentWorker? worker = component.Worker;
                if (worker == null)
                {
                    continue;
                }

                string? value = worker.GetSummaryValue(configuration);
                if (value.NullOrEmpty())
                {
                    continue;
                }

                if (builder == null)
                {
                    builder = new StringBuilder();
                }
                else
                {
                    builder.AppendLine();
                }

                builder.Append(component.LabelCap);
                builder.Append('：');
                builder.Append(value);
            }

            return builder?.ToString();
        }

        private float GetIdeologyAdaptationAreaHeight(float width)
        {
            float innerWidth = Mathf.Max(1f, width - IdeoCardPadding * 2f);
            float titleHeight;
            float descHeight;
            GameFont previousFont = Text.Font;
            bool previousWordWrap = Text.WordWrap;
            try
            {
                Text.Font = GameFont.Small;
                Text.WordWrap = true;
                titleHeight = Text.CalcHeight(GetIdeologyAdaptationTitle(), innerWidth);
                descHeight = Text.CalcHeight(GetIdeologyAdaptationDescription(), innerWidth);
            }
            finally
            {
                Text.Font = previousFont;
                Text.WordWrap = previousWordWrap;
            }

            return IdeoCardPadding
                + titleHeight
                + IdeoTitleDescGap
                + descHeight
                + IdeoDescButtonsGap
                + IdeoButtonRowHeight
                + IdeoCardPadding;
        }

        private void DrawIdeologyAdaptationArea(Rect rect)
        {
            Widgets.DrawBoxSolidWithOutline(rect, IdeoCardBgColor, IdeoCardOutlineColor);

            Color previousColor = GUI.color;
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            bool previousWordWrap = Text.WordWrap;
            try
            {
                float innerX = rect.x + IdeoCardPadding;
                float innerWidth = rect.width - IdeoCardPadding * 2f;

                Text.Font = GameFont.Small;
                Text.WordWrap = true;
                Text.Anchor = TextAnchor.UpperLeft;

                TaggedString title = GetIdeologyAdaptationTitle();
                float titleHeight = Text.CalcHeight(title, innerWidth);
                GUI.color = Color.white;
                Widgets.Label(
                    new Rect(innerX, rect.y + IdeoCardPadding, innerWidth, titleHeight),
                    title);

                TaggedString desc = GetIdeologyAdaptationDescription();
                float descY = rect.y + IdeoCardPadding + titleHeight + IdeoTitleDescGap;
                float descHeight = Text.CalcHeight(desc, innerWidth);
                GUI.color = IdeoDescriptionColor;
                Widgets.Label(
                    new Rect(innerX, descY, innerWidth, descHeight),
                    desc);

                GUI.color = Color.white;
                float buttonsY = descY + descHeight + IdeoDescButtonsGap;
                DrawAdaptationButtons(new Rect(innerX, buttonsY, innerWidth, IdeoButtonRowHeight));
            }
            finally
            {
                GUI.color = previousColor;
                Text.Font = previousFont;
                Text.Anchor = previousAnchor;
                Text.WordWrap = previousWordWrap;
            }
        }

        private void DrawAdaptationButtons(Rect row)
        {
            int count = AdaptationOptions.Length;
            float buttonWidth =
                (row.width - IdeoButtonGap * (count - 1)) / count;

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            bool previousWordWrap = Text.WordWrap;
            Color previousColor = GUI.color;
            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.WordWrap = false;

                for (int i = 0; i < count; i++)
                {
                    MechanoidMechanitorIdeologyAdaptationLevel level = AdaptationOptions[i];
                    Rect buttonRect = new Rect(
                        row.x + i * (buttonWidth + IdeoButtonGap),
                        row.y,
                        buttonWidth,
                        row.height);

                    bool selected = customConfigurationDraft.ideologyAdaptationLevel == level;
                    Color bg = selected ? IdeoSegmentBgSelectedColor : IdeoSegmentBgColor;
                    Color outline = selected
                        ? IdeoSegmentOutlineSelectedColor
                        : IdeoSegmentOutlineColor;
                    if (!selected && Mouse.IsOver(buttonRect))
                    {
                        bg = IdeoSegmentBgHoverColor;
                    }

                    Widgets.DrawBoxSolidWithOutline(buttonRect, bg, outline);

                    GUI.color = selected ? Color.white : new Color(0.85f, 0.85f, 0.85f, 1f);
                    Widgets.Label(
                        buttonRect,
                        MechanoidMechanitorStoryConfigurationLabels.LabelFor(level));

                    // Tooltip 作用范围精确限制为当前按钮，使用稳定且互不冲突的 ID。
                    TooltipHandler.TipRegion(
                        buttonRect,
                        new TipSignal(
                            GetAdaptationTooltip(level),
                            AdaptationTooltipIdBase + (int)level));

                    if (Widgets.ButtonInvisible(buttonRect, doMouseoverSound: true))
                    {
                        if (customConfigurationDraft.ideologyAdaptationLevel != level)
                        {
                            customConfigurationDraft.ideologyAdaptationLevel = level;
                            SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
                        }
                    }
                }
            }
            finally
            {
                Text.Font = previousFont;
                Text.Anchor = previousAnchor;
                Text.WordWrap = previousWordWrap;
                GUI.color = previousColor;
            }
        }

        private static string GetAdaptationTooltip(
            MechanoidMechanitorIdeologyAdaptationLevel level)
        {
            string title = MechanoidMechanitorStoryConfigurationLabels.LabelFor(level);
            return title.Colorize(ColoredText.TipSectionTitleColor)
                + "\n\n"
                + GetAdaptationTooltipBody(level);
        }

        private static string GetAdaptationTooltipBody(
            MechanoidMechanitorIdeologyAdaptationLevel level)
        {
            return level switch
            {
                MechanoidMechanitorIdeologyAdaptationLevel.Disabled =>
                    "MAP_MechanoidMechanitor.Scenario.ReadyPage.IdeologyAdaptation.Tooltip.Disabled"
                        .Translate(),
                MechanoidMechanitorIdeologyAdaptationLevel.Basic =>
                    "MAP_MechanoidMechanitor.Scenario.ReadyPage.IdeologyAdaptation.Tooltip.Basic"
                        .Translate(),
                MechanoidMechanitorIdeologyAdaptationLevel.Partial =>
                    "MAP_MechanoidMechanitor.Scenario.ReadyPage.IdeologyAdaptation.Tooltip.Partial"
                        .Translate(),
                MechanoidMechanitorIdeologyAdaptationLevel.Full =>
                    "MAP_MechanoidMechanitor.Scenario.ReadyPage.IdeologyAdaptation.Tooltip.Full"
                        .Translate(),
                _ => string.Empty
            };
        }

        private static TaggedString GetIdeologyAdaptationTitle()
        {
            return "MAP_MechanoidMechanitor.Scenario.ReadyPage.IdeologyAdaptation.Title"
                .Translate();
        }

        private static TaggedString GetIdeologyAdaptationDescription()
        {
            return "MAP_MechanoidMechanitor.Scenario.ReadyPage.IdeologyAdaptation.Description"
                .Translate();
        }

        private static List<MechanoidMechanitorStoryStyleDef> GetAvailableStoryStylesSorted()
        {
            return DefDatabase<MechanoidMechanitorStoryStyleDef>
                .AllDefsListForReading
                .OrderBy(def => def.displayOrder)
                .ThenBy(def => def.defName, StringComparer.Ordinal)
                .ToList();
        }

        private void EnsureValidSelectedStoryStyle(
            List<MechanoidMechanitorStoryStyleDef> availableStyles)
        {
            if (availableStyles.Count == 0)
            {
                selectedStoryStyle = null;
                if (!loggedNoStylesAvailable)
                {
                    loggedNoStylesAvailable = true;
                    Log.Error(
                        "[MAP-机械族机械师] 未找到可用的剧情风格 Def。请检查 MOD 配置。");
                }

                return;
            }

            if (selectedStoryStyle != null
                && availableStyles.Contains(selectedStoryStyle))
            {
                return;
            }

            MechanoidMechanitorStoryStyleDef classic =
                MechanoidMechanitorStoryStyleDefOf.MAP_StoryStyle_Classic;
            if (classic != null && availableStyles.Contains(classic))
            {
                selectedStoryStyle = classic;
                return;
            }

            selectedStoryStyle = availableStyles[0];
        }
    }
}
