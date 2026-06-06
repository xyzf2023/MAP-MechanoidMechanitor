using HarmonyLib;
using Verse;

namespace MMT
{
    [StaticConstructorOnStartup]
    public static class ModInit
    {
        static ModInit()
        {
            new Harmony("xyzf.mechmechanitor.prototype").PatchAll();
        }
    }
}
