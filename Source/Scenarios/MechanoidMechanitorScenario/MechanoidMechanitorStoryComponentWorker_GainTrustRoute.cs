using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechanoidMechanitorStoryComponentWorker_GainTrustRoute
        : MechanoidMechanitorStoryComponentWorker
    {
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
            return MeasureHeaderAndDescription(width)
                + OptionRowHeight * 2f
                + SectionGap;
        }

        public override void Draw(
            Rect rect,
            MechanoidMechanitorStoryConfigurationContext context)
        {
            MechanoidMechanitorStoryConfiguration configuration = context.Configuration;
            bool canInteract = CanInteract(context);
            float width = rect.width;
            float y = DrawHeaderAndDescription(rect, width);
            Rect optionsRect = new Rect(rect.x, y, width, OptionRowHeight * 2f);

            if (DrawRadioOption(
                new Rect(rect.x, y, width, OptionRowHeight),
                MechanoidMechanitorStoryConfigurationLabels.DisabledLabel,
                !configuration.gainTrustRouteEnabled,
                canInteract,
                out _))
            {
                configuration.gainTrustRouteEnabled = false;
                NormalizeAfterChange(context);
            }

            y += OptionRowHeight;
            if (DrawRadioOption(
                new Rect(rect.x, y, width, OptionRowHeight),
                MechanoidMechanitorStoryConfigurationLabels.EnabledLabel,
                configuration.gainTrustRouteEnabled,
                canInteract,
                out _))
            {
                configuration.gainTrustRouteEnabled = true;
                NormalizeAfterChange(context);
            }

            DrawDisabledTip(optionsRect, context);
        }
    }
}
