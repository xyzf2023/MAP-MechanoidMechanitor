using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.RPGStyleInventory
{
    internal sealed class RPGInventoryBinding
    {
        internal readonly MethodInfo Target;
        internal readonly MethodInfo Patch;
        internal readonly HarmonyPatchType Kind;
        internal RPGInventoryBinding(MethodInfo target, MethodInfo patch, HarmonyPatchType kind)
        { Target = target; Patch = patch; Kind = kind; }
    }

    /// <summary>两个 PackageId、两组目标签名分别校验；核心和布局独立安装与回滚。</summary>
    internal sealed class RPGInventoryCompatibility : IThirdPartyCompatibilityModule
    {
        private readonly bool revamped;
        private readonly bool layout;
        internal RPGInventoryCompatibility(bool revamped, bool layout = false)
        { this.revamped = revamped; this.layout = layout; }
        public string PackageId => revamped ? RPGInventoryRuntime.RevampedPackageId : RPGInventoryRuntime.OriginalPackageId;
        public string ModuleId => (revamped ? "RPGInventory.Revamped" : "RPGInventory.Original") + (layout ? ".Layout" : ".Equipment");
        public string DisplayName => (revamped ? "RPG 装备栏 Revamped" : "RPG 装备栏原版")
            + (layout ? "：机械体槽位与动态页签" : "：服装、武器与专武操作");

        public ThirdPartyCompatibilityResult Apply(Harmony harmony)
        {
            ModContentPack? mod = ThirdPartyCompatibilityTargetResolver.FindRunningMod(PackageId);
            if (mod == null) return ThirdPartyCompatibilityResult.CreateInactive(ModuleId, DisplayName, PackageId);
            if (ThirdPartyCompatibilityTargetResolver.FindRunningMod(revamped
                    ? RPGInventoryRuntime.OriginalPackageId : RPGInventoryRuntime.RevampedPackageId) != null)
                return ThirdPartyCompatibilityResult.CreateTargetChanged(ModuleId, DisplayName, PackageId,
                    "RPG 原版与 Revamped 同时加载，无法安全选择同名装备页，已跳过。");

            List<RPGInventoryBinding> bindings = new List<RPGInventoryBinding>();
            List<RPGInventoryBinding> attempted = new List<RPGInventoryBinding>();
            RPGInventoryContext? context = null;
            try
            {
                try
                {
                    if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(mod, RPGInventoryRuntime.TabTypeName,
                            out Type? type, out string failure))
                        throw new InvalidOperationException(failure);
                    if (!typeof(ITab_Pawn_Gear).IsAssignableFrom(type) || type!.IsAbstract)
                        throw new InvalidOperationException("RPG 装备页不再是可实例化的 ITab_Pawn_Gear 子类。");
                    if (layout)
                    {
                        context = RPGInventoryRuntime.For(type);
                        if (context?.Enabled != true)
                            throw new InvalidOperationException("装备操作模块未成功安装，不启用依赖它的机械体槽位。");
                        context.LayoutEnabled = false;
                        ResolveLayout(mod, context, bindings);
                        // 先构造上游共享实例，失败时保留默认动态页签。
                        InspectTabManager.GetSharedInstance(type);
                    }
                    else
                    {
                        FieldInfo? cachedPawn = null;
                        if (revamped && !ThirdPartyCompatibilityTargetResolver.TryResolveUniqueField(type,
                                "cachedPawn", typeof(Pawn), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                                out cachedPawn, out failure))
                            throw new InvalidOperationException(failure);
                        context = new RPGInventoryContext(type, revamped,
                            Resolve(mod, "get_CanControl", typeof(bool)),
                            Resolve(mod, "get_CanControlColonist", typeof(bool)),
                            Resolve(mod, "get_SelPawnForGear", typeof(Pawn)),
                            Resolve(mod, "InterfaceDrop", typeof(void), typeof(Thing)), cachedPawn);
                        RPGInventoryRuntime.Register(context);
                        ResolveCore(mod, context, bindings);
                    }
                    foreach (RPGInventoryBinding binding in bindings)
                        if (binding.Kind == HarmonyPatchType.Transpiler)
                            RPGInventoryPatches.Transpiler(PatchProcessor.GetOriginalInstructions(binding.Target), binding.Target).ToList();
                }
                catch (InvalidOperationException ex)
                {
                    return ThirdPartyCompatibilityResult.CreateTargetChanged(ModuleId, DisplayName, PackageId,
                        ex.Message, bindings.Select(binding => (MethodBase)binding.Target).ToArray());
                }

                foreach (RPGInventoryBinding binding in bindings)
                {
                    attempted.Add(binding);
                    Install(harmony, binding);
                }
                if (layout)
                {
                    ColonistLikeGearTabProvider.Register(context!.TabType);
                    context.LayoutEnabled = true;
                }
                else context!.Enabled = true;
            }
            catch (Exception ex)
            {
                if (context != null)
                {
                    if (layout)
                    {
                        context.LayoutEnabled = false;
                        ColonistLikeGearTabProvider.Unregister(context.TabType);
                    }
                    else context.Enabled = false;
                }
                Rollback(harmony, attempted);
                return ThirdPartyCompatibilityResult.CreateFailed(ModuleId, DisplayName, PackageId,
                    "已关闭本模块修正并尝试仅回滚本模块补丁。", ex,
                    bindings.Select(binding => (MethodBase)binding.Target).ToArray());
            }
            return ThirdPartyCompatibilityResult.CreateApplied(ModuleId, DisplayName, PackageId,
                layout ? "已接入机械体槽位、空服装区域与动态补页。"
                    : "已接入现有装备权限与专武保护；Revamped 丢弃复用原版带宽确认，服用资格保持原有范围。");
        }

        private void ResolveCore(ModContentPack mod, RPGInventoryContext context, List<RPGInventoryBinding> bindings)
        {
            Add(bindings, context.CanControl, nameof(RPGInventoryPatches.CanControlPostfix), HarmonyPatchType.Postfix);
            AddTranspiler(bindings, context.CanControlColonist, RPGInventoryPatchKind.Control);
            AddTranspiler(bindings, Resolve(mod, "DrawThingRow", typeof(void),
                typeof(float).MakeByRefType(), typeof(float), typeof(Thing), typeof(bool)), RPGInventoryPatchKind.Row);
            Add(bindings, context.Drop, nameof(RPGInventoryPatches.DropPrefix), HarmonyPatchType.Prefix);
            Add(bindings, Resolve(mod, "ShouldShowInventory", typeof(bool), typeof(Pawn)),
                nameof(RPGInventoryPatches.InventoryPrefix), HarmonyPatchType.Prefix);
            if (revamped)
            {
                MethodInfo popup = Resolve(mod, "PopupMenu", typeof(List<FloatMenuOption>), typeof(Pawn), typeof(Thing), typeof(bool));
                RPGInventoryPatches.ValidateMenu(popup);
                Add(bindings, popup, nameof(RPGInventoryPatches.PopupPostfix), HarmonyPatchType.Postfix);
                AddTranspiler(bindings, Resolve(mod, "DrawSlotIcons", typeof(void), typeof(Thing), typeof(bool),
                    typeof(bool), typeof(Rect), typeof(float), typeof(float)), RPGInventoryPatchKind.Icons);
            }
            else
                AddTranspiler(bindings, Resolve(mod, "DrawThingRow_RPG", typeof(void), typeof(Rect), typeof(Thing), typeof(bool)),
                    RPGInventoryPatchKind.OriginalSlot);
        }

        private void ResolveLayout(ModContentPack mod, RPGInventoryContext context, List<RPGInventoryBinding> bindings)
        {
            Add(bindings, Resolve(mod, "ShouldShowApparel", typeof(bool), typeof(Pawn)),
                nameof(RPGInventoryPatches.ApparelPostfix), HarmonyPatchType.Postfix);
            if (revamped)
                Add(bindings, Resolve(mod, "CanShowSlots", typeof(bool)), nameof(RPGInventoryPatches.SlotsPostfix), HarmonyPatchType.Postfix);
            else
                AddTranspiler(bindings, Resolve(mod, "FillTab", typeof(void)), RPGInventoryPatchKind.OriginalLayout);
        }

        private MethodInfo Resolve(ModContentPack mod, string name, Type result, params Type[] parameters)
        {
            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueInstanceMethod(mod, RPGInventoryRuntime.TabTypeName,
                    name, result, parameters, out MethodInfo? method, out string failure)
                || method == null || method.IsAbstract || method.IsGenericMethod || method.ContainsGenericParameters)
                throw new InvalidOperationException(failure.Length > 0 ? failure : name + " 不是可补丁的普通实例方法。");
            return method;
        }

        internal static void Add(List<RPGInventoryBinding> bindings, MethodInfo target, string patch, HarmonyPatchType kind,
            Type? patchType = null)
        {
            MethodInfo method = AccessTools.DeclaredMethod(patchType ?? typeof(RPGInventoryPatches), patch)
                ?? throw new MissingMethodException(patch);
            bindings.Add(new RPGInventoryBinding(target, method, kind));
        }

        private static void AddTranspiler(List<RPGInventoryBinding> bindings, MethodInfo target, RPGInventoryPatchKind kind)
        {
            RPGInventoryPatches.Configure(target, kind);
            Add(bindings, target, nameof(RPGInventoryPatches.Transpiler), HarmonyPatchType.Transpiler);
        }

        internal static void Install(Harmony harmony, RPGInventoryBinding binding)
        {
            HarmonyMethod patch = new HarmonyMethod(binding.Patch);
            switch (binding.Kind)
            {
                case HarmonyPatchType.Prefix: harmony.Patch(binding.Target, prefix: patch); break;
                case HarmonyPatchType.Postfix: harmony.Patch(binding.Target, postfix: patch); break;
                case HarmonyPatchType.Transpiler: harmony.Patch(binding.Target, transpiler: patch); break;
                default: throw new InvalidOperationException("未支持的 RPG 补丁种类。");
            }
        }

        internal static void Rollback(Harmony harmony, List<RPGInventoryBinding> attempted)
        {
            foreach (RPGInventoryBinding binding in attempted.AsEnumerable().Reverse())
            {
                try { harmony.Unpatch(binding.Target, binding.Patch); }
                catch { /* 不卸载上游或其他模块，保留原始安装异常。 */ }
            }
        }
    }
}
