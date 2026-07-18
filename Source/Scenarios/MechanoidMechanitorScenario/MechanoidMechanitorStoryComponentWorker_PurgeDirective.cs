using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechanoidMechanitorStoryComponentWorker_PurgeDirective
        : MechanoidMechanitorStoryComponentWorker
    {
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
                !configuration.purgeDirectiveEnabled,
                canInteract,
                out _))
            {
                configuration.purgeDirectiveEnabled = false;
                NormalizeAfterChange(context);
            }

            y += OptionRowHeight;
            if (DrawRadioOption(
                new Rect(rect.x, y, width, OptionRowHeight),
                MechanoidMechanitorStoryConfigurationLabels.EnabledLabel,
                configuration.purgeDirectiveEnabled,
                canInteract,
                out _))
            {
                configuration.purgeDirectiveEnabled = true;
                NormalizeAfterChange(context);
            }

            DrawDisabledTip(optionsRect, context);
        }
    }
}
