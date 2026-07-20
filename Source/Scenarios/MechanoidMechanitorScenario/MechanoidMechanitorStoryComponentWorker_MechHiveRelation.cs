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
            return context.HasMechHive;
        }

        public override bool CanInteract(MechanoidMechanitorStoryConfigurationContext context)
        {
            return !context.HasPursuingMechanoidsScenarioPart;
        }

        public override string? GetDisabledReason(
            MechanoidMechanitorStoryConfigurationContext context)
        {
            if (CanInteract(context))
            {
                return null;
            }

            return "MAP_MechanoidMechanitor.Story.MechHiveRelation.PursuingMechanoidsDisabledReason"
                .Translate();
        }

        protected override TaggedString GetDescription()
        {
            return "MAP_MechanoidMechanitor.Story.Component.MechHiveRelation.Description"
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
