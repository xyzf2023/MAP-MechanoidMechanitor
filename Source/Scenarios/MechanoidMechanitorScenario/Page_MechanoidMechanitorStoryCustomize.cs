using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class Page_MechanoidMechanitorStoryCustomize : Page
    {
        private const float TwoColumnMinWidth = 820f;
        private const float ColumnGap = 16f;
        private const float LeftColumnRatio = 0.62f;
        private const float TitleToDescriptionGap = 6f;
        private const float DescriptionBottomGap = 8f;
        private const float LoadPresetButtonWidth = 170f;
        private const float LoadPresetButtonHeight = 29f;
        private const float LoadPresetButtonToAccentGap = 8f;
        private const float AccentLineWidth = 110f;
        private const float AccentLineHeight = 2f;
        private const float AccentToContentGap = 14f;
        private const float HorizontalPadding = 8f;

        private static readonly Color DescriptionColor = new Color(0.72f, 0.72f, 0.72f, 1f);
        private static readonly Color AccentLineColor = new Color(0.62f, 0.48f, 0.30f, 0.75f);
        private static readonly Color LoadPresetButtonBgColor = new Color(0.18f, 0.16f, 0.14f, 1f);
        private static readonly Color LoadPresetButtonBgHoverColor = new Color(0.24f, 0.21f, 0.17f, 1f);
        private static readonly Color LoadPresetButtonOutlineColor = new Color(0.58f, 0.46f, 0.30f, 0.70f);
        private static readonly Color LoadPresetButtonOutlineHoverColor =
            new Color(0.70f, 0.56f, 0.36f, 0.85f);

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

            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;
            bool previousWordWrap = Text.WordWrap;
            float descriptionHeight;
            float descriptionY = mainRect.y + TitleToDescriptionGap;
            try
            {
                Text.Font = GameFont.Small;
                Text.WordWrap = true;
                descriptionHeight = Text.CalcHeight(description, mainRect.width);
                GUI.color = DescriptionColor;
                Widgets.Label(
                    new Rect(mainRect.x, descriptionY, mainRect.width, descriptionHeight),
                    description);
            }
            finally
            {
                Text.Font = previousFont;
                GUI.color = previousColor;
                Text.WordWrap = previousWordWrap;
            }

            float loadPresetButtonY = descriptionY + descriptionHeight + DescriptionBottomGap;
            DrawLoadPresetButton(
                new Rect(
                    mainRect.xMax - LoadPresetButtonWidth,
                    loadPresetButtonY,
                    LoadPresetButtonWidth,
                    LoadPresetButtonHeight));

            float accentY =
                loadPresetButtonY + LoadPresetButtonHeight + LoadPresetButtonToAccentGap;
            Widgets.DrawBoxSolid(
                new Rect(mainRect.x, accentY, AccentLineWidth, AccentLineHeight),
                AccentLineColor);

            Rect scrollOutRect = new Rect(
                mainRect.x,
                accentY + AccentLineHeight + AccentToContentGap,
                mainRect.width,
                mainRect.height - (accentY - mainRect.y) - AccentLineHeight - AccentToContentGap);

            MechanoidMechanitorStoryConfigurationContext context =
                MechanoidMechanitorStoryConfigurationContext.Create(configurationDraft);
            List<MechanoidMechanitorStoryComponentDef> components =
                GetVisibleComponentsSorted(context);

            float availableWidth = scrollOutRect.width - 16f - HorizontalPadding * 2f;
            bool useTwoColumns = availableWidth >= TwoColumnMinWidth;

            float contentHeight;
            if (useTwoColumns)
            {
                float leftWidth = availableWidth * LeftColumnRatio;
                float rightWidth = availableWidth - leftWidth - ColumnGap;
                float leftHeight = 0f;
                float rightHeight = 0f;
                for (int i = 0; i < components.Count; i++)
                {
                    MechanoidMechanitorStoryComponentWorker worker = components[i].Worker;
                    if (worker.DrawInRightColumn)
                    {
                        if (rightHeight > 0f)
                        {
                            rightHeight += MechanoidMechanitorStoryComponentWorker.CardGap;
                        }

                        rightHeight += worker.GetHeight(context, rightWidth);
                    }
                    else
                    {
                        if (leftHeight > 0f)
                        {
                            leftHeight += MechanoidMechanitorStoryComponentWorker.CardGap;
                        }

                        leftHeight += worker.GetHeight(context, leftWidth);
                    }
                }

                contentHeight = Mathf.Max(leftHeight, rightHeight);
            }
            else
            {
                contentHeight = 0f;
                for (int i = 0; i < components.Count; i++)
                {
                    if (i > 0)
                    {
                        contentHeight += MechanoidMechanitorStoryComponentWorker.CardGap;
                    }

                    contentHeight += components[i].Worker.GetHeight(context, availableWidth);
                }
            }

            viewHeight = Mathf.Max(contentHeight, scrollOutRect.height);
            Rect scrollViewRect = new Rect(
                0f,
                0f,
                availableWidth + HorizontalPadding * 2f,
                viewHeight);
            Widgets.BeginScrollView(scrollOutRect, ref scrollPosition, scrollViewRect);

            if (useTwoColumns)
            {
                float leftWidth = availableWidth * LeftColumnRatio;
                float rightWidth = availableWidth - leftWidth - ColumnGap;
                float leftX = HorizontalPadding;
                float rightX = HorizontalPadding + leftWidth + ColumnGap;
                float leftY = 0f;
                float rightY = 0f;

                for (int i = 0; i < components.Count; i++)
                {
                    MechanoidMechanitorStoryComponentWorker worker = components[i].Worker;
                    if (worker.DrawInRightColumn)
                    {
                        if (rightY > 0f)
                        {
                            rightY += MechanoidMechanitorStoryComponentWorker.CardGap;
                        }

                        float height = worker.GetHeight(context, rightWidth);
                        worker.Draw(new Rect(rightX, rightY, rightWidth, height), context);
                        rightY += height;
                    }
                    else
                    {
                        if (leftY > 0f)
                        {
                            leftY += MechanoidMechanitorStoryComponentWorker.CardGap;
                        }

                        float height = worker.GetHeight(context, leftWidth);
                        worker.Draw(new Rect(leftX, leftY, leftWidth, height), context);
                        leftY += height;
                    }
                }
            }
            else
            {
                float y = 0f;
                for (int i = 0; i < components.Count; i++)
                {
                    if (i > 0)
                    {
                        y += MechanoidMechanitorStoryComponentWorker.CardGap;
                    }

                    MechanoidMechanitorStoryComponentWorker worker = components[i].Worker;
                    float height = worker.GetHeight(context, availableWidth);
                    worker.Draw(new Rect(HorizontalPadding, y, availableWidth, height), context);
                    y += height;
                }
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

        private void DrawLoadPresetButton(Rect rect)
        {
            Color previousColor = GUI.color;
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            bool previousWordWrap = Text.WordWrap;
            try
            {
                Color bg = LoadPresetButtonBgColor;
                Color outline = LoadPresetButtonOutlineColor;
                if (Mouse.IsOver(rect))
                {
                    bg = LoadPresetButtonBgHoverColor;
                    outline = LoadPresetButtonOutlineHoverColor;
                }

                Widgets.DrawBoxSolidWithOutline(rect, bg, outline);

                Text.Font = GameFont.Small;
                Text.WordWrap = false;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = Color.white;
                Widgets.Label(
                    rect,
                    "MAP_MechanoidMechanitor.Scenario.CustomizePage.LoadPreset".Translate());

                if (Widgets.ButtonInvisible(rect, doMouseoverSound: true))
                {
                    OpenPresetFloatMenu();
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

        private void OpenPresetFloatMenu()
        {
            List<MechanoidMechanitorStoryStyleDef> presets = GetLoadablePresetsSorted();
            if (presets.Count == 0)
            {
                return;
            }

            List<FloatMenuOption> options = new List<FloatMenuOption>(presets.Count);
            for (int i = 0; i < presets.Count; i++)
            {
                MechanoidMechanitorStoryStyleDef preset = presets[i];
                options.Add(
                    new FloatMenuOption(
                        preset.LabelCap,
                        () => ApplyPresetToDraft(preset)));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static List<MechanoidMechanitorStoryStyleDef> GetLoadablePresetsSorted()
        {
            return DefDatabase<MechanoidMechanitorStoryStyleDef>
                .AllDefsListForReading
                .Where(def => !def.opensCustomizePage && def.presetConfiguration != null)
                .OrderBy(def => def.displayOrder)
                .ThenBy(def => def.defName, StringComparer.Ordinal)
                .ToList();
        }

        private void ApplyPresetToDraft(MechanoidMechanitorStoryStyleDef style)
        {
            MechanoidMechanitorStoryConfiguration snapshot =
                style.CreateConfigurationSnapshot();
            MechanoidMechanitorIdeologyAdaptationLevel preservedIdeologyAdaptationLevel =
                configurationDraft.ideologyAdaptationLevel;

            configurationDraft.ordinaryFactionRelationsMode =
                snapshot.ordinaryFactionRelationsMode;
            configurationDraft.factionOutpostFrequency = snapshot.factionOutpostFrequency;
            configurationDraft.hostileFactionOutpostWeight = snapshot.hostileFactionOutpostWeight;
            configurationDraft.allyFactionOutpostWeight = snapshot.allyFactionOutpostWeight;
            configurationDraft.neutralFactionOutpostWeight = snapshot.neutralFactionOutpostWeight;
            configurationDraft.factionOutpostRaidChancePercent = snapshot.factionOutpostRaidChancePercent;
            configurationDraft.factionOutpostSupportChancePercent = snapshot.factionOutpostSupportChancePercent;
            configurationDraft.mechHiveRelationMode = snapshot.mechHiveRelationMode;
            configurationDraft.mechHiveNodeFrequency = snapshot.mechHiveNodeFrequency;
            configurationDraft.mechHiveNodeRaidChancePercent = snapshot.mechHiveNodeRaidChancePercent;
            configurationDraft.mechHiveNodeSupportChancePercent = snapshot.mechHiveNodeSupportChancePercent;
            configurationDraft.purgeDirectiveEnabled = snapshot.purgeDirectiveEnabled;
            configurationDraft.symbiosisCovenantEnabled = snapshot.symbiosisCovenantEnabled;
            configurationDraft.ideologyAdaptationLevel = preservedIdeologyAdaptationLevel;

            configurationDraft.ordinaryFactionRelationSettings.Clear();

            MechanoidMechanitorStoryConfigurationContext context =
                MechanoidMechanitorStoryConfigurationContext.Create(configurationDraft);
            configurationDraft.SyncOrdinaryFactionEntries(context);
            configurationDraft.Normalize(context);

            scrollPosition = Vector2.zero;
            SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
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
