using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.MiliraRace
{
    internal sealed class MiliraButcheryCompatibility : IThirdPartyCompatibilityModule
    {
        public string ModuleId => "MiliraRace.Butchery";
        public string DisplayName => "Milira Race：机械族机械师拆解米莉安";
        public string PackageId => "Ancot.MiliraRace";

        public ThirdPartyCompatibilityResult Apply(Harmony harmony)
        {
            MiliraButcheryCompatibilityPatches.Enabled = false;
            ModContentPack? mod = ThirdPartyCompatibilityTargetResolver.FindRunningMod(PackageId);
            if (mod == null)
                return ThirdPartyCompatibilityResult.CreateInactive(ModuleId, DisplayName, PackageId);

            MethodInfo? target = null;
            MethodInfo? patch = null;
            bool attempted = false;
            try
            {
                try
                {
                    if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(mod,
                            "Milira.Milira_CorpseButch_Patch", out Type? type, out string failure))
                        throw new InvalidOperationException(failure);
                    Type[] parameters = { typeof(IEnumerable<Thing>), typeof(Corpse), typeof(Pawn), typeof(float) };
                    MethodInfo[] postfixes = type!.GetMethods(BindingFlags.Static | BindingFlags.Public
                            | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                        .Where(method => method.Name == "Postfix" && !method.IsGenericMethod
                            && !method.ContainsGenericParameters && !method.IsAbstract
                            && method.ReturnType == typeof(IEnumerable<Thing>)
                            && method.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameters)).ToArray();
                    if (postfixes.Length != 1)
                        throw new InvalidOperationException($"米莉安拆解产物 Postfix 完整签名预期唯一，实际 {postfixes.Length} 个。");

                    target = AccessTools.EnumeratorMoveNext(postfixes[0]);
                    Type? stateMachine = target?.DeclaringType;
                    if (target == null || target.IsStatic || target.IsAbstract || target.IsGenericMethod
                        || target.ReturnType != typeof(bool) || target.GetParameters().Length != 0
                        || stateMachine == null || stateMachine.DeclaringType != type
                        || stateMachine.Assembly != type.Assembly
                        || !typeof(IEnumerator<Thing>).IsAssignableFrom(stateMachine))
                        throw new InvalidOperationException("米莉安拆解产物迭代器无法精确确认。");

                    patch = AccessTools.DeclaredMethod(typeof(MiliraButcheryCompatibilityPatches),
                        nameof(MiliraButcheryCompatibilityPatches.Transpiler),
                        new[] { typeof(IEnumerable<CodeInstruction>) });
                    if (patch == null || !patch.IsStatic || patch.ReturnType != typeof(IEnumerable<CodeInstruction>))
                        throw new InvalidOperationException("无法解析米莉安拆解产物兼容 Transpiler。");

                    MiliraButcheryCompatibilityPatches.Transpiler(
                        PatchProcessor.GetOriginalInstructions(target)).ToList();
                }
                catch (InvalidOperationException ex)
                {
                    return ThirdPartyCompatibilityResult.CreateTargetChanged(ModuleId, DisplayName, PackageId,
                        ex.Message, target == null ? Array.Empty<MethodBase>() : new MethodBase[] { target });
                }

                attempted = true;
                harmony.Patch(target, transpiler: new HarmonyMethod(patch));
                MiliraButcheryCompatibilityPatches.Enabled = true;
                return ThirdPartyCompatibilityResult.CreateApplied(ModuleId, DisplayName, PackageId,
                    "正式机械师拆解米莉安时，使用上游制作技能曲线计算额外产物概率。");
            }
            catch (Exception ex)
            {
                MiliraButcheryCompatibilityPatches.Enabled = false;
                if (attempted && target != null && patch != null)
                {
                    try { harmony.Unpatch(target, patch); }
                    catch { /* 保留原始异常，只回滚本模块。 */ }
                }
                return ThirdPartyCompatibilityResult.CreateFailed(ModuleId, DisplayName, PackageId,
                    "米莉安拆解产物兼容安装失败，已关闭修正并尝试回滚。", ex,
                    target == null ? Array.Empty<MethodBase>() : new MethodBase[] { target });
            }
        }
    }
}
