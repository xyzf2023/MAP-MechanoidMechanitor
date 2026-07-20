using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    [StaticConstructorOnStartup]
    public static class MechanoidOvermindUiAssets
    {
        public const string CerebrexCoreBrainPath = "Things/CerebrexCoreBrain";

        public static readonly Texture2D? CerebrexCoreBrain;

        static MechanoidOvermindUiAssets()
        {
            CerebrexCoreBrain = ContentFinder<Texture2D>.Get(CerebrexCoreBrainPath, reportFailure: false);
        }
    }
}
