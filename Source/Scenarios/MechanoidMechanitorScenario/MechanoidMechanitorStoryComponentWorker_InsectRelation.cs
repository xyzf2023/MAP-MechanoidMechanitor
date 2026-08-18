using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechanoidMechanitorStoryComponentWorker_InsectRelation
        : MechanoidMechanitorStoryComponentWorker
    {
        private static readonly MechanoidMechanitorInsectRelationMode[] Modes =
        {
            MechanoidMechanitorInsectRelationMode.Default,
            MechanoidMechanitorInsectRelationMode.PermanentNeutral,
            MechanoidMechanitorInsectRelationMode.Ally,
            MechanoidMechanitorInsectRelationMode.Pursuit
        };

        public override bool ShouldShow(
            MechanoidMechanitorStoryConfigurationContext context)
        {
            return true;
        }

        public override bool CanInteract(
            MechanoidMechanitorStoryConfigurationContext context)
        {
            return context.HasInsectFaction;
        }

        public override string? GetDisabledReason(
            MechanoidMechanitorStoryConfigurationContext context)
        {
            if (!context.HasInsectFaction)
            {
                return "MAP_MechanoidMechanitor.Story.NoInsectFaction".Translate();
            }

            return null;
        }

        public override string? GetSummaryValue(
            MechanoidMechanitorStoryConfiguration configuration)
        {
            return MechanoidMechanitorStoryConfigurationLabels.LabelFor(
                configuration.insectRelationMode);
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
            MechanoidMechanitorStoryConfiguration configuration =
                context.Configuration;

            bool canInteract = CanInteract(context);

            DrawCardHeaderAndDropdown(
                rect,
                context,
                MechanoidMechanitorStoryConfigurationLabels.LabelFor(
                    configuration.insectRelationMode),
                canInteract,
                () => OpenDropdownMenu(
                    Modes,
                    MechanoidMechanitorStoryConfigurationLabels.LabelFor,
                    mode =>
                    {
                        if (configuration.insectRelationMode == mode)
                        {
                            return;
                        }

                        configuration.insectRelationMode = mode;
                        NormalizeAfterChange(context);
                    }));
        }
    }
}
