using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    [DefOf]
    public static class MechanoidMechanitorStoryStyleDefOf
    {
        public static MechanoidMechanitorStoryStyleDef MAP_StoryStyle_Classic = null!;

        public static MechanoidMechanitorStoryStyleDef MAP_StoryStyle_Disorder = null!;

        public static MechanoidMechanitorStoryStyleDef MAP_StoryStyle_Custom = null!;

        static MechanoidMechanitorStoryStyleDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(MechanoidMechanitorStoryStyleDefOf));
        }
    }
}
