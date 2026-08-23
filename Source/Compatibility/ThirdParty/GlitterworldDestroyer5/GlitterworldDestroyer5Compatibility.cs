using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.GlitterworldDestroyer5
{
    internal sealed class GlitterworldDestroyer5Compatibility :
        IThirdPartyCompatibilityModule
    {
        private const string TargetTypeName =
            "GD3.FloatMenuOptionProvider_ModifySavingMech";

        private const string TargetMethodName = "CanTakeOrder";

        public string ModuleId => "GlitterworldDestroyer5";

        public string DisplayName => "闪耀世界毁灭者5";

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
                        new[] { typeof(Pawn) },
                        out MethodInfo? targetMethod,
                        out string failureReason)
                || targetMethod == null)
            {
                return ThirdPartyCompatibilityResult.CreateTargetChanged(
                    ModuleId,
                    DisplayName,
                    PackageId,
                    $"预期目标：System.Boolean {TargetTypeName}." +
                    $"{TargetMethodName}(Verse.Pawn)。{failureReason}");
            }

            MethodInfo? postfix = typeof(ModifySavingMechCompatibilityPatch)
                .GetMethod(
                    nameof(ModifySavingMechCompatibilityPatch.Postfix),
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
                    "无法解析本项目的ModifySavingMech Postfix。",
                    new MissingMethodException(
                        typeof(ModifySavingMechCompatibilityPatch).FullName,
                        nameof(ModifySavingMechCompatibilityPatch.Postfix)));
            }

            harmony.Patch(
                targetMethod,
                postfix: new HarmonyMethod(postfix));

            return ThirdPartyCompatibilityResult.CreateApplied(
                ModuleId,
                DisplayName,
                PackageId,
                $"{TargetTypeName}.{TargetMethodName}(Verse.Pawn)");
        }
    }
}
