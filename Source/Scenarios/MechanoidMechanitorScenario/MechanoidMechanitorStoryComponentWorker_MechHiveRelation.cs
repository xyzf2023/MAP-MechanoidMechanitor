using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechanoidMechanitorStoryComponentWorker_MechHiveRelation
        : MechanoidMechanitorStoryComponentWorker
    {
        private static readonly MechanoidMechanitorMechHiveRelationMode[] Modes =
        {
            MechanoidMechanitorMechHiveRelationMode.Default,
            MechanoidMechanitorMechHiveRelationMode.Neutral,
            MechanoidMechanitorMechHiveRelationMode.PermanentNeutral,
            MechanoidMechanitorMechHiveRelationMode.Ally
        };

        public override bool ShouldShow(MechanoidMechanitorStoryConfigurationContext context)
        {
            return true;
        }

        public override bool CanInteract(MechanoidMechanitorStoryConfigurationContext context)
        {
            return context.HasMechHive && !context.HasPursuingMechanoidsScenarioPart;
        }

        public override string? GetDisabledReason(
            MechanoidMechanitorStoryConfigurationContext context)
        {
            if (!context.HasMechHive)
            {
                return "MAP_MechanoidMechanitor.Story.NoMechHive".Translate();
            }

            if (context.HasPursuingMechanoidsScenarioPart)
            {
                return "MAP_MechanoidMechanitor.Story.MechHiveRelation.PursuingMechanoidsDisabledReason"
                    .Translate();
            }

            return null;
        }

        public override string? GetSummaryValue(
            MechanoidMechanitorStoryConfiguration configuration)
        {
            return MechanoidMechanitorStoryConfigurationLabels.LabelFor(
                configuration.mechHiveRelationMode);
        }

        public override float GetHeight(
            MechanoidMechanitorStoryConfigurationContext context,
            float width)
        {
            return MeasureCardHeight(context, width);
        }

        public override void Draw(
            Rect rect,
            MechanoidMechanitorStoryConfigurationContext context)
        {
            MechanoidMechanitorStoryConfiguration configuration = context.Configuration;
            bool canInteract = CanInteract(context);
            DrawCardHeaderAndDropdown(
                rect,
                context,
                MechanoidMechanitorStoryConfigurationLabels.LabelFor(
                    configuration.mechHiveRelationMode),
                canInteract,
                () => OpenDropdownMenu(
                    Modes,
                    MechanoidMechanitorStoryConfigurationLabels.LabelFor,
                    mode =>
                    {
                        if (configuration.mechHiveRelationMode == mode)
                        {
                            return;
                        }

                        configuration.mechHiveRelationMode = mode;
                        NormalizeAfterChange(context);
                    }));
        }
    }
}
