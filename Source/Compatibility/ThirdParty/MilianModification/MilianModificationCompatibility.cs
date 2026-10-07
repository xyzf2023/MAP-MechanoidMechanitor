using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.MilianModification
{
    internal sealed class MilianModificationCompatibility : IThirdPartyCompatibilityModule
    {
        internal enum Feature
        {
            TransitionAnchor,
            SelfInstallation,
            AbilityCooldown
        }

        private sealed class PatchBinding
        {
            internal readonly MethodInfo Target;
            internal readonly MethodInfo Patch;
            internal readonly Action<IEnumerable<CodeInstruction>> Validate;

            internal PatchBinding(MethodInfo target, MethodInfo patch,
                Action<IEnumerable<CodeInstruction>> validate)
            {
                Target = target;
                Patch = patch;
                Validate = validate;
            }
        }

        private readonly Feature feature;

        internal MilianModificationCompatibility(Feature feature)
        {
            this.feature = feature;
        }

        public string ModuleId => "MilianModification." + feature;
        public string PackageId => "Ancot.MilianModification";
        public string DisplayName => feature switch
        {
            Feature.TransitionAnchor => "Milian Modification：机械族机械师跃迁锚点",
            Feature.SelfInstallation => "Milian Modification：机械族机械师自我安装与拆卸",
            _ => "Milian Modification：安装模块时保护外部技能"
        };

        public ThirdPartyCompatibilityResult Apply(Harmony harmony)
        {
            MilianModificationCompatibilityPatches.SetEnabled(feature, false);
            ModContentPack? mod = ThirdPartyCompatibilityTargetResolver.FindRunningMod(PackageId);
            if (mod == null)
                return ThirdPartyCompatibilityResult.CreateInactive(ModuleId, DisplayName, PackageId);

            List<PatchBinding> bindings = new List<PatchBinding>();
            List<PatchBinding> attempted = new List<PatchBinding>();
            try
            {
                try
                {
                    // 每项功能独立安装；同一功能的全部签名和原始 IL 必须先通过校验。
                    ResolveBindings(mod, bindings);
                    foreach (PatchBinding binding in bindings)
                        binding.Validate(PatchProcessor.GetOriginalInstructions(binding.Target));
                }
                catch (InvalidOperationException ex)
                {
                    return ThirdPartyCompatibilityResult.CreateTargetChanged(
                        ModuleId, DisplayName, PackageId, ex.Message,
                        bindings.Select(binding => (MethodBase)binding.Target).ToArray());
                }

                foreach (PatchBinding binding in bindings)
                {
                    attempted.Add(binding);
                    harmony.Patch(binding.Target, transpiler: new HarmonyMethod(binding.Patch));
                }
                MilianModificationCompatibilityPatches.SetEnabled(feature, true);
            }
            catch (Exception ex)
            {
                // 关闭 helper 后再回滚本功能，残留补丁也会退回上游行为。
                MilianModificationCompatibilityPatches.SetEnabled(feature, false);
                foreach (PatchBinding binding in attempted.AsEnumerable().Reverse())
                {
                    try { harmony.Unpatch(binding.Target, binding.Patch); }
                    catch { /* 保留安装异常，只撤销本功能的补丁。 */ }
                }
                return ThirdPartyCompatibilityResult.CreateFailed(
                    ModuleId, DisplayName, PackageId,
                    "米莉安模块兼容安装失败，已关闭该功能并尝试回滚对应补丁。", ex,
                    bindings.Select(binding => (MethodBase)binding.Target).ToArray());
            }

            return ThirdPartyCompatibilityResult.CreateApplied(
                ModuleId, DisplayName, PackageId, "已启用对应米莉安模块兼容修正。");
        }

        private void ResolveBindings(ModContentPack mod, List<PatchBinding> bindings)
        {
            switch (feature)
            {
                case Feature.TransitionAnchor:
                    ResolveTransition(mod, bindings);
                    break;
                case Feature.SelfInstallation:
                    ResolveSelfInstallation(mod, bindings);
                    break;
                case Feature.AbilityCooldown:
                    ResolveAbilityCooldown(mod, bindings);
                    break;
                default:
                    throw new InvalidOperationException("未知米莉安模块兼容功能。");
            }
        }

        private static void ResolveTransition(ModContentPack mod, List<PatchBinding> bindings)
        {
            MethodInfo selector = ResolveInstanceMethod(mod, "MilianModification.CompAbilityTransitionDrive",
                "AlliedPawnOnMap", typeof(Pawn), new[] { typeof(Map) }, typeof(CompAbilityEffect));

            // 从实际选择方法的 ldftn 找到它引用的谓词，不猜编译器生成的名称或数字后缀。
            MethodInfo[] predicates = PatchProcessor.GetOriginalInstructions(selector)
                .Where(code => code.opcode == OpCodes.Ldftn || code.opcode == OpCodes.Ldvirtftn)
                .Select(code => code.operand as MethodInfo)
                .Where(method => method != null && method.DeclaringType == selector.DeclaringType
                    && !method.IsAbstract && !method.IsGenericMethod && !method.ContainsGenericParameters
                    && method.ReturnType == typeof(bool)
                    && method.GetParameters().Select(parameter => parameter.ParameterType)
                        .SequenceEqual(new[] { typeof(Pawn) }))
                .Cast<MethodInfo>().Distinct().ToArray();
            if (predicates.Length != 1)
                throw new InvalidOperationException(
                    $"跃迁友军选择方法中的 Pawn 谓词预期唯一，实际 {predicates.Length} 个。");

            MilianModificationCompatibilityPatches.ConfigureTransition(predicates[0].IsStatic);
            MethodInfo patch = ResolveTranspiler(
                nameof(MilianModificationCompatibilityPatches.TranspilerTransitionAnchor));
            bindings.Add(new PatchBinding(predicates[0], patch,
                instructions => MilianModificationCompatibilityPatches
                    .TranspilerTransitionAnchor(instructions).ToList()));
        }

        private static void ResolveSelfInstallation(ModContentPack mod, List<PatchBinding> bindings)
        {
            MethodInfo? waitWith = AccessTools.DeclaredMethod(typeof(Toils_General), nameof(Toils_General.WaitWith),
                new[] { typeof(TargetIndex), typeof(int), typeof(bool), typeof(bool), typeof(bool),
                    typeof(TargetIndex), typeof(PathEndMode) });
            if (waitWith == null || !waitWith.IsStatic || waitWith.ReturnType != typeof(Toil))
                throw new InvalidOperationException("原版 WaitWith 的完整签名发生变化。");

            MethodInfo patch = ResolveTranspiler(
                nameof(MilianModificationCompatibilityPatches.TranspilerSelfWait), typeof(MethodBase));
            Dictionary<MethodBase, FieldInfo> captures = new Dictionary<MethodBase, FieldInfo>();
            foreach (string typeName in new[]
            {
                "MilianModification.JobDriver_InstallMilianFitting",
                "MilianModification.JobDriver_UninstallMilianFitting"
            })
            {
                MethodInfo makeToils = ResolveInstanceMethod(mod, typeName, "MakeNewToils",
                    typeof(IEnumerable<Toil>), Type.EmptyTypes, typeof(JobDriver));
                Type driverType = makeToils.DeclaringType!;
                MethodInfo? moveNext = AccessTools.EnumeratorMoveNext(makeToils);
                Type? stateMachine = moveNext?.DeclaringType;
                if (moveNext == null || moveNext.IsStatic || moveNext.IsAbstract || moveNext.IsGenericMethod
                    || moveNext.ReturnType != typeof(bool) || moveNext.GetParameters().Length != 0
                    || stateMachine == null || stateMachine.DeclaringType != driverType
                    || stateMachine.Assembly != driverType.Assembly
                    || !typeof(IEnumerator<Toil>).IsAssignableFrom(stateMachine))
                    throw new InvalidOperationException(typeName + ".MakeNewToils 的状态机无法精确确认。");

                FieldInfo[] fields = stateMachine.GetFields(BindingFlags.Instance | BindingFlags.Public
                        | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(field => field.FieldType == driverType).ToArray();
                if (fields.Length != 1)
                    throw new InvalidOperationException(
                        $"{typeName} 的 JobDriver 捕获字段预期唯一，实际 {fields.Length} 个。");
                captures.Add(moveNext, fields[0]);
                MethodInfo target = moveNext;
                bindings.Add(new PatchBinding(target, patch,
                    instructions => MilianModificationCompatibilityPatches
                        .TranspilerSelfWait(instructions, target).ToList()));
            }
            MilianModificationCompatibilityPatches.ConfigureSelfWait(captures, waitWith);
        }

        private static void ResolveAbilityCooldown(ModContentPack mod, List<PatchBinding> bindings)
        {
            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(mod,
                    "MilianModification.ComponentSlot", out Type? slotType, out string failure)
                || slotType == null || !slotType.IsEnum)
                throw new InvalidOperationException(failure.Length > 0 ? failure : "模块槽位类型不再是枚举。");
            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(mod,
                    "MilianModification.MilianComponentDef", out Type? componentType, out failure)
                || componentType == null || !typeof(ThingDef).IsAssignableFrom(componentType))
                throw new InvalidOperationException(failure.Length > 0 ? failure : "模块定义类型不再继承 ThingDef。");

            MethodInfo target = ResolveInstanceMethod(mod, "MilianModification.CompModification",
                "Notify_InstallPostFinished", typeof(void),
                new[] { typeof(List<>).MakeGenericType(slotType), componentType }, typeof(ThingComp));
            ModContentPack? milira = ThirdPartyCompatibilityTargetResolver.FindRunningMod("Ancot.MiliraRace");
            if (milira == null)
                throw new InvalidOperationException("未找到模块依赖 Ancot.MiliraRace，无法确认技能定义归属。");

            MilianModificationCompatibilityPatches.ConfigureAbilityOwners(milira, mod);
            MethodInfo patch = ResolveTranspiler(
                nameof(MilianModificationCompatibilityPatches.TranspilerAbilityCooldown));
            bindings.Add(new PatchBinding(target, patch,
                instructions => MilianModificationCompatibilityPatches
                    .TranspilerAbilityCooldown(instructions).ToList()));
        }

        private static MethodInfo ResolveInstanceMethod(ModContentPack mod, string typeName, string name,
            Type returnType, Type[] parameters, Type requiredBase)
        {
            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueInstanceMethod(mod, typeName, name,
                    returnType, parameters, out MethodInfo? method, out string failure))
                throw new InvalidOperationException(failure);
            if (method == null || method.IsAbstract || method.IsGenericMethod || method.ContainsGenericParameters
                || !requiredBase.IsAssignableFrom(method.DeclaringType))
                throw new InvalidOperationException(typeName + "." + name + " 的实例方法或继承关系发生变化。");
            return method;
        }

        private static MethodInfo ResolveTranspiler(string name, params Type[] additionalParameters)
        {
            Type[] parameters = new[] { typeof(IEnumerable<CodeInstruction>) }.Concat(additionalParameters).ToArray();
            MethodInfo? patch = AccessTools.DeclaredMethod(typeof(MilianModificationCompatibilityPatches),
                name, parameters);
            if (patch == null || !patch.IsStatic || patch.ReturnType != typeof(IEnumerable<CodeInstruction>))
                throw new MissingMethodException(typeof(MilianModificationCompatibilityPatches).FullName, name);
            return patch;
        }
    }
}
