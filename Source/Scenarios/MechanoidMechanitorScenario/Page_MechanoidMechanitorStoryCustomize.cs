using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class Page_MechanoidMechanitorStoryCustomize : Page
    {
        private readonly MechanoidMechanitorStoryConfiguration configurationDraft;

        private Vector2 scrollPosition;

        private float viewHeight;

        public Page_MechanoidMechanitorStoryCustomize(
            MechanoidMechanitorStoryConfiguration configurationDraft)
        {
            this.configurationDraft = configurationDraft;
            PrepareDraft();
        }

        public override string PageTitle =>
            "MAP_MechanoidMechanitor.Scenario.CustomizePage.Title".Translate();

        public override void PreOpen()
        {
            base.PreOpen();
            PrepareDraft();
        }

        public override void DoWindowContents(Rect inRect)
        {
            DrawPageTitle(inRect);

            Rect mainRect = GetMainRect(inRect);
            TaggedString description =
                "MAP_MechanoidMechanitor.Scenario.CustomizePage.Text".Translate();
            float descriptionHeight = Text.CalcHeight(description, mainRect.width);
            Widgets.Label(
                new Rect(mainRect.x, mainRect.y, mainRect.width, descriptionHeight),
                description);

            Rect scrollOutRect = new Rect(
                mainRect.x,
                mainRect.y + descriptionHeight + 10f,
                mainRect.width,
                mainRect.height - descriptionHeight - 10f);

            MechanoidMechanitorStoryConfigurationContext context =
                MechanoidMechanitorStoryConfigurationContext.Create(configurationDraft);
            List<MechanoidMechanitorStoryComponentDef> components =
                GetVisibleComponentsSorted(context);

            float width = scrollOutRect.width - 16f;
            float contentHeight = 0f;
            for (int i = 0; i < components.Count; i++)
            {
                contentHeight += components[i].Worker.GetHeight(context, width);
            }

            viewHeight = Mathf.Max(contentHeight, scrollOutRect.height);
            Rect scrollViewRect = new Rect(0f, 0f, width, viewHeight);
            Widgets.BeginScrollView(scrollOutRect, ref scrollPosition, scrollViewRect);

            float y = 0f;
            for (int i = 0; i < components.Count; i++)
            {
                MechanoidMechanitorStoryComponentWorker worker = components[i].Worker;
                float height = worker.GetHeight(context, width);
                worker.Draw(new Rect(0f, y, width, height), context);
                y += height;
            }

            Widgets.EndScrollView();

            DoBottomButtons(
                inRect,
                nextLabel:
                    "MAP_MechanoidMechanitor.Scenario.CustomizePage.StartGame".Translate());
        }

        protected override bool CanDoNext()
        {
            return base.CanDoNext();
        }

        protected override void DoNext()
        {
            if (!TryCommitConfigurationAndStart())
            {
                return;
            }

            base.DoNext();
        }

        private void PrepareDraft()
        {
            MechanoidMechanitorStoryConfigurationContext context =
                MechanoidMechanitorStoryConfigurationContext.Create(configurationDraft);
            configurationDraft.SyncOrdinaryFactionEntries(context);
            configurationDraft.Normalize(context);
        }

        private bool TryCommitConfigurationAndStart()
        {
            MechanoidMechanitorStoryConfigurationContext context =
                MechanoidMechanitorStoryConfigurationContext.Create(configurationDraft);
            configurationDraft.SyncOrdinaryFactionEntries(context);
            configurationDraft.Normalize(context);

            if (Current.Game == null)
            {
                Log.Error(
                    "[MAP-机械族机械师] 无法保存自定义剧情配置：当前没有有效的 Game 实例。");
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
                    "[MAP-机械族机械师] 无法保存自定义剧情配置：缺少 GameComponent_MechanoidMechanitorStoryState 组件。");
                Messages.Message(
                    "MAP_MechanoidMechanitor.Scenario.ReadyPage.SaveFailed".Translate(),
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return false;
            }

            MechanoidMechanitorStoryStyleDef customStyle =
                MechanoidMechanitorStoryStyleDefOf.MAP_StoryStyle_Custom;
            if (customStyle == null)
            {
                Log.Error(
                    "[MAP-机械族机械师] 无法保存自定义剧情配置：缺少 MAP_StoryStyle_Custom。");
                Messages.Message(
                    "MAP_MechanoidMechanitor.Scenario.ReadyPage.SaveFailed".Translate(),
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return false;
            }

            component.SetStoryStyleForNewGame(customStyle, configurationDraft);
            return true;
        }

        private static List<MechanoidMechanitorStoryComponentDef> GetVisibleComponentsSorted(
            MechanoidMechanitorStoryConfigurationContext context)
        {
            return DefDatabase<MechanoidMechanitorStoryComponentDef>
                .AllDefsListForReading
                .OrderBy(def => def.displayOrder)
                .ThenBy(def => def.defName, StringComparer.Ordinal)
                .Where(def => def.Worker.ShouldShow(context))
                .ToList();
        }
    }
}
