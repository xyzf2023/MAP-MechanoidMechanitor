using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.MiliraRace
{
    internal sealed class MiliraAutoSuitCompatibility : IThirdPartyCompatibilityModule
    {
        public string ModuleId => "MiliraRace.AutoSuit";
        public string DisplayName => "Milira Race：机械族机械师自动为米莉安换装";
        public string PackageId => "Ancot.MiliraRace";

        public ThirdPartyCompatibilityResult Apply(Harmony harmony)
        {
            MiliraAutoSuitCompatibilityPatch.Enabled = false;
            ModContentPack? mod = ThirdPartyCompatibilityTargetResolver.FindRunningMod(PackageId);
            if (mod == null)
                return ThirdPartyCompatibilityResult.CreateInactive(ModuleId, DisplayName, PackageId);

            MethodInfo? target = null;
            MethodInfo? transpiler = null;
            bool attempted = false;
            try
            {
                try
                {
                    if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueInstanceMethod(
                            mod, "Milira.WorkGiver_AutoSuitMilian", "HasJob", typeof(bool),
                            new[] { typeof(Pawn), typeof(Thing), typeof(bool) }, out target, out string failure))
                        throw new InvalidOperationException(failure);
                    if (target == null || !target.IsPublic || target.IsAbstract || target.IsGenericMethod
                        || target.ContainsGenericParameters || !typeof(WorkGiver_Scanner).IsAssignableFrom(target.DeclaringType))
                        throw new InvalidOperationException("米莉安自动换装 HasJob 不再是预期的公开工作检查方法。");

                    transpiler = AccessTools.DeclaredMethod(typeof(MiliraAutoSuitCompatibilityPatch),
                        nameof(MiliraAutoSuitCompatibilityPatch.Transpiler),
                        new[] { typeof(IEnumerable<CodeInstruction>) });
                    if (transpiler == null || !transpiler.IsStatic
                        || transpiler.ReturnType != typeof(IEnumerable<CodeInstruction>))
                        throw new InvalidOperationException("无法解析米莉安自动换装兼容 Transpiler。");

                    // 安装前核对原始指令；Harmony 后续重建时仍由 Transpiler 再次校验。
                    MiliraAutoSuitCompatibilityPatch.Transpiler(
                        PatchProcessor.GetOriginalInstructions(target)).ToList();
                }
                catch (InvalidOperationException ex)
                {
                    return ThirdPartyCompatibilityResult.CreateTargetChanged(
                        ModuleId, DisplayName, PackageId, ex.Message,
                        target == null ? Array.Empty<MethodBase>() : new MethodBase[] { target });
                }

                attempted = true;
                harmony.Patch(target!, transpiler: new HarmonyMethod(transpiler));
                MiliraAutoSuitCompatibilityPatch.Enabled = true;
            }
            catch (Exception ex)
            {
                MiliraAutoSuitCompatibilityPatch.Enabled = false;
                if (attempted && target != null && transpiler != null)
                {
                    try { harmony.Unpatch(target, transpiler); }
                    catch { /* 保留原始异常，只回滚本模块。 */ }
                }
                return ThirdPartyCompatibilityResult.CreateFailed(
                    ModuleId, DisplayName, PackageId,
                    "米莉安自动换装兼容安装失败，已关闭修正并尝试回滚本模块补丁。", ex,
                    target == null ? Array.Empty<MethodBase>() : new MethodBase[] { target });
            }

            return ThirdPartyCompatibilityResult.CreateApplied(
                ModuleId, DisplayName, PackageId,
                "已扩展正式玩家机械族机械师的自动换装工作资格，保留上游目标与工作条件。");
        }
    }
}
