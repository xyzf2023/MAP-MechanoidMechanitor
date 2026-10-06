using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.PerspectiveShift
{
    // 各功能独立验证、启用和回滚；只共享目标解析与显式安装流程。
    internal abstract class PerspectiveShiftCompatibilityModule : IThirdPartyCompatibilityModule
    {
        public abstract string ModuleId { get; }
        public abstract string DisplayName { get; }
        public string PackageId => "ferny.PerspectiveShift";
        protected abstract string AppliedDetail { get; }
        protected abstract void SetEnabled(bool enabled);
        protected abstract void Resolve(ModContentPack mod, List<Binding> bindings);

        protected sealed class Binding
        {
            internal readonly MethodInfo Target;
            internal readonly MethodInfo Patch;
            internal readonly HarmonyPatchType Kind;
            internal Binding(MethodInfo target, Type patchType, string patchName, HarmonyPatchType kind)
            {
                Target = target;
                MethodInfo[] matches = patchType.GetMethods(BindingFlags.Static | BindingFlags.Public
                        | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(m => m.Name == patchName && !m.IsGenericMethod).ToArray();
                if (matches.Length != 1)
                    throw new MissingMethodException(patchType.FullName, patchName);
                Patch = matches[0];
                Kind = kind;
            }
        }

        public ThirdPartyCompatibilityResult Apply(Harmony harmony)
        {
            SetEnabled(false);
            ModContentPack? mod = ThirdPartyCompatibilityTargetResolver.FindRunningMod(PackageId);
            if (mod == null)
                return ThirdPartyCompatibilityResult.CreateInactive(ModuleId, DisplayName, PackageId);

            var bindings = new List<Binding>();
            var attempted = new List<Binding>();
            try
            {
                try
                {
                    PerspectiveShiftRuntime.Resolve(mod);
                    Resolve(mod, bindings);
                    // 安装前检查原始 IL；Transpiler 安装时会再次检查实际输入。
                    foreach (Binding binding in bindings.Where(b => b.Kind == HarmonyPatchType.Transpiler))
                    {
                        var rewritten = (IEnumerable<CodeInstruction>)binding.Patch.Invoke(null,
                            new object[] { PatchProcessor.GetOriginalInstructions(binding.Target) })!;
                        rewritten.ToList();
                    }
                }
                catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException)
                {
                    return ThirdPartyCompatibilityResult.CreateTargetChanged(ModuleId, DisplayName,
                        PackageId, ex.InnerException!.Message, bindings.Select(b => (MethodBase)b.Target).ToArray());
                }
                catch (InvalidOperationException ex)
                {
                    return ThirdPartyCompatibilityResult.CreateTargetChanged(ModuleId, DisplayName,
                        PackageId, ex.Message, bindings.Select(b => (MethodBase)b.Target).ToArray());
                }

                foreach (Binding binding in bindings)
                {
                    attempted.Add(binding);
                    var patch = new HarmonyMethod(binding.Patch);
                    if (binding.Kind == HarmonyPatchType.Prefix)
                        harmony.Patch(binding.Target, prefix: patch);
                    else if (binding.Kind == HarmonyPatchType.Postfix)
                        harmony.Patch(binding.Target, postfix: patch);
                    else if (binding.Kind == HarmonyPatchType.Transpiler)
                        harmony.Patch(binding.Target, transpiler: patch);
                    else
                        throw new InvalidOperationException("Perspective Shift：不支持的补丁种类。");
                }
                SetEnabled(true);
            }
            catch (Exception ex)
            {
                SetEnabled(false);
                for (int i = attempted.Count - 1; i >= 0; i--)
                {
                    try { harmony.Unpatch(attempted[i].Target, attempted[i].Patch); }
                    catch { /* 只撤销本模块，保留原始安装异常。 */ }
                }
                return ThirdPartyCompatibilityResult.CreateFailed(ModuleId, DisplayName, PackageId,
                    "Perspective Shift 兼容安装失败，已关闭修正并尝试回滚本模块补丁。", ex,
                    bindings.Select(b => (MethodBase)b.Target).ToArray());
            }
            return ThirdPartyCompatibilityResult.CreateApplied(ModuleId, DisplayName, PackageId, AppliedDetail);
        }

        internal static Type ResolveType(ModContentPack mod, string name)
        {
            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(mod, "PerspectiveShift." + name,
                    out Type? type, out string failure))
                throw new InvalidOperationException(failure);
            return type!;
        }

        internal static MethodInfo Method(Type type, string name, bool isStatic, Type result, params Type[] parameters)
        {
            MethodInfo[] matches = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly | (isStatic ? BindingFlags.Static : BindingFlags.Instance))
                .Where(m => m.Name == name && m.IsStatic == isStatic && !m.IsGenericMethod
                    && !m.ContainsGenericParameters && !m.IsAbstract && m.ReturnType == result
                    && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameters)).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException($"Perspective Shift：{type.FullName}.{name} 完整签名预期唯一，实际 {matches.Length} 个。");
            return matches[0];
        }

        internal static FieldInfo Field(Type type, string name, Type fieldType, bool isStatic = false)
        {
            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueField(type, name, fieldType,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly
                    | (isStatic ? BindingFlags.Static : BindingFlags.Instance), out FieldInfo? field, out string failure))
                throw new InvalidOperationException(failure);
            return field!;
        }

        internal static void RequirePatch(MethodInfo original, MethodInfo patch, HarmonyPatchType kind)
        {
            Patches? info = Harmony.GetPatchInfo(original);
            bool found = kind == HarmonyPatchType.Prefix
                ? info?.Prefixes.Any(p => p.owner == "PerspectiveShiftMod" && p.PatchMethod == patch) == true
                : info?.Postfixes.Any(p => p.owner == "PerspectiveShiftMod" && p.PatchMethod == patch) == true;
            if (!found)
                throw new InvalidOperationException($"Perspective Shift：{patch.DeclaringType?.FullName}.{patch.Name} 不再绑定预期原版入口。");
        }
    }
}
