using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [StaticConstructorOnStartup]
    public static class ModInit
    {
        static ModInit()
        {
            new Harmony("xyzf.map.mechanoidmechanitor").PatchAll();

            if (Prefs.DevMode)
            {
                Log.Message("[MAP-MechanoidMechanitor] Loaded.");
            }
        }
    }
}
