using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.MiliraRace
{
    internal sealed class MiliraEquipmentCompatibility : IThirdPartyCompatibilityModule
    {
        private const string DressDriverTypeName = "Milira.JobDriver_DressMilian";

        private sealed class PatchBinding
        {
            internal readonly MethodInfo Target;
            internal readonly MethodInfo Patch;

            internal PatchBinding(MethodInfo target, MethodInfo patch)
            {
                Target = target;
                Patch = patch;
            }
        }

        public string ModuleId => "MiliraRace.Equipment";
        public string DisplayName => "Milira Race：机械族机械师为米莉安换装";
        public string PackageId => "Ancot.MiliraRace";

        public ThirdPartyCompatibilityResult Apply(Harmony harmony)
        {
            MiliraEquipmentCompatibilityPatches.Enabled = false;
            ModContentPack? mod = ThirdPartyCompatibilityTargetResolver.FindRunningMod(PackageId);
            if (mod == null)
                return ThirdPartyCompatibilityResult.CreateInactive(ModuleId, DisplayName, PackageId);

            List<PatchBinding> bindings = new List<PatchBinding>();
            List<PatchBinding> attempted = new List<PatchBinding>();
            try
            {
                try
                {
                    // 全部签名、状态机捕获字段与原始 IL 均确认后，才安装任何补丁。
                    ResolveBindings(mod, bindings);
                    foreach (PatchBinding binding in bindings)
                    {
                        IEnumerable<CodeInstruction> original = PatchProcessor.GetOriginalInstructions(binding.Target);
                        if (binding.Patch.Name == nameof(MiliraEquipmentCompatibilityPatches.TranspilerDoEffectOn))
                            MiliraEquipmentCompatibilityPatches.TranspilerDoEffectOn(original).ToList();
                        else
                            MiliraEquipmentCompatibilityPatches.TranspilerDressWait(original).ToList();
                    }
                }
                catch (InvalidOperationException ex)
                {
                    return ThirdPartyCompatibilityResult.CreateTargetChanged(
                        ModuleId, DisplayName, PackageId, ex.Message,
                        bindings.Select(binding => (MethodBase)binding.Target).ToArray());
                }

                foreach (PatchBinding binding in bindings)
                {
                    // 将安装尝试也纳入回滚，覆盖 Harmony 在生成 wrapper 时抛出的异常。
                    attempted.Add(binding);
                    harmony.Patch(binding.Target, transpiler: new HarmonyMethod(binding.Patch));
                }
                MiliraEquipmentCompatibilityPatches.Enabled = true;
            }
            catch (Exception ex)
            {
                // 先关闭新增资格与等待修正；残留 helper 也会退回上游行为。
                MiliraEquipmentCompatibilityPatches.Enabled = false;
                foreach (PatchBinding binding in attempted.AsEnumerable().Reverse())
                {
                    try { harmony.Unpatch(binding.Target, binding.Patch); }
                    catch { /* 保留原始异常，仅撤销本模块补丁。 */ }
                }
                return ThirdPartyCompatibilityResult.CreateFailed(
                    ModuleId, DisplayName, PackageId,
                    "Milira 换装兼容安装失败，已关闭修正并尝试回滚本模块补丁。", ex,
                    bindings.Select(binding => (MethodBase)binding.Target).ToArray());
            }

            return ThirdPartyCompatibilityResult.CreateApplied(
                ModuleId, DisplayName, PackageId,
                "已扩展两个换装入口的操作者资格，并处理正式机械师米莉安的自我穿戴等待；保留上游目标与装备流程。");
        }

        private static void ResolveBindings(ModContentPack mod, List<PatchBinding> bindings)
        {
            MethodInfo effectPatch = ResolveTranspiler(nameof(MiliraEquipmentCompatibilityPatches.TranspilerDoEffectOn));
            Type[] effectParameters = { typeof(Pawn), typeof(Thing) };
            foreach (string typeName in new[]
            {
                "Milira.CompTargetEffect_EquipMilian",
                "Milira.CompTargetEffect_DressMilian"
            })
            {
                MethodInfo effect = ResolveInstanceMethod(mod, typeName, "DoEffectOn", typeof(void), effectParameters);
                if (!effect.IsPublic || !typeof(CompTargetEffect).IsAssignableFrom(effect.DeclaringType))
                    throw new InvalidOperationException($"{typeName}.DoEffectOn 不再是 CompTargetEffect 的公开实例方法。");
                bindings.Add(new PatchBinding(effect, effectPatch));
            }

            MethodInfo makeToils = ResolveInstanceMethod(mod, DressDriverTypeName, "MakeNewToils",
                typeof(IEnumerable<Toil>), Type.EmptyTypes);
            Type driverType = makeToils.DeclaringType!;
            if (!typeof(JobDriver).IsAssignableFrom(driverType))
                throw new InvalidOperationException($"{DressDriverTypeName} 不再继承 JobDriver。");

            MethodInfo? moveNext = AccessTools.EnumeratorMoveNext(makeToils);
            Type? stateMachine = moveNext?.DeclaringType;
            if (moveNext == null || moveNext.IsStatic || moveNext.IsAbstract || moveNext.IsGenericMethod
                || moveNext.ReturnType != typeof(bool) || moveNext.GetParameters().Length != 0
                || stateMachine == null || stateMachine.DeclaringType != driverType
                || stateMachine.Assembly != driverType.Assembly
                || !typeof(IEnumerator<Toil>).IsAssignableFrom(stateMachine))
                throw new InvalidOperationException($"{DressDriverTypeName}.MakeNewToils 的迭代器 MoveNext 无法精确确认。");

            // 只按精确字段类型匹配，不依赖编译器生成字段的名称或数字后缀。
            FieldInfo[] captures = stateMachine.GetFields(BindingFlags.Instance | BindingFlags.Public
                    | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(field => field.FieldType == driverType).ToArray();
            if (captures.Length != 1)
                throw new InvalidOperationException($"米莉安穿戴迭代器的 JobDriver 捕获字段预期唯一，实际 {captures.Length} 个。");

            MethodInfo? waitWith = AccessTools.DeclaredMethod(typeof(Toils_General), nameof(Toils_General.WaitWith),
                new[] { typeof(TargetIndex), typeof(int), typeof(bool), typeof(bool), typeof(bool),
                    typeof(TargetIndex), typeof(PathEndMode) });
            if (waitWith == null || !waitWith.IsStatic || waitWith.ReturnType != typeof(Toil))
                throw new InvalidOperationException("原版 Toils_General.WaitWith 的完整签名发生变化。");

            MiliraEquipmentCompatibilityPatches.Configure(captures[0], waitWith);
            bindings.Add(new PatchBinding(moveNext,
                ResolveTranspiler(nameof(MiliraEquipmentCompatibilityPatches.TranspilerDressWait))));
        }

        private static MethodInfo ResolveInstanceMethod(ModContentPack mod, string typeName, string methodName,
            Type returnType, Type[] parameters)
        {
            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueInstanceMethod(mod, typeName, methodName,
                    returnType, parameters, out MethodInfo? method, out string failure))
                throw new InvalidOperationException(failure);
            if (method == null || method.IsAbstract || method.IsGenericMethod || method.ContainsGenericParameters)
                throw new InvalidOperationException($"{typeName}.{methodName} 不再是可补丁的非泛型实例方法。");
            return method;
        }

        private static MethodInfo ResolveTranspiler(string name)
        {
            MethodInfo? patch = AccessTools.DeclaredMethod(typeof(MiliraEquipmentCompatibilityPatches), name,
                new[] { typeof(IEnumerable<CodeInstruction>) });
            if (patch == null || !patch.IsStatic || patch.ReturnType != typeof(IEnumerable<CodeInstruction>))
                throw new MissingMethodException(typeof(MiliraEquipmentCompatibilityPatches).FullName, name);
            return patch;
        }
    }
}
