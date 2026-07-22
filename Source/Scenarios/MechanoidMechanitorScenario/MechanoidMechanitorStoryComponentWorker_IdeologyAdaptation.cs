using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechanoidMechanitorStoryComponentWorker_IdeologyAdaptation
        : MechanoidMechanitorStoryComponentWorker
    {
        private static readonly MechanoidMechanitorIdeologyAdaptationLevel[] Options =
        {
            MechanoidMechanitorIdeologyAdaptationLevel.Disabled,
            MechanoidMechanitorIdeologyAdaptationLevel.Basic,
            MechanoidMechanitorIdeologyAdaptationLevel.Partial,
            MechanoidMechanitorIdeologyAdaptationLevel.Full
        };

        public override bool DrawInRightColumn => true;

        public override bool ShouldShow(MechanoidMechanitorStoryConfigurationContext context)
        {
            return ModsConfig.IdeologyActive;
        }

        protected override TaggedString GetDescription()
        {
            return "MAP_MechanoidMechanitor.Story.Component.IdeologyAdaptation.Description"
                .Translate();
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
            string currentLabel = MechanoidMechanitorStoryConfigurationLabels.LabelFor(
                configuration.ideologyAdaptationLevel);

            DrawCardHeaderAndDropdown(
                rect,
                context,
                currentLabel,
                enabled: true,
                () => OpenDropdownMenu(
                    Options,
                    MechanoidMechanitorStoryConfigurationLabels.LabelFor,
                    value =>
                    {
                        if (configuration.ideologyAdaptationLevel == value)
                        {
                            return;
                        }

                        configuration.ideologyAdaptationLevel = value;
                        NormalizeAfterChange(context);
                    }));
        }
    }
}
