using System;
using HarmonyLib;
using MAP_MechanoidMechanitor.Compatibility.ThirdParty.GlitterworldDestroyer5;
using MAP_MechanoidMechanitor.Compatibility.ThirdParty.GlitterworldDestroyer5Expansion;
using MAP_MechanoidMechanitor.Compatibility.ThirdParty.GlitterworldDestroyer5ExpansionMechFusion;
using MAP_MechanoidMechanitor.Compatibility.ThirdParty.ProgressionEducation;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty
{
    internal static class ThirdPartyCompatibilityRegistry
    {
        private const string LogPrefix =
            "[MAP-机械族机械师] 第三方兼容：";

        private static readonly IThirdPartyCompatibilityModule[] Modules =
        {
            new GlitterworldDestroyer5Compatibility(),
            new GlitterworldDestroyer5DryseaCompatibility(),
            new GlitterworldDestroyer5ExpansionCompatibility(),
            new GlitterworldDestroyer5ExpansionMechFusionCompatibility(),
            new ProgressionEducationCompatibility()
        };

        public static void ApplyAll(Harmony harmony)
        {
            for (int i = 0; i < Modules.Length; i++)
            {
                IThirdPartyCompatibilityModule module = Modules[i];
                ThirdPartyCompatibilityResult result;

                try
                {
                    result = module.Apply(harmony);
                }
                catch (Exception ex)
                {
                    result = ThirdPartyCompatibilityResult.CreateFailed(
                        module.ModuleId,
                        module.DisplayName,
                        module.PackageId,
                        "应用兼容模块时发生未预期异常。",
                        ex);
                }

                LogResult(result);
            }
        }

        private static void LogResult(ThirdPartyCompatibilityResult result)
        {
            switch (result.Status)
            {
                case ThirdPartyCompatibilityStatus.Inactive:
                    return;

                case ThirdPartyCompatibilityStatus.Applied:
                    if (Prefs.DevMode)
                    {
                        Log.Message(
                            $"{LogPrefix}{result.DisplayName}（{result.PackageId}）" +
                            $"兼容已应用：{result.Detail}");
                    }
                    return;

                case ThirdPartyCompatibilityStatus.TargetChanged:
                    Log.Warning(
                        $"{LogPrefix}{result.DisplayName}（{result.PackageId}）" +
                        $"目标代码结构无法确认，兼容已安全跳过，不影响其他功能。" +
                        $"详情：{result.Detail}");
                    return;

                case ThirdPartyCompatibilityStatus.Failed:
                    Log.Error(
                        $"{LogPrefix}{result.DisplayName}（{result.PackageId}）" +
                        $"兼容应用失败，已停止该模块，不影响其他兼容模块。" +
                        $"详情：{result.Detail}" +
                        (result.Exception == null
                            ? string.Empty
                            : $"\n{result.Exception}"));
                    return;
            }
        }
    }
}
