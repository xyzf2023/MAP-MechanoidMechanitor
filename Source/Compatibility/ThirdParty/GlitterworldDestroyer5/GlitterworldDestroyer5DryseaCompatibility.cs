using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.GlitterworldDestroyer5
{
    /// <summary>
    /// 独立应用闪耀世界毁灭者5“枯海”白花触发兼容。
    /// 与“动手脚”兼容分开注册，任一目标变化都不会阻断另一个模块。
    /// </summary>
    internal sealed class GlitterworldDestroyer5DryseaCompatibility :
        IThirdPartyCompatibilityModule
    {
        private const string TargetTypeName = "GD3.DryseaDummy";

        private const string TargetMethodName = "get_IfPawnStanding";

        public string ModuleId => "GlitterworldDestroyer5.Drysea";

        public string DisplayName => "闪耀世界毁灭者5·枯海";

        public string PackageId => "fxz.glitterworlddestroyer.mk5";

        public ThirdPartyCompatibilityResult Apply(Harmony harmony)
        {
            ModContentPack? mod =
                ThirdPartyCompatibilityTargetResolver.FindRunningMod(PackageId);
            if (mod == null)
            {
                return ThirdPartyCompatibilityResult.CreateInactive(
                    ModuleId,
                    DisplayName,
                    PackageId);
            }

            if (!ThirdPartyCompatibilityTargetResolver
                    .TryResolveUniqueInstanceMethod(
                        mod,
                        TargetTypeName,
                        TargetMethodName,
                        typeof(bool),
                        Type.EmptyTypes,
                        out MethodInfo? targetMethod,
                        out string failureReason)
                || targetMethod == null)
            {
                return ThirdPartyCompatibilityResult.CreateTargetChanged(
                    ModuleId,
                    DisplayName,
                    PackageId,
                    $"预期目标：System.Boolean {TargetTypeName}." +
                    $"{TargetMethodName}()。{failureReason}");
            }

            MethodInfo? postfix = typeof(DryseaStandingCompatibilityPatch)
                .GetMethod(
                    nameof(DryseaStandingCompatibilityPatch.Postfix),
                    BindingFlags.Static
                    | BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly);
            if (postfix == null)
            {
                return ThirdPartyCompatibilityResult.CreateFailed(
                    ModuleId,
                    DisplayName,
                    PackageId,
                    "无法解析本项目的DryseaStanding Postfix。",
                    new MissingMethodException(
                        typeof(DryseaStandingCompatibilityPatch).FullName,
                        nameof(DryseaStandingCompatibilityPatch.Postfix)));
            }

            harmony.Patch(
                targetMethod,
                postfix: new HarmonyMethod(postfix));

            return ThirdPartyCompatibilityResult.CreateApplied(
                ModuleId,
                DisplayName,
                PackageId,
                $"{TargetTypeName}.IfPawnStanding getter");
        }
    }
}
