using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.RimHUD
{
    internal sealed class RimHUDRechargeButtonCompatibility : IThirdPartyCompatibilityModule
    {
        private const string ButtonsTypeName = "RimHUD.Interface.Screen.InspectPaneButtons";
        private const string PaneTypeName = "RimHUD.Interface.Screen.InspectPanePlus";

        public string ModuleId => "RimHUD.PersonalRechargeButton";
        public string DisplayName => "RimHUD：自律机械体个人充电阈值按钮";
        public string PackageId => "Jaxe.RimHUD";

        public ThirdPartyCompatibilityResult Apply(Harmony harmony)
        {
            RimHUDRechargeButtonPatches.Disable();
            ModContentPack? mod = ThirdPartyCompatibilityTargetResolver.FindRunningMod(PackageId);
            if (mod == null)
                return ThirdPartyCompatibilityResult.CreateInactive(ModuleId, DisplayName, PackageId);

            MethodInfo? draw = null;
            MethodInfo? getRowRect = null;
            MethodInfo? drawPane = null;
            MethodInfo? postfix = null;
            bool attempted = false;
            try
            {
                // 不静态引用 RimHUD；所有类型、完整签名及调用路径确认后才安装。
                if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(
                        mod, ButtonsTypeName, out Type? buttonsType, out string failure)
                    || !TryResolveMethod(buttonsType!, "Draw", typeof(void), true,
                        new[] { typeof(Rect), typeof(IInspectPane), typeof(float).MakeByRefType() },
                        out draw, out failure)
                    || !TryResolveMethod(buttonsType!, "GetRowRect", typeof(Rect), false,
                        new[] { typeof(Rect), typeof(float).MakeByRefType(), typeof(float), typeof(float) },
                        out getRowRect, out failure)
                    || !ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(
                        mod, PaneTypeName, out Type? paneType, out failure)
                    || !TryResolveMethod(paneType!, "DrawPane", typeof(void), true,
                        new[] { typeof(Rect), typeof(IInspectPane) }, out drawPane, out failure)
                    || !ValidateCallPath(draw!, getRowRect!, drawPane!, out failure))
                {
                    return ThirdPartyCompatibilityResult.CreateTargetChanged(
                        ModuleId, DisplayName, PackageId, failure,
                        DiagnosticTargets(draw, getRowRect, drawPane));
                }

                postfix = AccessTools.DeclaredMethod(
                    typeof(RimHUDRechargeButtonPatches), nameof(RimHUDRechargeButtonPatches.Postfix),
                    new[] { typeof(Rect), typeof(IInspectPane), typeof(float).MakeByRefType(), typeof(bool) });
                if (postfix == null || !postfix.IsStatic || postfix.ReturnType != typeof(void))
                    throw new MissingMethodException(typeof(RimHUDRechargeButtonPatches).FullName, "Postfix");

                // 缓存强类型委托，GUI 每帧不做 MethodInfo.Invoke 或生成反射参数数组。
                var rowLayout = (RimHUDRechargeButtonPatches.RowRectGetter)Delegate.CreateDelegate(
                    typeof(RimHUDRechargeButtonPatches.RowRectGetter), getRowRect!);
                RimHUDRechargeButtonPatches.Configure(rowLayout);
                attempted = true;
                harmony.Patch(draw!, postfix: new HarmonyMethod(postfix));
                RimHUDRechargeButtonPatches.Enabled = true;
            }
            catch (Exception ex)
            {
                // 先禁用并清空委托；即使回滚失败，残留 Postfix 也不绘制或修改 offset。
                RimHUDRechargeButtonPatches.Disable();
                if (attempted && draw != null && postfix != null)
                {
                    try { harmony.Unpatch(draw, postfix); }
                    catch { /* 保留初始异常，仅撤销本模块补丁。 */ }
                }
                return ThirdPartyCompatibilityResult.CreateFailed(
                    ModuleId, DisplayName, PackageId,
                    "RimHUD 充电按钮兼容安装失败，已禁用并尝试回滚本模块补丁。", ex,
                    DiagnosticTargets(draw, getRowRect, drawPane));
            }

            return ThirdPartyCompatibilityResult.CreateApplied(
                ModuleId, DisplayName, PackageId,
                "已在 RimHUD 顶部按钮排追加个人充电阈值按钮，复用上游布局及现有设置窗口。");
        }

        private static bool TryResolveMethod(
            Type type, string name, Type returnType, bool isPublic, Type[] parameters,
            out MethodInfo? method, out string failure)
        {
            BindingFlags visibility = isPublic ? BindingFlags.Public : BindingFlags.NonPublic;
            MethodInfo[] matches = type.GetMethods(visibility | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(candidate => candidate.Name == name
                    && !candidate.IsAbstract && !candidate.IsGenericMethod && !candidate.ContainsGenericParameters
                    && candidate.ReturnType == returnType
                    && candidate.GetParameters().Select(parameter => parameter.ParameterType)
                        .SequenceEqual(parameters))
                .ToArray();
            method = matches.Length == 1 ? matches[0] : null;
            failure = method == null
                ? $"RimHUD 方法 {type.FullName}.{name} 的完整签名预期唯一，实际找到 {matches.Length} 个。"
                : string.Empty;
            return method != null;
        }

        private static bool ValidateCallPath(
            MethodInfo draw, MethodInfo getRowRect, MethodInfo drawPane, out string failure)
        {
            if (draw.GetMethodBody() == null || getRowRect.GetMethodBody() == null || drawPane.GetMethodBody() == null)
            {
                failure = "RimHUD 顶部按钮、布局或面板方法缺少可检查的方法体。";
                return false;
            }

            List<CodeInstruction> buttons = PatchProcessor.GetOriginalInstructions(draw).ToList();
            List<CodeInstruction> pane = PatchProcessor.GetOriginalInstructions(drawPane).ToList();
            if (pane.Count(code => code.Calls(draw)) != 1 || !buttons.Any(code => code.Calls(getRowRect)))
            {
                failure = "RimHUD 不再沿用 DrawPane → InspectPaneButtons.Draw → GetRowRect 的预期调用路径。";
                return false;
            }

            // 上游若改为复用原版按钮，原版 Transpiler 已负责绘制；不再额外补画。
            if (buttons.Concat(pane).Any(code => code.operand is MethodInfo method
                    && method.Name == nameof(MainTabWindow_Inspect.DoInspectPaneButtons)
                    && method.GetParameters().Select(parameter => parameter.ParameterType)
                        .SequenceEqual(new[] { typeof(Rect), typeof(float).MakeByRefType() })
                    && method.DeclaringType != null
                    && typeof(IInspectPane).IsAssignableFrom(method.DeclaringType)))
            {
                failure = "RimHUD 已调用原版检查面板按钮入口，跳过额外按钮以避免重复绘制。";
                return false;
            }

            failure = string.Empty;
            return true;
        }

        private static MethodBase[] DiagnosticTargets(params MethodInfo?[] methods) =>
            methods.Where(method => method != null).Cast<MethodBase>().ToArray();
    }
}
