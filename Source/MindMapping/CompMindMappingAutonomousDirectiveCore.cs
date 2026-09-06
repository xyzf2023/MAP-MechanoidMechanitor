using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class CompProperties_MindMappingAutonomousDirectiveCore : CompProperties
    {
        public CompProperties_MindMappingAutonomousDirectiveCore()
            : base(typeof(CompMindMappingAutonomousDirectiveCore))
        {
        }
    }

    public sealed class CompMindMappingAutonomousDirectiveCore : ThingComp
    {
        private MindMappingData? data;

        public bool IsBlank => data == null;
        public MindMappingData? Data => data;

        public void Store(MindMappingData newData)
        {
            data = newData;
        }

        public void Clear()
        {
            data = null;
        }

        public override string TransformLabel(string label)
        {
            return data == null
                ? "MAP_MindMapping.Core.BlankLabel".Translate()
                : "MAP_MindMapping.Core.LoadedLabel".Translate(data.ShortName);
        }

        public override string CompInspectStringExtra()
        {
            return data?.GetInspectString() ?? "MAP_MindMapping.Inspect.Blank".Translate();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Deep.Look(ref data, "mindMappingData");
        }
    }
}
