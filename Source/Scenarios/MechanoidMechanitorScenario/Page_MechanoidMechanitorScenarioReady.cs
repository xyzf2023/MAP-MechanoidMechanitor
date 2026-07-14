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

        private MechanoidMechanitorStoryStyleDef selectedStoryStyle;

        public Page_MechanoidMechanitorScenarioReady()
        {
            selectedStoryStyle = MechanoidMechanitorStoryStyleDefOf.MAP_StoryStyle_Classic;
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

        protected override void DoNext()
        {
            if (selectedStoryStyle == null)
            {
                Log.Error(
                    "[MAP-机械族机械师] 无法保存剧情风格：当前页面未选择任何剧情风格。");
                Messages.Message(
                    "MAP_MechanoidMechanitor.Scenario.ReadyPage.SaveFailed".Translate(),
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            if (Current.Game == null)
            {
                Log.Error(
                    "[MAP-机械族机械师] 无法保存剧情风格：当前没有有效的 Game 实例。");
                Messages.Message(
                    "MAP_MechanoidMechanitor.Scenario.ReadyPage.SaveFailed".Translate(),
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
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
                return;
            }

            component.SetStoryStyleForNewGame(selectedStoryStyle);
            base.DoNext();
        }

        private void DrawStoryStyleCards(Rect area)
        {
            List<MechanoidMechanitorStoryStyleDef> styles = DefDatabase<MechanoidMechanitorStoryStyleDef>
                .AllDefsListForReading
                .OrderBy(def => def.displayOrder)
                .ToList();
            if (styles.Count == 0)
            {
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

            if (style.iconThingDef != null)
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
            }
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
