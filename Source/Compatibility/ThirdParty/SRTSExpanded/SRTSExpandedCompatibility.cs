using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.SRTSExpanded
{
    /// <summary>
    /// 为第三方 MOD《SRTS Expanded (continued)》（Shashlichnik.srtsexpanded）提供驾驶员资格兼容。
    /// 仅在目标 MOD 已加载、且 SRTS.CompLaunchableSRTS.IsPilot(Pawn) 能按完整签名唯一解析时，
    /// 动态安装一个最小 Transpiler：只扩展该方法内部的 IsFreeColonist 身份门槛，
    /// 让拥有 MAP ShuttlePilot 能力的合法玩家机械体能够继续接受 SRTS 自身的驾驶属性检查。
    /// 不静态引用 SRTS.dll，不修改 Pawn.IsFreeColonist，不接管 SRTS 的人数、燃料、天气或其他起飞规则。
    /// </summary>
    internal sealed class SRTSExpandedCompatibility : IThirdPartyCompatibilityModule
    {
        private const string TargetPackageId = "Shashlichnik.srtsexpanded";
        private const string TargetTypeName = "SRTS.CompLaunchableSRTS";
        private const string TargetMethodName = "IsPilot";

        public string ModuleId => "SRTSExpanded";

        public string DisplayName => "SRTS Expanded (continued)";

        public string PackageId => TargetPackageId;

        public ThirdPartyCompatibilityResult Apply(Harmony harmony)
        {
            ModContentPack? srtsMod =
                ThirdPartyCompatibilityTargetResolver.FindRunningMod(TargetPackageId);
            if (srtsMod == null)
            {
                return ThirdPartyCompatibilityResult.CreateInactive(
                    ModuleId, DisplayName, PackageId);
            }

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueInstanceMethod(
                    srtsMod,
                    TargetTypeName,
                    TargetMethodName,
                    typeof(bool),
                    new[] { typeof(Pawn) },
                    out MethodInfo? isPilotMethod,
                    out string resolveFailure)
                || isPilotMethod == null)
            {
                return ThirdPartyCompatibilityResult.CreateTargetChanged(
                    ModuleId,
                    DisplayName,
                    PackageId,
                    $"预期目标：System.Boolean {TargetTypeName}.{TargetMethodName}(Verse.Pawn)。" +
                    resolveFailure);
            }

            MethodInfo? transpiler = typeof(SRTSExpandedPilotPatch).GetMethod(
                nameof(SRTSExpandedPilotPatch.TranspilerIsPilot),
                BindingFlags.Static
                | BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly);

            if (!IsValidTranspiler(transpiler))
            {
                return ThirdPartyCompatibilityResult.CreateFailed(
                    ModuleId,
                    DisplayName,
                    PackageId,
                    "无法解析本项目的 SRTS Expanded 驾驶员兼容 Transpiler，或其签名不符。",
                    new MissingMethodException(
                        typeof(SRTSExpandedPilotPatch).FullName,
                        nameof(SRTSExpandedPilotPatch.TranspilerIsPilot)),
                    isPilotMethod);
            }

            try
            {
                harmony.Patch(
                    isPilotMethod,
                    transpiler: new HarmonyMethod(transpiler));
            }
            catch (Exception ex)
            {
                return ThirdPartyCompatibilityResult.CreateFailed(
                    ModuleId,
                    DisplayName,
                    PackageId,
                    "安装 SRTS Expanded 驾驶员资格兼容补丁时发生异常。",
                    ex,
                    isPilotMethod);
            }

            return ThirdPartyCompatibilityResult.CreateApplied(
                ModuleId,
                DisplayName,
                PackageId,
                "已扩展 CompLaunchableSRTS.IsPilot(Pawn) 的自由殖民者身份门槛；" +
                "合法 MAP ShuttlePilot 机械体可计入 SRTS 驾驶员，其余 SRTS 驾驶与起飞规则保持原样。");
        }

        private static bool IsValidTranspiler(MethodInfo? method)
        {
            if (method == null
                || !method.IsStatic
                || method.ReturnType != typeof(IEnumerable<CodeInstruction>))
            {
                return false;
            }

            ParameterInfo[] parameters = method.GetParameters();
            return parameters.Length == 1
                && parameters[0].ParameterType == typeof(IEnumerable<CodeInstruction>);
        }
    }
}
