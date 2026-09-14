using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Sound;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.GlitterworldDestroyer5
{
    /// <summary>
    /// 闪耀世界毁灭者5：禁卫机械蜈蚣专用武器切换与无监管者机械族机械师兼容。
    /// 仅包装 CompChangeWeaponB 返回的 Gizmo，不静态引用第三方程序集类型。
    /// </summary>
    internal sealed class GlitterworldDestroyer5CataphractCentipedeWeaponCompatibility :
        IThirdPartyCompatibilityModule
    {
        private const string TargetTypeName = "GD3.CompChangeWeaponB";
        private const string TargetMethodName = "CompGetGizmosExtra";
        private const string SrWeaponDefName = "CataphractCentipede_SR";
        private const string FyWeaponDefName = "CataphractCentipede_FY";
        private const string SwitchSoundDefName = "Interact_ChargeRifle";

        public string ModuleId =>
            "GlitterworldDestroyer5.CataphractCentipedeWeapon";

        public string DisplayName =>
            "闪耀世界毁灭者5·禁卫机械蜈蚣武器切换";

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

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueInstanceMethod(
                    mod,
                    TargetTypeName,
                    TargetMethodName,
                    typeof(IEnumerable<Gizmo>),
                    Type.EmptyTypes,
                    out MethodInfo? targetMethod,
                    out string targetFailure)
                || targetMethod == null)
            {
                return TargetChanged(
                    $"预期目标：System.Collections.Generic.IEnumerable<Verse.Gizmo> "
                    + $"{TargetTypeName}.{TargetMethodName}()。{targetFailure}");
            }

            ThingDef? srWeapon = ResolveOwnedThingDef(mod, SrWeaponDefName);
            ThingDef? fyWeapon = ResolveOwnedThingDef(mod, FyWeaponDefName);
            if (srWeapon == null || fyWeapon == null)
            {
                string missing = srWeapon == null
                    ? SrWeaponDefName
                    : FyWeaponDefName;
                return ThirdPartyCompatibilityResult.CreateTargetChanged(
                    ModuleId,
                    DisplayName,
                    PackageId,
                    $"预期武器Def：{missing}，且应由目标MOD提供；未能精确解析。",
                    targetMethod);
            }

            SoundDef? switchSound =
                DefDatabase<SoundDef>.GetNamedSilentFail(SwitchSoundDefName);
            if (switchSound == null)
            {
                return ThirdPartyCompatibilityResult.CreateTargetChanged(
                    ModuleId,
                    DisplayName,
                    PackageId,
                    $"预期音效Def：{SwitchSoundDefName}；未能解析。",
                    targetMethod);
            }

            MethodInfo? postfix =
                typeof(CataphractCentipedeWeaponCompatibilityPatch).GetMethod(
                    nameof(CataphractCentipedeWeaponCompatibilityPatch.Postfix),
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
                    "无法解析本项目的禁卫机械蜈蚣武器切换 Postfix。",
                    new MissingMethodException(
                        typeof(CataphractCentipedeWeaponCompatibilityPatch).FullName,
                        nameof(CataphractCentipedeWeaponCompatibilityPatch.Postfix)),
                    targetMethod);
            }

            CataphractCentipedeWeaponCompatibilityPatch.Configure(
                srWeapon,
                fyWeapon,
                switchSound);

            try
            {
                harmony.Patch(
                    targetMethod,
                    postfix: new HarmonyMethod(postfix));
            }
            catch (Exception ex)
            {
                return ThirdPartyCompatibilityResult.CreateFailed(
                    ModuleId,
                    DisplayName,
                    PackageId,
                    "安装禁卫机械蜈蚣武器切换兼容补丁时发生异常。",
                    ex,
                    targetMethod);
            }

            return ThirdPartyCompatibilityResult.CreateApplied(
                ModuleId,
                DisplayName,
                PackageId,
                "GD3.CompChangeWeaponB.CompGetGizmosExtra -> "
                + "无监管者机械族机械师专武切换与自由装备保护");
        }

        private ThirdPartyCompatibilityResult TargetChanged(string reason)
        {
            return ThirdPartyCompatibilityResult.CreateTargetChanged(
                ModuleId,
                DisplayName,
                PackageId,
                reason);
        }

        private static ThingDef? ResolveOwnedThingDef(
            ModContentPack mod,
            string defName)
        {
            ThingDef? def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            return def != null && ReferenceEquals(def.modContentPack, mod)
                ? def
                : null;
        }
    }
}
