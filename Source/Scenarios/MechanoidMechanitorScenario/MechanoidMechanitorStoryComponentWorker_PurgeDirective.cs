using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechanoidMechanitorStoryComponentWorker_PurgeDirective
        : MechanoidMechanitorStoryComponentWorker
    {
        private static readonly bool[] BooleanOptions = { false, true };

        public override bool ShouldShow(MechanoidMechanitorStoryConfigurationContext context)
        {
            return context.HasMechHive;
        }

        public override bool CanInteract(MechanoidMechanitorStoryConfigurationContext context)
        {
            return context.Configuration.mechHiveRelationMode
                == MechanoidMechanitorMechHiveRelationMode.Ally;
        }

        public override string? GetDisabledReason(
            MechanoidMechanitorStoryConfigurationContext context)
        {
            if (CanInteract(context))
            {
                return null;
            }

            return "MAP_MechanoidMechanitor.Story.PurgeDirective.DisabledReason".Translate();
        }

        protected override TaggedString GetDescription()
        {
            return "MAP_MechanoidMechanitor.Story.Component.PurgeDirective.Description"
                .Translate();
        }

        public override float GetHeight(
            MechanoidMechanitorStoryConfigurationContext context,
            float width)
        {
            return MeasureDropdownSectionHeight(width);
        }

        public override void Draw(
            Rect rect,
            MechanoidMechanitorStoryConfigurationContext context)
        {
            MechanoidMechanitorStoryConfiguration configuration = context.Configuration;
            bool canInteract = CanInteract(context);
            string currentLabel = configuration.purgeDirectiveEnabled
                ? MechanoidMechanitorStoryConfigurationLabels.EnabledLabel
                : MechanoidMechanitorStoryConfigurationLabels.DisabledLabel;

            DrawDropdownSection(
                rect,
                context,
                currentLabel,
                canInteract,
                BooleanOptions,
                LabelForBoolean,
                value =>
                {
                    if (configuration.purgeDirectiveEnabled == value)
                    {
                        return;
                    }

                    configuration.purgeDirectiveEnabled = value;
                    NormalizeAfterChange(context);
                });
        }

        private static string LabelForBoolean(bool value)
        {
            return value
                ? MechanoidMechanitorStoryConfigurationLabels.EnabledLabel
                : MechanoidMechanitorStoryConfigurationLabels.DisabledLabel;
        }
    }
}
