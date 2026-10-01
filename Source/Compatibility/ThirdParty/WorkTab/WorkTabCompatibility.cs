using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.WorkTab
{
    internal sealed class WorkTabCompatibility : IThirdPartyCompatibilityModule
    {
        private sealed class PatchBinding
        {
            internal readonly MethodInfo Target;
            internal readonly MethodInfo Patch;
            internal readonly MethodInfo? Finalizer;
            internal readonly MethodInfo? Caller;
            internal readonly bool IsScope;

            internal PatchBinding(MethodInfo target, MethodInfo patch, MethodInfo? finalizer = null,
                MethodInfo? caller = null)
            {
                Target = target;
                Patch = patch;
                Finalizer = finalizer;
                Caller = caller;
                IsScope = finalizer != null;
            }
        }

        public string ModuleId => "WorkTab.DetailedMechWork";
        public string DisplayName => "Work Tab：机械族机械师详细工作设置";
        public string PackageId => WorkTabCompatibilityUtility.PackageId;

        public ThirdPartyCompatibilityResult Apply(Harmony harmony)
        {
            ModContentPack? mod = ThirdPartyCompatibilityTargetResolver.FindRunningMod(PackageId);
            if (mod == null)
                return ThirdPartyCompatibilityResult.CreateInactive(ModuleId, DisplayName, PackageId);

            List<PatchBinding> bindings = new List<PatchBinding>();
            List<PatchBinding> attempted = new List<PatchBinding>();
            try
            {
                // 全部方法、组件、字段、上游补丁绑定和原始 IL 均确认后，才开始安装。
                try
                {
                    ResolveBindings(mod, bindings);
                    foreach (PatchBinding binding in bindings.Where(b => !b.IsScope))
                    {
                        IEnumerable<CodeInstruction> original = PatchProcessor.GetOriginalInstructions(binding.Target);
                        if (binding.Patch.DeclaringType == typeof(WorkTabWorkSettingsPatch))
                            WorkTabWorkSettingsPatch.Transpiler(original, binding.Target).ToList();
                        else
                            WorkTabHourlyRefreshPatch.Transpiler(original, binding.Target).ToList();
                    }
                }
                catch (InvalidOperationException ex)
                {
                    WorkTabCompatibilityUtility.Disable();
                    return ThirdPartyCompatibilityResult.CreateTargetChanged(ModuleId, DisplayName, PackageId,
                        ex.Message, bindings.Select(b => (MethodBase)b.Target).ToArray());
                }

                foreach (PatchBinding binding in bindings)
                {
                    attempted.Add(binding);
                    if (binding.IsScope)
                    {
                        // 先捕获嵌套上下文，异常或原方法被跳过时也由 Finalizer 恢复。
                        harmony.Patch(binding.Target,
                            prefix: new HarmonyMethod(binding.Patch) { priority = Priority.First },
                            finalizer: new HarmonyMethod(binding.Finalizer!));
                    }
                    else
                        harmony.Patch(binding.Target, transpiler: new HarmonyMethod(binding.Patch));
                }
                // WorkTab 先于本模块生成了原版方法的 Harmony wrapper。重新组合其现有补丁，
                // 避免短小 Prefix 已被运行时内联时仍执行旧体；不注册额外补丁或改变补丁顺序。
                RefreshCallers(harmony, bindings);
                WorkTabCompatibilityUtility.Enable();
            }
            catch (Exception ex)
            {
                WorkTabCompatibilityUtility.Disable();
                foreach (PatchBinding binding in attempted.AsEnumerable().Reverse())
                {
                    try { harmony.Unpatch(binding.Target, binding.Patch); }
                    catch { /* 保留原始异常，仅撤销本模块补丁。 */ }
                    if (binding.Finalizer != null)
                    {
                        try { harmony.Unpatch(binding.Target, binding.Finalizer); }
                        catch { /* 不移除 WorkTab 或其他 MOD 的补丁。 */ }
                    }
                }
                try { RefreshCallers(harmony, bindings); }
                catch { /* 保留安装失败的初始原因；禁用标志使残留 helper 不再扩展机械师。 */ }
                return ThirdPartyCompatibilityResult.CreateFailed(ModuleId, DisplayName, PackageId,
                    "WorkTab 兼容安装失败，已尝试回滚本模块全部补丁。", ex,
                    bindings.Select(b => (MethodBase)b.Target).ToArray());
            }

            return ThirdPartyCompatibilityResult.CreateApplied(ModuleId, DisplayName, PackageId,
                "已接通正式玩家机械师的父子工作优先级、工作器排序和跨小时刷新；默认授权写入全天，沿用 WorkTab 存档。");
        }

        private static void ResolveBindings(ModContentPack mod, List<PatchBinding> bindings)
        {
            Type extensions = ResolveType(mod, "WorkTab.Pawn_Extensions");
            MethodInfo humanlike = ResolveMethod(extensions, "Humanlike", typeof(bool),
                new[] { typeof(Pawn) }, true);
            Type window = ResolveType(mod, "WorkTab.MainTabWindow_WorkTab");
            MethodInfo selectedHours = ResolveMethod(window, "get_SelectedHours", typeof(List<int>),
                Type.EmptyTypes, true);
            Type priorityManager = ResolveType(mod, "WorkTab.PriorityManager");
            Type favouriteManager = ResolveType(mod, "WorkTab.FavouriteManager");
            if (!typeof(GameComponent).IsAssignableFrom(priorityManager)
                || !typeof(GameComponent).IsAssignableFrom(favouriteManager))
                throw new InvalidOperationException("WorkTab：优先级或收藏管理器不再是 GameComponent。");

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueField(priorityManager, "_instance",
                    priorityManager, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                    out FieldInfo? managerInstance, out string failure))
                throw new InvalidOperationException(failure);
            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueField(typeof(Pawn_WorkSettings), "pawn",
                    typeof(Pawn), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                        | BindingFlags.DeclaredOnly, out FieldInfo? pawnField, out failure))
                throw new InvalidOperationException(failure);

            WorkTabCompatibilityUtility.Configure(priorityManager, favouriteManager,
                managerInstance!, pawnField!, humanlike, selectedHours);

            MethodInfo workSettingsTranspiler = ResolveMethod(typeof(WorkTabWorkSettingsPatch), "Transpiler",
                typeof(IEnumerable<CodeInstruction>),
                new[] { typeof(IEnumerable<CodeInstruction>), typeof(MethodBase) }, true);
            Type settings = typeof(Pawn_WorkSettings);
            AddWorkSettingsBinding(mod, bindings, workSettingsTranspiler,
                "WorkTab.Pawn_WorkSettings_GetPriority", typeof(bool),
                new[] { typeof(WorkTypeDef), settings, typeof(int).MakeByRefType() },
                "GetPriority", typeof(int), new[] { typeof(WorkTypeDef) });
            AddWorkSettingsBinding(mod, bindings, workSettingsTranspiler,
                "WorkTab.Pawn_WorkSettings_SetPriority", typeof(void),
                new[] { settings, typeof(WorkTypeDef), typeof(int).MakeByRefType() },
                "SetPriority", typeof(void), new[] { typeof(WorkTypeDef), typeof(int) });
            AddWorkSettingsBinding(mod, bindings, workSettingsTranspiler,
                "WorkTab.Pawn_WorkSettings_CacheWorkGiversInOrder", typeof(bool),
                new[] { settings, typeof(bool).MakeByRefType(),
                    typeof(List<WorkGiver>).MakeByRefType(), typeof(List<WorkGiver>).MakeByRefType() },
                "CacheWorkGiversInOrder", typeof(void), Type.EmptyTypes);
            AddWorkSettingsBinding(mod, bindings, workSettingsTranspiler,
                "WorkTab.Pawn_WorkSettings_Disable", typeof(void),
                new[] { settings, typeof(WorkTypeDef) },
                "Disable", typeof(void), new[] { typeof(WorkTypeDef) });
            AddWorkSettingsBinding(mod, bindings, workSettingsTranspiler,
                "WorkTab.Pawn_WorkSettings_DisableAll", typeof(void),
                new[] { settings }, "DisableAll", typeof(void), Type.EmptyTypes);

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueInstanceMethod(mod,
                    "WorkTab.MapComponent_TimeKeeper", "MapComponentTick", typeof(void), Type.EmptyTypes,
                    out MethodInfo? tick, out failure)
                || !typeof(MapComponent).IsAssignableFrom(tick!.DeclaringType))
                throw new InvalidOperationException(string.IsNullOrEmpty(failure)
                    ? "WorkTab：小时刷新入口不再属于 MapComponent。" : failure);
            bindings.Add(new PatchBinding(tick!, ResolveMethod(typeof(WorkTabHourlyRefreshPatch), "Transpiler",
                typeof(IEnumerable<CodeInstruction>),
                new[] { typeof(IEnumerable<CodeInstruction>), typeof(MethodBase) }, true)));

            // 默认设置均来自已存在的生命周期/首次授权入口，不改变其调用时机和首次接入记录。
            MethodInfo scopePrefix = ResolveMethod(typeof(WorkTabDefaultPriorityWritePatch), "Prefix",
                typeof(void), new[] { typeof(Pawn), typeof(int).MakeByRefType() }, true);
            MethodInfo scopeFinalizer = ResolveMethod(typeof(WorkTabDefaultPriorityWritePatch), "Finalizer",
                typeof(Exception), new[] { typeof(Exception), typeof(int) }, true);
            AddDefaultWriteScope(bindings, typeof(MechWorkSettingsUtility),
                "SynchronizeGeneralWorkSettings", scopePrefix, scopeFinalizer);
            AddDefaultWriteScope(bindings, typeof(WardenWorkUtility),
                "TryInitializeDefaultWardenPriority", scopePrefix, scopeFinalizer);
            AddDefaultWriteScope(bindings, typeof(AnimalHandlingWorkUtility),
                "TryInitializeDefaultHandlingPriority", scopePrefix, scopeFinalizer);
            AddDefaultWriteScope(bindings, typeof(MechanicalChildcareUtility),
                "TryInitializeDefaultChildcarePriority", scopePrefix, scopeFinalizer);

            MethodInfo initialization = ResolveMethod(settings, "EnableAndInitialize", typeof(void),
                Type.EmptyTypes, false);
            MethodInfo initializationPrefix = ResolveMethod(typeof(WorkTabDefaultPriorityWritePatch),
                "InitializationPrefix", typeof(void), new[] { typeof(Pawn), typeof(int).MakeByRefType() }, true);
            bindings.Add(new PatchBinding(initialization, initializationPrefix, scopeFinalizer));
        }

        private static void AddWorkSettingsBinding(ModContentPack mod, List<PatchBinding> bindings,
            MethodInfo transpiler, string typeName, Type prefixReturnType, Type[] prefixParameters,
            string originalName, Type originalReturnType, Type[] originalParameters)
        {
            MethodInfo prefix = ResolveMethod(ResolveType(mod, typeName), "Prefix",
                prefixReturnType, prefixParameters, true);
            MethodInfo original = ResolveMethod(typeof(Pawn_WorkSettings), originalName,
                originalReturnType, originalParameters, false);
            if (Harmony.GetPatchInfo(original)?.Prefixes.Any(p => p.PatchMethod == prefix) != true)
                throw new InvalidOperationException(
                    $"WorkTab：{typeName}.Prefix 未绑定到 Pawn_WorkSettings.{originalName}，拒绝安装部分兼容。");
            bindings.Add(new PatchBinding(prefix, transpiler, caller: original));
        }

        private static void RefreshCallers(Harmony harmony, IEnumerable<PatchBinding> bindings)
        {
            foreach (MethodInfo caller in bindings.Where(b => b.Caller != null).Select(b => b.Caller!).Distinct())
                new PatchProcessor(harmony, caller).Patch();
        }

        private static void AddDefaultWriteScope(List<PatchBinding> bindings, Type type,
            string methodName, MethodInfo prefix, MethodInfo finalizer) =>
            bindings.Add(new PatchBinding(ResolveMethod(type, methodName, typeof(void),
                new[] { typeof(Pawn) }, true), prefix, finalizer));

        private static Type ResolveType(ModContentPack mod, string fullName)
        {
            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(mod, fullName,
                    out Type? type, out string failure))
                throw new InvalidOperationException(failure);
            return type!;
        }

        private static MethodInfo ResolveMethod(Type type, string name, Type returnType,
            Type[] parameters, bool isStatic)
        {
            MethodInfo[] matches = type.GetMethods(BindingFlags.Static | BindingFlags.Instance
                    | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(m => m.Name == name && m.IsStatic == isStatic && !m.ContainsGenericParameters
                    && m.ReturnType == returnType
                    && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameters))
                .ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException(
                    $"WorkTab：{type.FullName}.{name} 的完整签名预期唯一，实际找到 {matches.Length} 个。");
            return matches[0];
        }
    }
}
