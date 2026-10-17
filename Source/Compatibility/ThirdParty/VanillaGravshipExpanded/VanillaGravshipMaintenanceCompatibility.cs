using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.VanillaGravshipExpanded
{
    internal sealed class VanillaGravshipMaintenanceCompatibility : IThirdPartyCompatibilityModule
    {
        private const string WorkGiverTypeName = "VanillaGravshipExpanded.WorkGiver_MaintainGrav";

        public string ModuleId => "VanillaGravshipExpanded.MechMaintenance";
        public string DisplayName => "Vanilla Gravship Expanded：机械族机械师维护重力船";
        public string PackageId => VanillaGravshipLaunchStateCompatibility.ModPackageId;

        public ThirdPartyCompatibilityResult Apply(Harmony harmony)
        {
            VanillaGravshipMaintenancePatch.Enabled = false;
            ModContentPack? mod = ThirdPartyCompatibilityTargetResolver.FindRunningMod(PackageId);
            if (mod == null)
                return ThirdPartyCompatibilityResult.CreateInactive(ModuleId, DisplayName, PackageId);

            MethodInfo? target = null;
            MethodInfo? patch = null;
            bool attempted = false;
            try
            {
                if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueInstanceMethod(
                        mod, WorkGiverTypeName, "HasJobOnThing", typeof(bool),
                        new[] { typeof(Pawn), typeof(Thing), typeof(bool) }, out target, out string failure)
                    || !ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(
                        mod, "VanillaGravshipExpanded.VGEDefOf", out Type? defOf, out failure))
                    return Changed(failure);

                if (target == null || !target.IsPublic || !target.IsVirtual || target.IsAbstract
                    || target.ContainsGenericParameters || target.GetMethodBody() == null
                    || target.GetBaseDefinition().DeclaringType != typeof(WorkGiver_Scanner))
                    return Changed("VGE 维护方法不再是预期的 WorkGiver_Scanner.HasJobOnThing 重写。", target);

                WorkGiverDef? workDef = DefDatabase<WorkGiverDef>.GetNamedSilentFail("VGE_MaintainGrav");
                if (workDef == null || !ReferenceEquals(workDef.modContentPack, mod)
                    || workDef.giverClass != target.DeclaringType || workDef.workType != WorkTypeDefOf.Construction
                    || workDef.requiredCapacities == null
                    || !workDef.requiredCapacities.Contains(PawnCapacityDefOf.Manipulation))
                    return Changed("VGE 维护 WorkGiverDef 的归属、工作类别或操作能力要求已变化。", target);

                if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueField(
                        defOf!, "VGE_Astronaut", typeof(ThingDef),
                        BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly,
                        out FieldInfo? astronautField, out failure))
                    return Changed(failure, target);
                if (astronautField!.GetValue(null) is not ThingDef astronaut
                    || astronaut.defName != "VGE_Astronaut" || !ReferenceEquals(astronaut.modContentPack, mod))
                    return Changed("VGE 宇航机械体 DefOf 未绑定到目标 MOD 的预期 Def。", target);

                VanillaGravshipMaintenancePatch.Configure(workDef, astronautField);
                // 仅在游戏安装模块时检查原始 IL；实际安装时再次检查，结构变化不放宽身份。
                try
                {
                    VanillaGravshipMaintenancePatch.Transpiler(PatchProcessor.GetOriginalInstructions(target));
                }
                catch (InvalidOperationException ex)
                {
                    return Changed(ex.Message, target);
                }

                patch = AccessTools.DeclaredMethod(typeof(VanillaGravshipMaintenancePatch),
                    nameof(VanillaGravshipMaintenancePatch.Transpiler), new[] { typeof(IEnumerable<CodeInstruction>) });
                if (patch == null || !patch.IsStatic || patch.ReturnType != typeof(IEnumerable<CodeInstruction>))
                    throw new MissingMethodException(nameof(VanillaGravshipMaintenancePatch.Transpiler));

                attempted = true;
                harmony.Patch(target, transpiler: new HarmonyMethod(patch));
                VanillaGravshipMaintenancePatch.Enabled = true;
                return ThirdPartyCompatibilityResult.CreateApplied(ModuleId, DisplayName, PackageId,
                    "仅允许具备当前维护工作能力的正式玩家机械师绕过非宇航机械体门槛；保留 VGE 后续全部检查。");
            }
            catch (Exception ex)
            {
                // 即使回滚失败，helper 也恢复原来的 IsColonyMech 判断。
                VanillaGravshipMaintenancePatch.Enabled = false;
                if (attempted && target != null && patch != null)
                {
                    try { harmony.Unpatch(target, patch); }
                    catch { /* 保留原安装异常，只尝试回滚本模块。 */ }
                }
                return ThirdPartyCompatibilityResult.CreateFailed(ModuleId, DisplayName, PackageId,
                    "VGE 维护兼容安装失败，已关闭新增资格并尝试回滚本模块补丁。", ex,
                    target == null ? Array.Empty<MethodBase>() : new MethodBase[] { target });
            }
        }

        private ThirdPartyCompatibilityResult Changed(string reason, MethodInfo? target = null) =>
            ThirdPartyCompatibilityResult.CreateTargetChanged(ModuleId, DisplayName, PackageId, reason,
                target == null ? Array.Empty<MethodBase>() : new MethodBase[] { target });
    }
}
