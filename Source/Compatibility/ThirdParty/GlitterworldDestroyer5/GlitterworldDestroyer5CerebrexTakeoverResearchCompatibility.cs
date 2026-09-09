using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.GlitterworldDestroyer5
{
    /// <summary>
    /// 闪耀世界毁灭者5：在奥德赛主脑正式接管完成后，
    /// 静默完成三级集群科技，并由原版 ResearchManager 递归补齐其前置科技。
    /// </summary>
    internal sealed class GlitterworldDestroyer5CerebrexTakeoverResearchCompatibility :
        IThirdPartyCompatibilityModule
    {
        private const string TargetResearchDefName = "GD3_GiantCluster_Ultra";

        public string ModuleId => "GlitterworldDestroyer5.CerebrexTakeoverResearch";

        public string DisplayName => "闪耀世界毁灭者5·主脑接管集群科技";

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

            ResearchProjectDef? targetResearch =
                DefDatabase<ResearchProjectDef>.GetNamedSilentFail(TargetResearchDefName);
            if (targetResearch == null
                || !ReferenceEquals(targetResearch.modContentPack, mod))
            {
                return ThirdPartyCompatibilityResult.CreateTargetChanged(
                    ModuleId,
                    DisplayName,
                    PackageId,
                    $"预期研究Def：{TargetResearchDefName}，且应由目标MOD提供；未能精确解析。");
            }

            MethodInfo? targetMethod = typeof(GameComponent_CerebrexTakeoverState)
                .GetMethod(
                    nameof(GameComponent_CerebrexTakeoverState.CompleteTakeover),
                    BindingFlags.Instance
                    | BindingFlags.Public
                    | BindingFlags.DeclaredOnly,
                    binder: null,
                    types: new[] { typeof(CompCerebrexCore), typeof(Pawn) },
                    modifiers: null);
            if (targetMethod == null || targetMethod.ReturnType != typeof(bool))
            {
                return ThirdPartyCompatibilityResult.CreateTargetChanged(
                    ModuleId,
                    DisplayName,
                    PackageId,
                    "预期目标：System.Boolean GameComponent_CerebrexTakeoverState."
                    + "CompleteTakeover(CompCerebrexCore, Verse.Pawn)。当前主脑接管入口已变化。");
            }

            MethodInfo? prefix = typeof(CerebrexTakeoverResearchCompatibilityPatch)
                .GetMethod(
                    nameof(CerebrexTakeoverResearchCompatibilityPatch.Prefix),
                    BindingFlags.Static
                    | BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly);
            MethodInfo? postfix = typeof(CerebrexTakeoverResearchCompatibilityPatch)
                .GetMethod(
                    nameof(CerebrexTakeoverResearchCompatibilityPatch.Postfix),
                    BindingFlags.Static
                    | BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly);
            if (prefix == null || postfix == null)
            {
                return ThirdPartyCompatibilityResult.CreateFailed(
                    ModuleId,
                    DisplayName,
                    PackageId,
                    "无法解析本项目的主脑接管集群科技兼容补丁。",
                    new MissingMethodException(
                        typeof(CerebrexTakeoverResearchCompatibilityPatch).FullName,
                        prefix == null
                            ? nameof(CerebrexTakeoverResearchCompatibilityPatch.Prefix)
                            : nameof(CerebrexTakeoverResearchCompatibilityPatch.Postfix)));
            }

            try
            {
                harmony.Patch(
                    targetMethod,
                    prefix: new HarmonyMethod(prefix),
                    postfix: new HarmonyMethod(postfix));
                CerebrexTakeoverResearchCompatibilityPatch.Configure(targetResearch);
            }
            catch (Exception ex)
            {
                return ThirdPartyCompatibilityResult.CreateFailed(
                    ModuleId,
                    DisplayName,
                    PackageId,
                    "安装闪耀世界毁灭者5主脑接管集群科技兼容补丁时发生异常。",
                    ex,
                    targetMethod);
            }

            return ThirdPartyCompatibilityResult.CreateApplied(
                ModuleId,
                DisplayName,
                PackageId,
                $"{nameof(GameComponent_CerebrexTakeoverState)}."
                + $"{nameof(GameComponent_CerebrexTakeoverState.CompleteTakeover)} -> "
                + TargetResearchDefName);
        }
    }
}
