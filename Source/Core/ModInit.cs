using HarmonyLib;
using MAP_MechanoidMechanitor.Compatibility.ThirdParty;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [StaticConstructorOnStartup]
    public static class ModInit
    {
        /// <summary>
        /// 与 Harmony.PatchAll 使用的 ID 保持一致；诊断工具用此核对补丁是否加载。
        /// </summary>
        public const string HarmonyId = "xyzf.map.mechanoidmechanitor";

        static ModInit()
        {
            Harmony harmony = new Harmony(HarmonyId);
            harmony.PatchAll();
            AutonomousMechRepairUtility.EnsureInnateRepairableDefs();
            MechFusionGenerationPatch.RemoveFromInitialCache();
            ThirdPartyCompatibilityBootstrap.ApplyAll(harmony);
            PawnNameValidationUtility.ExtendPawnNameRegex();
            MechanoidMechanitorBrainImplantFeatureState.InitializeFromSettings();
            HumanImplantFeatureState.InitializeFromSettings();

            Log.Message("[MAP-机械族机械师] Harmony补丁初始化成功。");
        }
    }
}
