using System;
using System.Collections.Generic;
using System.Linq;
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

        // 文化适配横向卡片
        private const float IdeoCardPadding = 12f;
        private const float IdeoTitleDescGap = 4f;
        private const float IdeoDescButtonsGap = 10f;
        private const float IdeoButtonRowHeight = 32f;
        private const float IdeoButtonGap = 8f;

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

        // 该草稿同时是本页“文化适配等级”的唯一选择状态，往返自定义页面时保持不变。
        private readonly MechanoidMechanitorStoryConfiguration customConfigurationDraft =
            MechanoidMechanitorStoryConfiguration.CreateDefault();

        public Page_MechanoidMechanitorScenarioReady()
        {
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

            if (selectedStoryStyle.opensCustomizePage)
            {
                OpenCustomizePage();
                return;
            }

            if (!TryCommitPresetConfiguration(selectedStoryStyle))
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
            float widthForCards = area.width - StoryStyleCardGap * (count - 1);
            float cardWidth = Mathf.Clamp(
                widthForCards / count,
                StoryStyleCardMinWidth,
                StoryStyleCardMaxWidth);

            float cardHeight = GetStoryStyleCardHeight();
            cardHeight = Mathf.Min(cardHeight, area.height);

            float totalWidth = cardWidth * count + StoryStyleCardGap * (count - 1);
            float curX = area.x + Mathf.Max(0f, (area.width - totalWidth) / 2f);
            float curY = area.y + Mathf.Max(0f, (area.height - cardHeight) / 2f);

            for (int i = 0; i < count; i++)
            {
                Rect cardRect = new Rect(curX, curY, cardWidth, cardHeight);
                DrawStoryStyle(cardRect, styles[i]);
                curX += cardWidth + StoryStyleCardGap;
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
                Widgets.Label(
                    new Rect(innerX, titleY, innerWidth, Text.LineHeight),
                    style.LabelCap);

                float iconY = titleY + Text.LineHeight + StoryStyleTitleIconGap;
                Rect iconRect = new Rect(
                    rect.x + (rect.width - StoryStyleIconSize) / 2f,
                    iconY,
                    StoryStyleIconSize,
                    StoryStyleIconSize);
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

                float descY = iconY + StoryStyleIconSize + StoryStyleIconDescGap;
                float descHeight = rect.yMax - StoryStyleCardPadding - descY;
                if (descHeight > 0f && !style.description.NullOrEmpty())
                {
                    Text.WordWrap = true;
                    Text.Anchor = TextAnchor.UpperCenter;
                    GUI.color = CardDescriptionColor;
                    Widgets.Label(
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

            string tooltip =
                style.LabelCap.Colorize(ColoredText.TipSectionTitleColor)
                + "\n\n"
                + style.description;
            TooltipHandler.TipRegion(rect, tooltip);

            if (Widgets.ButtonInvisible(rect))
            {
                if (selectedStoryStyle != style)
                {
                    selectedStoryStyle = style;
                    SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
                }
            }
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
