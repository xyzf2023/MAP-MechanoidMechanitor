using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechanoidMechanitorStoryComponentWorker_PurgeDirective
        : MechanoidMechanitorStoryComponentWorker
    {
        private static readonly bool[] BooleanOptions = { false, true };

        public override bool DrawInRightColumn => true;

        public override bool ShouldShow(MechanoidMechanitorStoryConfigurationContext context)
        {
            return true;
        }

        public override bool CanInteract(MechanoidMechanitorStoryConfigurationContext context)
        {
            return context.HasMechHive
                && context.Configuration.mechHiveRelationMode
                    == MechanoidMechanitorMechHiveRelationMode.Ally;
        }

        public override string? GetDisabledReason(
            MechanoidMechanitorStoryConfigurationContext context)
        {
            if (CanInteract(context))
            {
                return null;
            }

            if (!context.HasMechHive)
            {
                return "MAP_MechanoidMechanitor.PurgeDirective.Scenario.NoMechHiveDisabledReason"
                    .Translate();
            }

            return "MAP_MechanoidMechanitor.PurgeDirective.Scenario.DisabledReason".Translate();
        }

        public override string? GetSummaryValue(
            MechanoidMechanitorStoryConfiguration configuration)
        {
            return LabelForBoolean(configuration.purgeDirectiveEnabled);
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
            string currentLabel = configuration.purgeDirectiveEnabled
                ? MechanoidMechanitorStoryConfigurationLabels.EnabledLabel
                : MechanoidMechanitorStoryConfigurationLabels.DisabledLabel;

            DrawCardHeaderAndDropdown(
                rect,
                context,
                currentLabel,
                canInteract,
                () => OpenDropdownMenu(
                    BooleanOptions,
                    LabelForBoolean,
                    value =>
                    {
                        if (configuration.purgeDirectiveEnabled == value)
                        {
                            return;
                        }

                        configuration.purgeDirectiveEnabled = value;
                        if (value)
                        {
                            configuration.symbiosisCovenantEnabled = false;
                        }

                        NormalizeAfterChange(context);
                    }));
        }

        private static string LabelForBoolean(bool value)
        {
            return value
                ? MechanoidMechanitorStoryConfigurationLabels.EnabledLabel
                : MechanoidMechanitorStoryConfigurationLabels.DisabledLabel;
        }
    }
}
