using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty
{
    internal interface IThirdPartyCompatibilityModule
    {
        string ModuleId { get; }

        string DisplayName { get; }

        string PackageId { get; }

        ThirdPartyCompatibilityResult Apply(Harmony harmony);
    }

    internal static class ThirdPartyCompatibilityBootstrap
    {
        private const string LogPrefix =
            "[MAP-机械族机械师] 第三方兼容：";

        private static bool initialized;

        public static void ApplyAll(Harmony harmony)
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            if (harmony == null)
            {
                Log.Error($"{LogPrefix}Harmony实例为空，第三方兼容层未初始化。");
                return;
            }

            ThirdPartyCompatibilityRegistry.ApplyAll(harmony);
        }
    }
}
