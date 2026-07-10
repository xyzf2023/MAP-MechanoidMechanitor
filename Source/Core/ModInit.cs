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
            MechanoidMechanitorBrainImplantFeatureState.InitializeFromSettings();

            if (Prefs.DevMode)
            {
                Log.Message("[MAP-机械族机械师] Harmony补丁初始化成功。");
            }
        }
    }
}
