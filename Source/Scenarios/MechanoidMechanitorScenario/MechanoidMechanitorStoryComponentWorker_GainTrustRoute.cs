using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechanoidMechanitorStoryComponentWorker_GainTrustRoute
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
            return context.Configuration.IsGainTrustAvailable(context);
        }

        public override string? GetDisabledReason(
            MechanoidMechanitorStoryConfigurationContext context)
        {
            if (CanInteract(context))
            {
                return null;
            }

            return "MAP_MechanoidMechanitor.Story.GainTrustRoute.DisabledReason".Translate();
        }

        protected override TaggedString GetDescription()
        {
            return "MAP_MechanoidMechanitor.Story.Component.GainTrustRoute.Description"
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
            bool canInteract = CanInteract(context);
            string currentLabel = configuration.gainTrustRouteEnabled
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
                        if (configuration.gainTrustRouteEnabled == value)
                        {
                            return;
                        }

                        configuration.gainTrustRouteEnabled = value;
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
