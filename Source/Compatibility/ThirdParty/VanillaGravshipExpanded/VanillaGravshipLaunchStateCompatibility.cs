using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.VanillaGravshipExpanded
{
    internal sealed class VanillaGravshipLaunchStateCompatibility : IThirdPartyCompatibilityModule
    {
        internal const string ModPackageId = "vanillaexpanded.gravship";
        private const string PatchTypeName =
            "VanillaGravshipExpanded.RitualBehaviorWorker_GravshipLaunch_TryExecuteOn_Patch";
        public string ModuleId => "VanillaGravshipExpanded.LaunchState";
        public string DisplayName => "Vanilla Gravship Expanded：起飞仪式状态保护";
        public string PackageId => ModPackageId;

        public ThirdPartyCompatibilityResult Apply(Harmony harmony)
        {
            ModContentPack? mod = ThirdPartyCompatibilityTargetResolver.FindRunningMod(PackageId);
            if (mod == null)
                return ThirdPartyCompatibilityResult.CreateInactive(ModuleId, DisplayName, PackageId);

            MethodInfo? target = null;
            MethodInfo? patch = null;
            bool attempted = false;
            try
            {
                if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(mod, PatchTypeName,
                        out Type? patchType, out string failure)
                    || !ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(mod,
                        "VanillaGravshipExpanded.LordJob_Ritual_ExposeData_Patch",
                        out Type? stateType, out failure)
                    || !ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(mod,
                        "VanillaGravshipExpanded.SettlementProximityGoodwillUtility_CheckConfirmSettle_Patch",
                        out Type? destinationType, out failure))
                    return Changed(failure);

                target = AccessTools.DeclaredMethod(patchType, "Postfix", new[]
                {
                    typeof(TargetInfo), typeof(Pawn), typeof(Precept_Ritual),
                    typeof(RitualObligation), typeof(RitualRoleAssignments), typeof(bool)
                });
                if (target == null || !target.IsStatic || target.ReturnType != typeof(void)
                    || target.ContainsGenericParameters || target.GetMethodBody() == null)
                    return Changed("VGE 起飞 Postfix 的静态签名不符合预期。");

                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.Static | BindingFlags.DeclaredOnly;
                if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueField(stateType!, "targetTile",
                        typeof(Dictionary<LordJob_Ritual, PlanetTile>), flags,
                        out FieldInfo? dictionary, out failure)
                    || !ThirdPartyCompatibilityTargetResolver.TryResolveUniqueField(destinationType!, "targetTile",
                        typeof(PlanetTile), flags, out FieldInfo? destination, out failure))
                    return Changed(failure);

                VanillaGravshipLaunchStatePatch.Configure(dictionary!, destination!);
                if (!VanillaGravshipLaunchStatePatch.TryFindAnchors(
                        new List<CodeInstruction>(PatchProcessor.GetOriginalInstructions(target)),
                        out _, out _, out failure))
                    return Changed(failure);

                patch = AccessTools.DeclaredMethod(typeof(VanillaGravshipLaunchStatePatch),
                    nameof(VanillaGravshipLaunchStatePatch.Transpiler));
                if (patch == null)
                    throw new MissingMethodException(nameof(VanillaGravshipLaunchStatePatch.Transpiler));
                attempted = true;
                harmony.Patch(target, transpiler: new HarmonyMethod(patch));
                return ThirdPartyCompatibilityResult.CreateApplied(ModuleId, DisplayName, PackageId,
                    "仅记录本次目标、仪式与角色分配对应的起飞任务；启动失败时不写入空键。");
            }
            catch (Exception ex)
            {
                if (attempted && target != null && patch != null)
                {
                    try { harmony.Unpatch(target, patch); }
                    catch { /* 只回滚本模块自己的补丁，保留安装异常。 */ }
                }
                return ThirdPartyCompatibilityResult.CreateFailed(ModuleId, DisplayName, PackageId,
                    "起飞状态兼容安装失败，已尝试回滚本模块补丁。", ex,
                    target == null ? Array.Empty<MethodBase>() : new MethodBase[] { target });
            }
        }

        private ThirdPartyCompatibilityResult Changed(string reason) =>
            ThirdPartyCompatibilityResult.CreateTargetChanged(ModuleId, DisplayName, PackageId, reason);
    }
}
