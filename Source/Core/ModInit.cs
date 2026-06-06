using HarmonyLib;
using Verse;

namespace MMT
{
    [StaticConstructorOnStartup]
    public static class ModInit
    {
        static ModInit()
        {
            new Harmony("xyzf.mechanoidmechanitor.test").PatchAll();

            if (Prefs.DevMode)
            {
                Log.Message("[MMT] Mechanoid Mechanitor Test loaded.");
            }
        }
    }
}
