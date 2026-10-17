using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.RimTalk
{
    internal sealed class RimTalkCatalystCompatibility : IThirdPartyCompatibilityModule
    {
        private const string EffectTypeName = "RimTalk.Properties.CompTargetEffect_InstallVocalLink";

        public string ModuleId => "RimTalk.CatalystUser";
        public string DisplayName => "RimTalk：机械族机械师使用声音链接催化剂";
        public string PackageId => "cj.rimtalk";

        public ThirdPartyCompatibilityResult Apply(Harmony harmony)
        {
            RimTalkCatalystUserPatch.Enabled = false;
            ModContentPack? mod = ThirdPartyCompatibilityTargetResolver.FindRunningMod(PackageId);
            if (mod == null)
                return ThirdPartyCompatibilityResult.CreateInactive(ModuleId, DisplayName, PackageId);

            MethodInfo? target = null;
            MethodInfo? transpiler = null;
            bool attempted = false;
            try
            {
                if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueInstanceMethod(
                        mod, EffectTypeName, "DoEffectOn", typeof(void),
                        new[] { typeof(Pawn), typeof(Thing) }, out target, out string failure))
                    return ThirdPartyCompatibilityResult.CreateTargetChanged(ModuleId, DisplayName, PackageId, failure);

                if (target == null || !target.IsPublic || target.IsAbstract || target.IsGenericMethod
                    || target.ContainsGenericParameters || !target.IsVirtual
                    || target.GetBaseDefinition().DeclaringType != typeof(CompTargetEffect))
                    return ThirdPartyCompatibilityResult.CreateTargetChanged(
                        ModuleId, DisplayName, PackageId, "催化剂 DoEffectOn 不再是预期的 CompTargetEffect 公开非泛型重写方法。");

                // 安装前验证原始 IL；实际安装时 Transpiler 再校验一次，避免误改其他 Pawn 的判断。
                try
                {
                    RimTalkCatalystUserPatch.Transpiler(PatchProcessor.GetOriginalInstructions(target));
                }
                catch (InvalidOperationException ex)
                {
                    return ThirdPartyCompatibilityResult.CreateTargetChanged(
                        ModuleId, DisplayName, PackageId, ex.Message, target);
                }

                transpiler = AccessTools.DeclaredMethod(typeof(RimTalkCatalystUserPatch),
                    nameof(RimTalkCatalystUserPatch.Transpiler), new[] { typeof(IEnumerable<CodeInstruction>) });
                if (transpiler == null || !transpiler.IsStatic
                    || transpiler.ReturnType != typeof(IEnumerable<CodeInstruction>))
                    throw new MissingMethodException(typeof(RimTalkCatalystUserPatch).FullName, "Transpiler");

                attempted = true;
                harmony.Patch(target, transpiler: new HarmonyMethod(transpiler));
                RimTalkCatalystUserPatch.Enabled = true;
            }
            catch (Exception ex)
            {
                // 即使回滚失败，残留 helper 也只执行上游身份判断。
                RimTalkCatalystUserPatch.Enabled = false;
                if (attempted && target != null && transpiler != null)
                {
                    try { harmony.Unpatch(target, transpiler); }
                    catch { /* 保留安装异常，仅尝试回滚本模块补丁。 */ }
                }
                return ThirdPartyCompatibilityResult.CreateFailed(
                    ModuleId, DisplayName, PackageId,
                    "RimTalk 催化剂使用者兼容安装失败，已关闭新增资格并尝试回滚本模块补丁。",
                    ex, target == null ? Array.Empty<MethodBase>() : new MethodBase[] { target });
            }

            return ThirdPartyCompatibilityResult.CreateApplied(ModuleId, DisplayName, PackageId,
                "已扩展催化剂效果派工的使用者资格；保留目标、安装工作、物品消耗和对话资格。");
        }
    }
}
