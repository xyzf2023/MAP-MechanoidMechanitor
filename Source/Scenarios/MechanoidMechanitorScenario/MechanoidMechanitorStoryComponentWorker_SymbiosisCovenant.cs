using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechanoidMechanitorStoryComponentWorker_SymbiosisCovenant
        : MechanoidMechanitorStoryComponentWorker
    {
        private static readonly bool[] BooleanOptions = { false, true };

        public override bool DrawInRightColumn => true;

        public override bool ShouldShow(MechanoidMechanitorStoryConfigurationContext context)
        {
            return context.HasOrdinaryFactions;
        }

        public override bool CanInteract(MechanoidMechanitorStoryConfigurationContext context)
        {
            return context.Configuration.IsSymbiosisCovenantAvailable(context);
        }

        public override string? GetDisabledReason(
            MechanoidMechanitorStoryConfigurationContext context)
        {
            if (CanInteract(context))
            {
                return null;
            }

            return "MAP_MechanoidMechanitor.Symbiosis.Scenario.DisabledReason".Translate();
        }

        public override string? GetSummaryValue(
            MechanoidMechanitorStoryConfiguration configuration)
        {
            return LabelForBoolean(configuration.symbiosisCovenantEnabled);
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
            string currentLabel = configuration.symbiosisCovenantEnabled
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
                        if (configuration.symbiosisCovenantEnabled == value)
                        {
                            return;
                        }

                        configuration.symbiosisCovenantEnabled = value;
                        if (value)
                        {
                            configuration.purgeDirectiveEnabled = false;
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
