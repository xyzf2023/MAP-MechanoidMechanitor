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

        protected override TaggedString GetDescription()
        {
            return "MAP_MechanoidMechanitor.Story.Component.MechHiveRelation.Description"
                .Translate();
        }

        public override float GetHeight(
            MechanoidMechanitorStoryConfigurationContext context,
            float width)
        {
            return MeasureHeaderAndDescription(width)
                + Modes.Length * OptionRowHeight
                + SectionGap;
        }

        public override void Draw(
            Rect rect,
            MechanoidMechanitorStoryConfigurationContext context)
        {
            MechanoidMechanitorStoryConfiguration configuration = context.Configuration;
            float width = rect.width;
            float y = DrawHeaderAndDescription(rect, width);

            for (int i = 0; i < Modes.Length; i++)
            {
                MechanoidMechanitorMechHiveRelationMode mode = Modes[i];
                Rect optionRect = new Rect(rect.x, y, width, OptionRowHeight);
                if (DrawRadioOption(
                    optionRect,
                    MechanoidMechanitorStoryConfigurationLabels.LabelFor(mode),
                    configuration.mechHiveRelationMode == mode,
                    enabled: true,
                    out _))
                {
                    configuration.mechHiveRelationMode = mode;
                    NormalizeAfterChange(context);
                }

                y += OptionRowHeight;
            }
        }
    }
}
