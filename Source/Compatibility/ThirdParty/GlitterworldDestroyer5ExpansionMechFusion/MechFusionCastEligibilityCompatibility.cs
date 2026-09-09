using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.GlitterworldDestroyer5ExpansionMechFusion
{
    /// <summary>
    /// 让闪毁5融合的机械融合/机械超融合 Apply() 接纳机械族机械师。
    /// 仅扩展 MechlinkImplant 门槛，不改变第三方 MOD 的融合业务逻辑。
    /// </summary>
    internal sealed class MechFusionCastEligibilityCompatibility :
        IThirdPartyCompatibilityModule
    {
        private const string FusionTypeName = "MechFusion.Comp_AbilityMechFusion";

        private const string SuperFusionTypeName =
            "MechFusion.Comp_AbilitySuperMechFusion";

        public string ModuleId =>
            "GlitterworldDestroyer5ExpansionMechFusion.CastEligibility";

        public string DisplayName => "闪耀世界毁灭者5扩展·MechFusion 融合施放资格";

        public string PackageId => "jixuanming.mechfusion.onepointsix";

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

            Type[] applyParameters =
            {
                typeof(LocalTargetInfo),
                typeof(LocalTargetInfo)
            };

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueInstanceMethod(
                    mod,
                    FusionTypeName,
                    "Apply",
                    typeof(void),
                    applyParameters,
                    out MethodInfo? fusionApply,
                    out string fusionFailure))
            {
                return ThirdPartyCompatibilityResult.CreateTargetChanged(
                    ModuleId,
                    DisplayName,
                    PackageId,
                    fusionFailure);
            }

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueInstanceMethod(
                    mod,
                    SuperFusionTypeName,
                    "Apply",
                    typeof(void),
                    applyParameters,
                    out MethodInfo? superFusionApply,
                    out string superFusionFailure))
            {
                return ThirdPartyCompatibilityResult.CreateTargetChanged(
                    ModuleId,
                    DisplayName,
                    PackageId,
                    superFusionFailure,
                    fusionApply!);
            }

            MethodInfo? fusionTranspiler =
                typeof(MechFusionCastEligibilityCompatibilityPatch).GetMethod(
                    nameof(MechFusionCastEligibilityCompatibilityPatch.Transpiler_MechFusionApply),
                    BindingFlags.Static
                    | BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly);
            MethodInfo? superFusionTranspiler =
                typeof(MechFusionCastEligibilityCompatibilityPatch).GetMethod(
                    nameof(MechFusionCastEligibilityCompatibilityPatch.Transpiler_SuperMechFusionApply),
                    BindingFlags.Static
                    | BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly);

            if (fusionTranspiler == null || superFusionTranspiler == null)
            {
                return ThirdPartyCompatibilityResult.CreateFailed(
                    ModuleId,
                    DisplayName,
                    PackageId,
                    "无法解析本项目的 MechFusion 融合施放资格 Transpiler。",
                    new MissingMethodException(
                        typeof(MechFusionCastEligibilityCompatibilityPatch).FullName,
                        nameof(MechFusionCastEligibilityCompatibilityPatch.Transpiler_MechFusionApply)
                        + " / "
                        + nameof(MechFusionCastEligibilityCompatibilityPatch.Transpiler_SuperMechFusionApply)),
                    fusionApply!,
                    superFusionApply!);
            }

            MechFusionCastEligibilityCompatibilityPatch.ResetInstallState();

            try
            {
                harmony.Patch(
                    fusionApply!,
                    transpiler: new HarmonyMethod(fusionTranspiler));
                if (!MechFusionCastEligibilityCompatibilityPatch
                    .FusionApplyAnchorMatched)
                {
                    UnpatchQuietly(harmony, fusionApply!, fusionTranspiler);
                    return ThirdPartyCompatibilityResult.CreateTargetChanged(
                        ModuleId,
                        DisplayName,
                        PackageId,
                        "MechFusion.Comp_AbilityMechFusion.Apply 中未能唯一确认"
                        + " MechlinkImplant → HediffSet.HasHediff 门槛，补丁已回滚。",
                        fusionApply!);
                }

                harmony.Patch(
                    superFusionApply!,
                    transpiler: new HarmonyMethod(superFusionTranspiler));
                if (!MechFusionCastEligibilityCompatibilityPatch
                    .SuperFusionApplyAnchorMatched)
                {
                    UnpatchQuietly(harmony, fusionApply!, fusionTranspiler);
                    UnpatchQuietly(
                        harmony,
                        superFusionApply!,
                        superFusionTranspiler);
                    return ThirdPartyCompatibilityResult.CreateTargetChanged(
                        ModuleId,
                        DisplayName,
                        PackageId,
                        "MechFusion.Comp_AbilitySuperMechFusion.Apply 中未能唯一确认"
                        + " MechlinkImplant → HediffSet.HasHediff 门槛，补丁已回滚。",
                        fusionApply!,
                        superFusionApply!);
                }
            }
            catch (Exception ex)
            {
                UnpatchQuietly(harmony, fusionApply!, fusionTranspiler);
                UnpatchQuietly(
                    harmony,
                    superFusionApply!,
                    superFusionTranspiler);
                return ThirdPartyCompatibilityResult.CreateFailed(
                    ModuleId,
                    DisplayName,
                    PackageId,
                    "安装 MechFusion 融合施放资格补丁时发生异常。",
                    ex,
                    fusionApply!,
                    superFusionApply!);
            }

            return ThirdPartyCompatibilityResult.CreateApplied(
                ModuleId,
                DisplayName,
                PackageId,
                "MechFusion 与 SuperMechFusion 的 Apply() 仅扩展机师链接门槛："
                + "原有 Mechlink 判定保持不变，机械族机械师在无 Mechlink 时也可通过；"
                + "融合配方、阵营与生成逻辑均由第三方 MOD 原样执行。");
        }

        private static void UnpatchQuietly(
            Harmony harmony,
            MethodInfo original,
            MethodInfo patch)
        {
            try
            {
                harmony.Unpatch(original, patch);
            }
            catch
            {
                // 回滚失败不得掩盖最初的目标变化或安装异常。
            }
        }
    }
}
