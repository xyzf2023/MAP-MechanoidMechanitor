using System;
using System.Reflection;
using HarmonyLib;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.GlitterworldDestroyer5
{
    /// <summary>
    /// 闪耀世界毁灭者5：肃清指令评级变化后，按当前评级静默补齐应获得的集群科技。
    /// 不保存历史最高评级或领取标记，研究完成状态本身作为唯一持久化依据。
    /// </summary>
    internal sealed class GlitterworldDestroyer5PurgeDirectiveResearchCompatibility :
        IThirdPartyCompatibilityModule
    {
        private const string MediumResearchDefName = "GD3_GiantCluster_Medium";
        private const string LargeResearchDefName = "GD3_GiantCluster_Large";
        private const string UltraResearchDefName = "GD3_GiantCluster_Ultra";

        public string ModuleId => "GlitterworldDestroyer5.PurgeDirectiveResearch";

        public string DisplayName => "闪耀世界毁灭者5·肃清评级科技支持";

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

            ResearchProjectDef? medium = ResolveResearch(MediumResearchDefName, mod);
            ResearchProjectDef? large = ResolveResearch(LargeResearchDefName, mod);
            ResearchProjectDef? ultra = ResolveResearch(UltraResearchDefName, mod);
            if (medium == null || large == null || ultra == null)
            {
                string missing = medium == null
                    ? MediumResearchDefName
                    : large == null
                        ? LargeResearchDefName
                        : UltraResearchDefName;
                return ThirdPartyCompatibilityResult.CreateTargetChanged(
                    ModuleId,
                    DisplayName,
                    PackageId,
                    $"预期研究Def：{missing}，且应由目标MOD提供；未能精确解析。");
            }

            MethodInfo? targetMethod = typeof(PurgeDirectiveRatingUtility)
                .GetMethod(
                    "NotifyRatingChanged",
                    BindingFlags.Static
                    | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly,
                    binder: null,
                    types: new[] { typeof(int), typeof(int) },
                    modifiers: null);
            if (targetMethod == null || targetMethod.ReturnType != typeof(void))
            {
                return ThirdPartyCompatibilityResult.CreateTargetChanged(
                    ModuleId,
                    DisplayName,
                    PackageId,
                    "预期目标：System.Void PurgeDirectiveRatingUtility.NotifyRatingChanged(System.Int32, System.Int32)。当前肃清评级变化入口已变化。");
            }

            MethodInfo? postfix = typeof(PurgeDirectiveResearchCompatibilityPatch)
                .GetMethod(
                    nameof(PurgeDirectiveResearchCompatibilityPatch.Postfix),
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
                    "无法解析本项目的肃清评级科技支持兼容补丁。",
                    new MissingMethodException(
                        typeof(PurgeDirectiveResearchCompatibilityPatch).FullName,
                        nameof(PurgeDirectiveResearchCompatibilityPatch.Postfix)));
            }

            try
            {
                harmony.Patch(
                    targetMethod,
                    postfix: new HarmonyMethod(postfix));
                PurgeDirectiveResearchCompatibilityPatch.Configure(
                    medium,
                    large,
                    ultra);
            }
            catch (Exception ex)
            {
                return ThirdPartyCompatibilityResult.CreateFailed(
                    ModuleId,
                    DisplayName,
                    PackageId,
                    "安装闪耀世界毁灭者5肃清评级科技支持兼容补丁时发生异常。",
                    ex,
                    targetMethod);
            }

            return ThirdPartyCompatibilityResult.CreateApplied(
                ModuleId,
                DisplayName,
                PackageId,
                "PurgeDirectiveRatingUtility.NotifyRatingChanged -> "
                + $"L2:{MediumResearchDefName}, L4:{LargeResearchDefName}, L5:{UltraResearchDefName}");
        }

        private static ResearchProjectDef? ResolveResearch(
            string defName,
            ModContentPack mod)
        {
            ResearchProjectDef? research =
                DefDatabase<ResearchProjectDef>.GetNamedSilentFail(defName);
            return research != null && ReferenceEquals(research.modContentPack, mod)
                ? research
                : null;
        }
    }
}
