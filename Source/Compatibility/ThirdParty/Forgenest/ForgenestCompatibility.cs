using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.Forgenest
{
    internal sealed class ForgenestCompatibility : IThirdPartyCompatibilityModule
    {
        private const string TargetTypeName = "RKFN.CompMechProducer";
        private const string TargetMethodName = "get_AllMechanitors";

        public string ModuleId => "Forgenest.MechProducer";
        public string DisplayName => "Forgenest·机械体培育机械师选择";
        public string PackageId => "riko.forgenest";

        public ThirdPartyCompatibilityResult Apply(Harmony harmony)
        {
            ModContentPack? mod =
                ThirdPartyCompatibilityTargetResolver.FindRunningMod(PackageId);
            if (mod == null)
            {
                return ThirdPartyCompatibilityResult.CreateInactive(
                    ModuleId, DisplayName, PackageId);
            }

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueInstanceMethod(
                    mod,
                    TargetTypeName,
                    TargetMethodName,
                    typeof(IEnumerable<Pawn>),
                    Type.EmptyTypes,
                    out MethodInfo? targetMethod,
                    out string failureReason)
                || targetMethod == null)
            {
                return ThirdPartyCompatibilityResult.CreateTargetChanged(
                    ModuleId, DisplayName, PackageId,
                    $"预期目标：IEnumerable<Pawn> {TargetTypeName}.{TargetMethodName}()。{failureReason}");
            }

            if (targetMethod.DeclaringType == null
                || !typeof(ThingComp).IsAssignableFrom(targetMethod.DeclaringType))
            {
                return ThirdPartyCompatibilityResult.CreateTargetChanged(
                    ModuleId, DisplayName, PackageId,
                    $"{TargetTypeName}不再继承ThingComp。", targetMethod);
            }

            MethodInfo? postfix = typeof(ForgenestMechanitorSelectionPatch).GetMethod(
                nameof(ForgenestMechanitorSelectionPatch.Postfix),
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            if (postfix == null)
            {
                return ThirdPartyCompatibilityResult.CreateFailed(
                    ModuleId, DisplayName, PackageId,
                    "无法解析本项目的Forgenest机械师选择Postfix。",
                    new MissingMethodException(
                        typeof(ForgenestMechanitorSelectionPatch).FullName,
                        nameof(ForgenestMechanitorSelectionPatch.Postfix)));
            }

            try
            {
                harmony.Patch(targetMethod, postfix: new HarmonyMethod(postfix));
            }
            catch (Exception ex)
            {
                return ThirdPartyCompatibilityResult.CreateFailed(
                    ModuleId, DisplayName, PackageId,
                    "安装Forgenest机械师选择兼容补丁时发生异常。",
                    ex, targetMethod);
            }

            return ThirdPartyCompatibilityResult.CreateApplied(
                ModuleId, DisplayName, PackageId,
                $"{TargetTypeName}.AllMechanitors getter");
        }
    }
}
