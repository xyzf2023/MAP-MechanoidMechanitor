using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.RimHUD
{
    // 四项显示修正独立注册、验证及回滚，某一上游入口变化不影响其他功能。
    internal sealed class RimHUDPawnDisplayCompatibility : IThirdPartyCompatibilityModule
    {
        internal enum Feature { CompInfo, Inspiration, CapabilityLayout, ServiceEnergy }
        private readonly Feature feature;
        internal RimHUDPawnDisplayCompatibility(Feature feature) => this.feature = feature;
        public string ModuleId => "RimHUD." + feature;
        public string PackageId => "Jaxe.RimHUD";
        public string DisplayName => "RimHUD：" + (feature switch
        {
            Feature.CompInfo => "低电关机与组件信息",
            Feature.Inspiration => "无心情机械师灵感",
            Feature.CapabilityLayout => "机械体技能与作息布局",
            _ => "整备台能量速率提示"
        });

        private sealed class Binding
        {
            internal readonly MethodInfo Target;
            internal readonly MethodInfo Patch;
            internal readonly bool Transpiler;
            internal Binding(MethodInfo target, string patchName, bool transpiler = false)
            {
                Target = target;
                Patch = typeof(RimHUDPawnDisplayPatches).GetMethods(BindingFlags.Static | BindingFlags.Public
                    | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Single(m => m.Name == patchName);
                Transpiler = transpiler;
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
                    Resolve(mod, bindings);
                    foreach (Binding binding in bindings.Where(b => b.Transpiler))
                    {
                        // 安装前验证原始 IL，安装时再验证 Harmony 实际提供的指令。
                        ((IEnumerable<CodeInstruction>)binding.Patch.Invoke(null,
                            new object[] { PatchProcessor.GetOriginalInstructions(binding.Target) })!).ToList();
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
                    if (binding.Transpiler) harmony.Patch(binding.Target, transpiler: patch);
                    else harmony.Patch(binding.Target, postfix: patch);
                }
                SetEnabled(true);
            }
            catch (Exception ex)
            {
                SetEnabled(false);
                for (int i = attempted.Count - 1; i >= 0; i--)
                {
                    try { harmony.Unpatch(attempted[i].Target, attempted[i].Patch); }
                    catch { /* 仅撤销本功能，保留原始异常。 */ }
                }
                return ThirdPartyCompatibilityResult.CreateFailed(ModuleId, DisplayName, PackageId,
                    "RimHUD 显示兼容安装失败，已禁用并尝试回滚本功能补丁。", ex,
                    bindings.Select(b => (MethodBase)b.Target).ToArray());
            }
            return ThirdPartyCompatibilityResult.CreateApplied(ModuleId, DisplayName, PackageId,
                "已安装显示层修正，保留上游模型、布局及本 MOD 的能力与数据来源。");
        }

        private void SetEnabled(bool enabled)
        {
            switch (feature)
            {
                case Feature.CompInfo: RimHUDPawnDisplayPatches.CompInfoEnabled = enabled; break;
                case Feature.Inspiration: RimHUDPawnDisplayPatches.InspirationEnabled = enabled; break;
                case Feature.CapabilityLayout: RimHUDPawnDisplayPatches.LayoutEnabled = enabled; break;
                case Feature.ServiceEnergy: RimHUDPawnDisplayPatches.EnergyEnabled = enabled; break;
            }
        }

        private void Resolve(ModContentPack mod, List<Binding> bindings)
        {
            if (feature == Feature.CompInfo)
            {
                bindings.Add(new Binding(Method(Type(mod, "Interface.Hud.Models.Values.CompInfoValue"),
                    "GetValue", true, typeof(string)), nameof(RimHUDPawnDisplayPatches.CompInfoTranspiler), true));
                return;
            }

            // 直接复用上游当前 Pawn 来源，缓存方法委托，不保存 Pawn 或跨存档 UI 状态。
            RimHUDPawnDisplayPatches.SelectedPawn = (Func<Pawn?>)Delegate.CreateDelegate(typeof(Func<Pawn?>),
                Method(Type(mod, "Engine.State"), "get_SelectedPawn", true, typeof(Pawn)));
            switch (feature)
            {
                case Feature.Inspiration:
                    Type mental = Type(mod, "Interface.Hud.Models.Values.MentalConditionValue");
                    MethodInfo format = Method(mental, "GetInspiration", true, typeof(string));
                    RimHUDPawnDisplayPatches.FormatInspiration = (Func<string?>)Delegate.CreateDelegate(
                        typeof(Func<string?>), format);
                    bindings.Add(new Binding(Method(mental, "GetValue", true, typeof(string)),
                        nameof(RimHUDPawnDisplayPatches.InspirationPostfix)));
                    bindings.Add(new Binding(Method(Type(mod, "Interface.Hud.Tooltips.MentalTooltip"),
                        "Get", true, typeof(string)), nameof(RimHUDPawnDisplayPatches.InspirationPostfix)));
                    break;
                case Feature.ServiceEnergy:
                    bindings.Add(new Binding(Method(Type(mod, "Interface.Hud.Models.Bars.NeedEnergyBar"),
                        "GetTooltip", true, typeof(string)), nameof(RimHUDPawnDisplayPatches.EnergyPostfix)));
                    break;
                case Feature.CapabilityLayout:
                    ResolveLayout(mod, bindings);
                    break;
            }
        }

        private static void ResolveLayout(ModContentPack mod, List<Binding> bindings)
        {
            Type layer = Type(mod, "Interface.Hud.Layers.BaseLayer");
            Type widget = Type(mod, "Interface.Hud.Layers.WidgetLayer");
            Type row = Type(mod, "Interface.Hud.Layers.RowLayer");
            Type panel = Type(mod, "Interface.Hud.Layers.PanelLayer");
            Type stack = Type(mod, "Interface.Hud.Layers.StackLayer");
            Type args = Type(mod, "Interface.Hud.HudArgs");
            Type target = Type(mod, "Interface.Hud.Layers.LayerTarget");
            if (!target.IsEnum || Enum.GetUnderlyingType(target) != typeof(int)
                || !Enum.IsDefined(target, "PlayerHumanlike") || !Enum.IsDefined(target, "PlayerCreature")
                || Convert.ToInt32(Enum.Parse(target, "PlayerHumanlike")) != 1
                || Convert.ToInt32(Enum.Parse(target, "PlayerCreature")) != 2
                || !layer.IsAssignableFrom(widget) || !layer.IsAssignableFrom(row)
                || !layer.IsAssignableFrom(panel) || !layer.IsAssignableFrom(stack))
                throw new InvalidOperationException("RimHUD 布局继承关系或玩家目标枚举已变化。");

            MethodInfo isTarget = Method(layer, "IsTarget", false, typeof(bool));
            MethodInfo argsGetter = Method(layer, "get_Args", false, args);
            MethodInfo targetsGetter = Method(args, "get_Targets", false, target);
            MethodInfo idGetter = Getter(layer, "Id", typeof(string));
            MethodInfo rowChildren = Method(row, "get_Children", false, widget.MakeArrayType());
            MethodInfo panelChildren = Method(panel, "get_Children", false, row.MakeArrayType());
            MethodInfo stackChildren = Method(stack, "get_Children", false, layer.MakeArrayType());

            RimHUDPawnDisplayPatches.IsTargetMethod = isTarget;
            RimHUDPawnDisplayPatches.OriginalIsTarget = InstanceGetter<bool>(isTarget);
            RimHUDPawnDisplayPatches.LayerId = InstanceGetter<string>(idGetter);
            var instance = Expression.Parameter(typeof(object), "layer");
            var layerArgs = Expression.Call(Expression.Convert(instance, layer), argsGetter);
            RimHUDPawnDisplayPatches.LayerTargets = Expression.Lambda<Func<object, int>>(
                Expression.Convert(Expression.Call(layerArgs, targetsGetter), typeof(int)), instance).Compile();
            RimHUDPawnDisplayPatches.WidgetType = widget;
            RimHUDPawnDisplayPatches.Children = new[]
            {
                new RimHUDPawnDisplayPatches.ChildrenAccessor(row, InstanceGetter<Array>(rowChildren)),
                new RimHUDPawnDisplayPatches.ChildrenAccessor(panel, InstanceGetter<Array>(panelChildren)),
                new RimHUDPawnDisplayPatches.ChildrenAccessor(stack, InstanceGetter<Array>(stackChildren))
            };

            // 修改实际调用点而非极短的 IsTarget getter，避免已内联的方法绕过修正。
            bindings.Add(new Binding(Method(widget, "Build", false, typeof(void)),
                nameof(RimHUDPawnDisplayPatches.LayoutTranspiler), true));
            Type container = Type(mod, "Interface.Hud.Layers.ContainerLayer`1");
            if (!container.IsGenericTypeDefinition || container.GetGenericArguments().Length != 1)
                throw new InvalidOperationException("RimHUD ContainerLayer 泛型结构已变化。");
            MethodInfo rowVisible = Method(container.MakeGenericType(widget), "IsVisible", false, typeof(bool));
            MethodInfo panelVisible = Method(container.MakeGenericType(row), "IsVisible", false, typeof(bool));
            foreach (MethodInfo visible in new[] { rowVisible, panelVisible })
                if (PatchProcessor.GetOriginalInstructions(visible).Count(c => c.Calls(isTarget)) != 1)
                    throw new InvalidOperationException("RimHUD 容器可见性不再调用预期目标判断。");
            RimHUDPawnDisplayPatches.RowVisibleMethod = rowVisible;
            RimHUDPawnDisplayPatches.PanelVisibleMethod = panelVisible;
            RimHUDPawnDisplayPatches.BaseVisibleMethod = Method(layer, "IsVisible", false, typeof(bool));
            RimHUDPawnDisplayPatches.OriginalRowVisible = InstanceGetter<bool>(rowVisible);
            RimHUDPawnDisplayPatches.OriginalPanelVisible = InstanceGetter<bool>(panelVisible);

            // 不直接 Patch 两个引用类型的泛型实例，避免 Mono 泛型代码共享导致重复替换。
            foreach (Type concrete in new[] { row, panel })
            {
                string patchName = concrete == row ? nameof(RimHUDPawnDisplayPatches.RowVisibilityTranspiler)
                    : nameof(RimHUDPawnDisplayPatches.PanelVisibilityTranspiler);
                bindings.Add(new Binding(Method(concrete, "Prepare", false, typeof(float)),
                    patchName, true));
                bindings.Add(new Binding(Method(concrete, "Draw", false, typeof(bool), typeof(Rect)),
                    patchName, true));
            }
            bindings.Add(new Binding(Method(stack, "IsVisible", false, typeof(bool)),
                nameof(RimHUDPawnDisplayPatches.LayoutTranspiler), true));
        }

        private static Type Type(ModContentPack mod, string name)
        {
            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(mod, "RimHUD." + name,
                    out Type? type, out string failure))
                throw new InvalidOperationException(failure);
            return type!;
        }

        private static MethodInfo Method(Type type, string name, bool isStatic, Type result, params Type[] parameters)
        {
            MethodInfo[] matches = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly | (isStatic ? BindingFlags.Static : BindingFlags.Instance))
                .Where(m => m.Name == name && !m.IsAbstract && !m.IsGenericMethod && !m.ContainsGenericParameters
                    && m.ReturnType == result && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameters))
                .ToArray();
            if (matches.Length != 1 || matches[0].GetMethodBody() == null)
                throw new InvalidOperationException($"RimHUD：{type.FullName}.{name} 完整签名或方法体已变化。");
            return matches[0];
        }

        // BaseLayer.Id 为抽象虚属性，只构建虚调用委托，不作为 Harmony 补丁入口。
        private static MethodInfo Getter(Type type, string name, Type result)
        {
            PropertyInfo? property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public
                | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            MethodInfo? getter = property?.GetGetMethod(true);
            if (getter == null || property!.PropertyType != result || getter.IsStatic
                || getter.GetParameters().Length != 0)
                throw new InvalidOperationException($"RimHUD：{type.FullName}.{name} 属性签名已变化。");
            return getter;
        }

        private static Func<object, T> InstanceGetter<T>(MethodInfo method)
        {
            var instance = Expression.Parameter(typeof(object), "instance");
            return Expression.Lambda<Func<object, T>>(Expression.Convert(
                Expression.Call(Expression.Convert(instance, method.DeclaringType!), method), typeof(T)), instance).Compile();
        }
    }
}
