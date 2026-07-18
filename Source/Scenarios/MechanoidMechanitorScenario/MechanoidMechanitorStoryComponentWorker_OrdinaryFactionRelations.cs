using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechanoidMechanitorStoryComponentWorker_OrdinaryFactionRelations
        : MechanoidMechanitorStoryComponentWorker
    {
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
            return context.HasOrdinaryFactions;
        }

        protected override TaggedString GetDescription()
        {
            return "MAP_MechanoidMechanitor.Story.Component.OrdinaryFactionRelations.Description"
                .Translate();
        }

        public override float GetHeight(
            MechanoidMechanitorStoryConfigurationContext context,
            float width)
        {
            float height = MeasureDropdownSectionHeight(width, includeSeparator: false);
            if (context.Configuration.ordinaryFactionRelationsMode
                == MechanoidMechanitorOrdinaryFactionRelationsMode.Custom)
            {
                height += SectionGap;
                height += context.OrdinaryFactions.Count * FactionRowHeight;
            }

            height += SeparatorTopGap + SeparatorBottomGap;
            return height;
        }

        public override void Draw(
            Rect rect,
            MechanoidMechanitorStoryConfigurationContext context)
        {
            MechanoidMechanitorStoryConfiguration configuration = context.Configuration;
            bool drawFactionListThisFrame = configuration.ordinaryFactionRelationsMode
                == MechanoidMechanitorOrdinaryFactionRelationsMode.Custom;

            float y = DrawDropdownSection(
                rect,
                context,
                MechanoidMechanitorStoryConfigurationLabels.LabelFor(
                    configuration.ordinaryFactionRelationsMode),
                enabled: true,
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
                },
                includeSeparator: false);

            if (drawFactionListThisFrame)
            {
                y += SectionGap;
                float listWidth = Mathf.Max(0f, rect.width - FactionListIndent);
                for (int i = 0; i < context.OrdinaryFactions.Count; i++)
                {
                    Faction faction = context.OrdinaryFactions[i];
                    Rect rowRect = new Rect(
                        rect.x + FactionListIndent,
                        y,
                        listWidth,
                        FactionRowHeight);
                    DrawFactionRow(rowRect, faction, configuration, context);
                    y += FactionRowHeight;
                }
            }

            DrawSectionSeparator(rect, ref y);
        }

        private void DrawFactionRow(
            Rect rowRect,
            Faction faction,
            MechanoidMechanitorStoryConfiguration configuration,
            MechanoidMechanitorStoryConfigurationContext context)
        {
            float labelWidth = Mathf.Max(0f, rowRect.width - DropdownButtonWidth - TitleButtonGap);
            Rect labelRect = new Rect(rowRect.x, rowRect.y, labelWidth, rowRect.height);
            Rect buttonRect = new Rect(
                rowRect.xMax - DropdownButtonWidth,
                rowRect.y + 1f,
                DropdownButtonWidth,
                rowRect.height - 2f);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            bool previousWordWrap = Text.WordWrap;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.WordWrap = false;
            string factionName = faction.Name;
            bool needsNameTip = Text.CalcSize(factionName).x > labelRect.width;
            Widgets.Label(labelRect, factionName.Truncate(labelRect.width));
            Text.WordWrap = previousWordWrap;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;

            if (needsNameTip)
            {
                TooltipHandler.TipRegion(labelRect, factionName);
            }

            MechanoidMechanitorFactionRelationOption currentOption =
                configuration.GetRelationOptionFor(faction);
            DrawDropdownButton(
                buttonRect,
                MechanoidMechanitorStoryConfigurationLabels.LabelFor(currentOption),
                enabled: true,
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
