using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.GlitterworldDestroyer5
{
    /// <summary>
    /// 闪耀世界毁灭者5：短波接收器与机械巢盟友/肃清指令状态兼容。
    /// 肃清指令开启时禁止扫描；其他机械巢盟友状态下以科技支持替代敌对集群任务。
    /// </summary>
    internal sealed class GlitterworldDestroyer5ClusterReceiverCompatibility :
        IThirdPartyCompatibilityModule
    {
        private const string DetectionTypeName = "GD3.CompUseEffect_Detection";
        private const string ReceiverSelectTypeName = "GD3.CompReceiverSelect";
        private const string SettingsTypeName = "GD3.GDSettings";
        private const string MediumResearchDefName = "GD3_GiantCluster_Medium";
        private const string LargeResearchDefName = "GD3_GiantCluster_Large";
        private const string UltraResearchDefName = "GD3_GiantCluster_Ultra";

        public string ModuleId => "GlitterworldDestroyer5.ClusterReceiver";

        public string DisplayName => "闪耀世界毁灭者5·巨型集群接收器";

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

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(
                    mod,
                    DetectionTypeName,
                    out Type? detectionType,
                    out string detectionFailure))
            {
                return TargetChanged(detectionFailure);
            }

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(
                    mod,
                    ReceiverSelectTypeName,
                    out Type? receiverSelectType,
                    out string receiverFailure))
            {
                return TargetChanged(receiverFailure);
            }

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(
                    mod,
                    SettingsTypeName,
                    out Type? settingsType,
                    out string settingsFailure))
            {
                return TargetChanged(settingsFailure);
            }

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueInstanceMethod(
                    mod,
                    DetectionTypeName,
                    "CanBeUsedBy",
                    typeof(AcceptanceReport),
                    new[] { typeof(Pawn) },
                    out MethodInfo? canBeUsedBy,
                    out string canBeUsedFailure))
            {
                return TargetChanged(canBeUsedFailure);
            }

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueInstanceMethod(
                    mod,
                    DetectionTypeName,
                    "DoEffect",
                    typeof(void),
                    new[] { typeof(Pawn) },
                    out MethodInfo? doEffect,
                    out string doEffectFailure))
            {
                return TargetChanged(doEffectFailure);
            }

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueInstanceMethod(
                    mod,
                    DetectionTypeName,
                    "get_CompSelect",
                    receiverSelectType!,
                    Type.EmptyTypes,
                    out MethodInfo? compSelectGetter,
                    out string compSelectFailure))
            {
                return TargetChanged(compSelectFailure);
            }

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueInstanceMethod(
                    mod,
                    ReceiverSelectTypeName,
                    "get_Mark",
                    typeof(int),
                    Type.EmptyTypes,
                    out MethodInfo? markGetter,
                    out string markFailure))
            {
                return TargetChanged(markFailure);
            }

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueField(
                    detectionType!,
                    "delayTicks",
                    typeof(int),
                    BindingFlags.Instance
                    | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly,
                    out FieldInfo? delayTicksField,
                    out string delayTicksFailure))
            {
                return TargetChanged(delayTicksFailure);
            }

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueField(
                    settingsType!,
                    "DetectCooldown",
                    typeof(int),
                    BindingFlags.Static
                    | BindingFlags.Public
                    | BindingFlags.DeclaredOnly,
                    out FieldInfo? detectCooldownField,
                    out string cooldownFailure))
            {
                return TargetChanged(cooldownFailure);
            }

            MethodInfo? canBeUsedPrefix = typeof(ClusterReceiverCompatibilityPatch)
                .GetMethod(
                    nameof(ClusterReceiverCompatibilityPatch.CanBeUsedByPrefix),
                    BindingFlags.Static
                    | BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly);
            MethodInfo? doEffectPrefix = typeof(ClusterReceiverCompatibilityPatch)
                .GetMethod(
                    nameof(ClusterReceiverCompatibilityPatch.DoEffectPrefix),
                    BindingFlags.Static
                    | BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly);
            if (canBeUsedPrefix == null || doEffectPrefix == null)
            {
                return ThirdPartyCompatibilityResult.CreateFailed(
                    ModuleId,
                    DisplayName,
                    PackageId,
                    "无法解析本项目的巨型集群接收器兼容补丁。",
                    new MissingMethodException(
                        typeof(ClusterReceiverCompatibilityPatch).FullName,
                        canBeUsedPrefix == null
                            ? nameof(ClusterReceiverCompatibilityPatch.CanBeUsedByPrefix)
                            : nameof(ClusterReceiverCompatibilityPatch.DoEffectPrefix)));
            }

            ClusterReceiverCompatibilityPatch.Configure(
                compSelectGetter!,
                markGetter!,
                delayTicksField!,
                detectCooldownField!,
                medium,
                large,
                ultra);

            try
            {
                harmony.Patch(
                    canBeUsedBy!,
                    prefix: new HarmonyMethod(canBeUsedPrefix));
                harmony.Patch(
                    doEffect!,
                    prefix: new HarmonyMethod(doEffectPrefix));
            }
            catch (Exception ex)
            {
                UnpatchQuietly(harmony, canBeUsedBy!, canBeUsedPrefix);
                UnpatchQuietly(harmony, doEffect!, doEffectPrefix);
                return ThirdPartyCompatibilityResult.CreateFailed(
                    ModuleId,
                    DisplayName,
                    PackageId,
                    "安装闪耀世界毁灭者5巨型集群接收器兼容补丁时发生异常。",
                    ex,
                    doEffect!);
            }

            return ThirdPartyCompatibilityResult.CreateApplied(
                ModuleId,
                DisplayName,
                PackageId,
                "GD3.CompUseEffect_Detection.CanBeUsedBy/DoEffect -> "
                + "肃清禁用；机械巢盟友扫描替换");
        }

        private ThirdPartyCompatibilityResult TargetChanged(string reason)
        {
            return ThirdPartyCompatibilityResult.CreateTargetChanged(
                ModuleId,
                DisplayName,
                PackageId,
                reason);
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
                // 回滚失败不得掩盖最初的补丁安装异常。
            }
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
