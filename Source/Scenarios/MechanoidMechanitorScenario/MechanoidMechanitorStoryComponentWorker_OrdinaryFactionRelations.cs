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
            float height = MeasureHeaderAndDescription(width);
            height += GlobalModes.Length * OptionRowHeight;
            if (context.Configuration.ordinaryFactionRelationsMode
                == MechanoidMechanitorOrdinaryFactionRelationsMode.Custom)
            {
                height += SectionGap;
                height += context.OrdinaryFactions.Count * FactionRowHeight;
            }

            height += SectionGap;
            return height;
        }

        public override void Draw(
            Rect rect,
            MechanoidMechanitorStoryConfigurationContext context)
        {
            MechanoidMechanitorStoryConfiguration configuration = context.Configuration;
            bool drawFactionListThisFrame = configuration.ordinaryFactionRelationsMode
                == MechanoidMechanitorOrdinaryFactionRelationsMode.Custom;
            float width = rect.width;
            float y = DrawHeaderAndDescription(rect, width);

            for (int i = 0; i < GlobalModes.Length; i++)
            {
                MechanoidMechanitorOrdinaryFactionRelationsMode mode = GlobalModes[i];
                Rect optionRect = new Rect(rect.x, y, width, OptionRowHeight);
                if (DrawRadioOption(
                    optionRect,
                    MechanoidMechanitorStoryConfigurationLabels.LabelFor(mode),
                    configuration.ordinaryFactionRelationsMode == mode,
                    enabled: true,
                    out _))
                {
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
                }

                y += OptionRowHeight;
            }

            if (!drawFactionListThisFrame)
            {
                return;
            }

            y += SectionGap;
            for (int i = 0; i < context.OrdinaryFactions.Count; i++)
            {
                Faction faction = context.OrdinaryFactions[i];
                Rect rowRect = new Rect(rect.x, y, width, FactionRowHeight);
                Widgets.Label(
                    new Rect(
                        rowRect.x,
                        rowRect.y,
                        rowRect.width - DropdownButtonWidth - 8f,
                        rowRect.height),
                    faction.Name);

                MechanoidMechanitorFactionRelationOption currentOption =
                    configuration.GetRelationOptionFor(faction);
                Rect buttonRect = new Rect(
                    rowRect.xMax - DropdownButtonWidth,
                    rowRect.y + 1f,
                    DropdownButtonWidth,
                    rowRect.height - 2f);

                DrawDropdownButton(
                    buttonRect,
                    MechanoidMechanitorStoryConfigurationLabels.LabelFor(currentOption),
                    enabled: true,
                    FactionOptions,
                    MechanoidMechanitorStoryConfigurationLabels.LabelFor,
                    option =>
                    {
                        configuration.SetRelationOptionFor(faction, option);
                        NormalizeAfterChange(context);
                    });

                y += FactionRowHeight;
            }
        }
    }
}
