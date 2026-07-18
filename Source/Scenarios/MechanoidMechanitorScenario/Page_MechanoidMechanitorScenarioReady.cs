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
        private const float IdeoBoxMargin = 5f;
        private const float IdeoBoxWidthMin = 110f;
        private const float StoryStyleIconSize = 50f;
        private const float StoryStyleCardGap = 20f;
        private const float DescriptionGap = 10f;

        private MechanoidMechanitorStoryStyleDef? selectedStoryStyle;
        private bool loggedNoStylesAvailable;
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

            float cardsAreaTop = mainRect.y + descriptionHeight + DescriptionGap;
            float cardsAreaHeight = mainRect.yMax - cardsAreaTop;
            DrawStoryStyleCards(
                new Rect(mainRect.x, cardsAreaTop, mainRect.width, cardsAreaHeight));

            DoBottomButtons(
                inRect,
                nextLabel: "Play".Translate());
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

            MechanoidMechanitorStoryConfiguration snapshot =
                storyStyle.CreateConfigurationSnapshot();
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

            float totalWidth = 0f;
            for (int i = 0; i < styles.Count; i++)
            {
                totalWidth += RectForStoryStyle(styles[i]).width;
                if (i < styles.Count - 1)
                {
                    totalWidth += StoryStyleCardGap;
                }
            }

            float maxCardHeight = 0f;
            for (int i = 0; i < styles.Count; i++)
            {
                maxCardHeight = Mathf.Max(maxCardHeight, RectForStoryStyle(styles[i]).height);
            }

            float curX = area.x + (area.width - totalWidth) / 2f;
            float curY = area.y + Mathf.Max(0f, (area.height - maxCardHeight) / 2f);

            for (int i = 0; i < styles.Count; i++)
            {
                MechanoidMechanitorStoryStyleDef style = styles[i];
                Rect cardRect = RectForStoryStyle(style);
                cardRect.x = curX;
                cardRect.y = curY;
                DrawStoryStyle(cardRect, style);
                curX += cardRect.width + StoryStyleCardGap;
            }
        }

        private void DrawStoryStyle(Rect rect, MechanoidMechanitorStoryStyleDef style)
        {
            bool selected = selectedStoryStyle == style;
            Widgets.DrawOptionBackground(rect, selected);

            TextAnchor previousAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(
                new Rect(
                    rect.x + IdeoBoxMargin,
                    rect.y + 2f,
                    rect.width - IdeoBoxMargin * 2f,
                    Text.LineHeight),
                style.LabelCap);
            Text.Anchor = previousAnchor;

            Texture2D? customIcon = style.IconTexture;
            if (customIcon != null)
            {
                Rect iconRect = new Rect(
                    rect.x + (rect.width - StoryStyleIconSize) / 2f,
                    rect.y + Text.LineHeight + IdeoBoxMargin,
                    StoryStyleIconSize,
                    StoryStyleIconSize);
                Widgets.DrawTextureFitted(iconRect, customIcon, 1f);
            }
            else if (style.iconThingDef != null)
            {
                Rect iconRect = new Rect(
                    rect.x + (rect.width - StoryStyleIconSize) / 2f,
                    rect.y + Text.LineHeight + IdeoBoxMargin,
                    StoryStyleIconSize,
                    StoryStyleIconSize);
                Widgets.ThingIcon(iconRect, style.iconThingDef);
            }

            string tooltip =
                style.LabelCap.Colorize(ColoredText.TipSectionTitleColor)
                + "\n\n"
                + style.description;
            TooltipHandler.TipRegion(rect, tooltip);

            if (Widgets.ButtonInvisible(rect))
            {
                selectedStoryStyle = style;
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
                if (style.opensCustomizePage)
                {
                    OpenCustomizePage();
                }
            }
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

        private static Rect RectForStoryStyle(MechanoidMechanitorStoryStyleDef style)
        {
            float labelWidth = Text.CalcSize(style.LabelCap).x + IdeoBoxMargin + 2f;
            float iconWidth = StoryStyleIconSize + IdeoBoxMargin;
            return new Rect
            {
                width = Mathf.Max(Mathf.Max(labelWidth, iconWidth) + IdeoBoxMargin, IdeoBoxWidthMin),
                height = Text.LineHeight + StoryStyleIconSize + IdeoBoxMargin * 2f
            };
        }
    }
}
