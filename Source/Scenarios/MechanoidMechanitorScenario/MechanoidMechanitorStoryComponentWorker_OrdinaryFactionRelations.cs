using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechanoidMechanitorStoryComponentWorker_OrdinaryFactionRelations
        : MechanoidMechanitorStoryComponentWorker
    {
        private const float SubPanelInset = 10f;
        private const float SubPanelTopGap = 10f;
        private const float SubPanelPaddingY = 7f;
        private const float FactionRowHeight = 35f;
        private const float FactionDropdownWidth = 135f;
        private const float FactionNameButtonGap = 8f;

        private static readonly Color SubPanelBgColor = new Color(0.11f, 0.11f, 0.11f, 1f);
        private static readonly Color SubPanelOutlineColor = new Color(0.40f, 0.34f, 0.26f, 0.40f);
        private static readonly Color FactionRowAltColor = new Color(1f, 1f, 1f, 0.03f);

        private static readonly MechanoidMechanitorOrdinaryFactionRelationsMode[] GlobalModes =
        {
            MechanoidMechanitorOrdinaryFactionRelationsMode.Default,
            MechanoidMechanitorOrdinaryFactionRelationsMode.AllHostile,
            MechanoidMechanitorOrdinaryFactionRelationsMode.AllPermanentHostile,
            MechanoidMechanitorOrdinaryFactionRelationsMode.AllPermanentNeutral,
            MechanoidMechanitorOrdinaryFactionRelationsMode.AllAlly,
            MechanoidMechanitorOrdinaryFactionRelationsMode.AllPermanentAlly,
            MechanoidMechanitorOrdinaryFactionRelationsMode.Custom
        };

        private static readonly MechanoidMechanitorFactionRelationOption[] FactionOptions =
        {
            MechanoidMechanitorFactionRelationOption.Default,
            MechanoidMechanitorFactionRelationOption.Hostile,
            MechanoidMechanitorFactionRelationOption.PermanentHostile,
            MechanoidMechanitorFactionRelationOption.PermanentNeutral,
            MechanoidMechanitorFactionRelationOption.Ally,
            MechanoidMechanitorFactionRelationOption.PermanentAlly
        };

        public override bool ShouldShow(MechanoidMechanitorStoryConfigurationContext context)
        {
            return true;
        }

        public override bool CanInteract(MechanoidMechanitorStoryConfigurationContext context)
        {
            return context.HasOrdinaryFactions;
        }

        public override string? GetDisabledReason(
            MechanoidMechanitorStoryConfigurationContext context)
        {
            if (context.HasOrdinaryFactions)
            {
                return null;
            }

            return "MAP_MechanoidMechanitor.Story.NoOrdinaryFactions".Translate();
        }

        public override string? GetSummaryValue(
            MechanoidMechanitorStoryConfiguration configuration)
        {
            return MechanoidMechanitorStoryConfigurationLabels.LabelFor(
                configuration.ordinaryFactionRelationsMode);
        }

        public override float GetHeight(
            MechanoidMechanitorStoryConfigurationContext context,
            float width)
        {
            float extra = 0f;
            if (context.Configuration.ordinaryFactionRelationsMode
                == MechanoidMechanitorOrdinaryFactionRelationsMode.Custom)
            {
                extra = MeasureFactionSubPanelHeight(context.OrdinaryFactions.Count);
            }

            return MeasureCardHeight(context, width, extra);
        }

        public override void Draw(
            Rect rect,
            MechanoidMechanitorStoryConfigurationContext context)
        {
            MechanoidMechanitorStoryConfiguration configuration = context.Configuration;
            bool drawFactionListThisFrame = configuration.ordinaryFactionRelationsMode
                == MechanoidMechanitorOrdinaryFactionRelationsMode.Custom;
            bool canInteract = CanInteract(context);

            float contentY = DrawCardHeaderAndDropdown(
                rect,
                context,
                MechanoidMechanitorStoryConfigurationLabels.LabelFor(
                    configuration.ordinaryFactionRelationsMode),
                canInteract,
                () => OpenDropdownMenu(
                    GlobalModes,
                    MechanoidMechanitorStoryConfigurationLabels.LabelFor,
                    mode =>
                    {
                        if (configuration.ordinaryFactionRelationsMode == mode)
                        {
                            return;
                        }

                        bool switchedToCustom =
                            mode == MechanoidMechanitorOrdinaryFactionRelationsMode.Custom
                            && configuration.ordinaryFactionRelationsMode
                                != MechanoidMechanitorOrdinaryFactionRelationsMode.Custom;

                        configuration.ordinaryFactionRelationsMode = mode;
                        if (switchedToCustom)
                        {
                            configuration.SyncOrdinaryFactionEntries(context);
                        }

                        NormalizeAfterChange(context);
                    }));

            if (drawFactionListThisFrame)
            {
                DrawFactionSubPanel(rect, contentY, context, configuration);
            }
        }

        private static float MeasureFactionSubPanelHeight(int factionCount)
        {
            return SubPanelTopGap
                + SubPanelPaddingY
                + factionCount * FactionRowHeight
                + SubPanelPaddingY;
        }

        private void DrawFactionSubPanel(
            Rect cardRect,
            float contentY,
            MechanoidMechanitorStoryConfigurationContext context,
            MechanoidMechanitorStoryConfiguration configuration)
        {
            Rect inner = cardRect.ContractedBy(CardPadding);
            float panelHeight = SubPanelPaddingY
                + context.OrdinaryFactions.Count * FactionRowHeight
                + SubPanelPaddingY;
            Rect panelRect = new Rect(
                inner.x + SubPanelInset,
                contentY + SubPanelTopGap,
                Mathf.Max(1f, inner.width - SubPanelInset * 2f),
                panelHeight);

            Widgets.DrawBoxSolidWithOutline(panelRect, SubPanelBgColor, SubPanelOutlineColor);

            float y = panelRect.y + SubPanelPaddingY;
            for (int i = 0; i < context.OrdinaryFactions.Count; i++)
            {
                Faction faction = context.OrdinaryFactions[i];
                Rect rowRect = new Rect(panelRect.x, y, panelRect.width, FactionRowHeight);
                DrawFactionRow(rowRect, i, faction, configuration, context);
                y += FactionRowHeight;
            }
        }

        private void DrawFactionRow(
            Rect rowRect,
            int rowIndex,
            Faction faction,
            MechanoidMechanitorStoryConfiguration configuration,
            MechanoidMechanitorStoryConfigurationContext context)
        {
            if ((rowIndex & 1) == 1)
            {
                Widgets.DrawBoxSolid(rowRect, FactionRowAltColor);
            }

            Widgets.DrawHighlightIfMouseover(rowRect);

            float dropdownWidth = Mathf.Clamp(FactionDropdownWidth, 125f, 145f);
            float labelWidth = Mathf.Max(1f, rowRect.width - dropdownWidth - FactionNameButtonGap);
            Rect labelRect = new Rect(rowRect.x + 8f, rowRect.y, labelWidth - 8f, rowRect.height);
            Rect buttonRect = new Rect(
                rowRect.xMax - dropdownWidth - 6f,
                rowRect.y + (rowRect.height - DropdownHeight) * 0.5f,
                dropdownWidth,
                DropdownHeight);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            bool previousWordWrap = Text.WordWrap;
            Color previousColor = GUI.color;
            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.WordWrap = false;
                GUI.color = Color.white;
                string factionName = faction.Name;
                bool needsNameTip = Text.CalcSize(factionName).x > labelRect.width;
                Widgets.Label(labelRect, factionName.Truncate(labelRect.width));
                if (needsNameTip)
                {
                    TooltipHandler.TipRegion(labelRect, factionName);
                }
            }
            finally
            {
                Text.Font = previousFont;
                Text.Anchor = previousAnchor;
                Text.WordWrap = previousWordWrap;
                GUI.color = previousColor;
            }

            MechanoidMechanitorFactionRelationOption currentOption =
                configuration.GetRelationOptionFor(faction);
            if (DrawFlatDropdownButton(
                buttonRect,
                MechanoidMechanitorStoryConfigurationLabels.LabelFor(currentOption),
                enabled: true))
            {
                OpenDropdownMenu(
                    FactionOptions,
                    MechanoidMechanitorStoryConfigurationLabels.LabelFor,
                    option =>
                    {
                        if (configuration.GetRelationOptionFor(faction) == option)
                        {
                            return;
                        }

                        configuration.SetRelationOptionFor(faction, option);
                        NormalizeAfterChange(context);
                    });
            }
        }
    }
}
