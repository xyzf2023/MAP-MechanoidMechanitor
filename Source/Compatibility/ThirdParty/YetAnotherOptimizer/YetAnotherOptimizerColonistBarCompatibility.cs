using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.YetAnotherOptimizer
{
    internal sealed class YetAnotherOptimizerColonistBarCompatibility : IThirdPartyCompatibilityModule
    {
        private const string TargetTypeName = "YaOpt.Helpers.MapPawnsHelper";
        private const string TargetMethodName = "FreeColonistsAndSubhumansControllable";

        public string ModuleId => "YetAnotherOptimizer.ScenarioColonistBar";
        public string DisplayName => "Yet Another Optimizer：剧本机械族机械师头像";
        public string PackageId => "sz.yaopt";

        public ThirdPartyCompatibilityResult Apply(Harmony harmony)
        {
            YetAnotherOptimizerColonistBarPatch.Enabled = false;
            ModContentPack? mod =
                ThirdPartyCompatibilityTargetResolver.FindRunningMod(PackageId);
            if (mod == null)
            {
                return ThirdPartyCompatibilityResult.CreateInactive(
                    ModuleId, DisplayName, PackageId);
            }

            MethodInfo? target = null;
            MethodInfo? postfix = null;
            bool attempted = false;
            try
            {
                // 解析目标 MOD 自己的程序集，不静态引用 YaOpt，也不扩大到其他名单函数。
                if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(
                        mod, TargetTypeName, out Type? helperType, out string failure)
                    || !TryResolveTarget(helperType!, out target, out failure))
                {
                    return ThirdPartyCompatibilityResult.CreateTargetChanged(
                        ModuleId, DisplayName, PackageId, failure);
                }

                postfix = AccessTools.DeclaredMethod(
                    typeof(YetAnotherOptimizerColonistBarPatch),
                    nameof(YetAnotherOptimizerColonistBarPatch.Postfix),
                    new[] { typeof(MapPawns), typeof(List<Pawn>).MakeByRefType() });
                if (postfix == null || !postfix.IsStatic || postfix.ReturnType != typeof(void))
                {
                    throw new MissingMethodException(
                        typeof(YetAnotherOptimizerColonistBarPatch).FullName, "Postfix");
                }

                // 不缓存 YAO 的设置值：优化关闭时头像栏走原版 Getter，
                // 再次开启后自然调用此方法；YAO 重装自己的补丁不会移除此模块。
                attempted = true;
                harmony.Patch(target!, postfix: new HarmonyMethod(postfix));
                YetAnotherOptimizerColonistBarPatch.Enabled = true;
            }
            catch (Exception ex)
            {
                // 先关闭新增名单资格；撤销失败时残留补丁也不会扩展结果。
                YetAnotherOptimizerColonistBarPatch.Enabled = false;
                if (attempted && target != null && postfix != null)
                {
                    try
                    {
                        harmony.Unpatch(target, postfix);
                    }
                    catch
                    {
                        // 保留原始安装异常，仅尝试回滚本模块自己的补丁。
                    }
                }

                return ThirdPartyCompatibilityResult.CreateFailed(
                    ModuleId, DisplayName, PackageId,
                    "YAO 头像名单兼容安装失败，已关闭新增资格并尝试回滚本模块补丁。",
                    ex, target == null ? Array.Empty<MethodBase>() : new MethodBase[] { target });
            }

            return ThirdPartyCompatibilityResult.CreateApplied(
                ModuleId, DisplayName, PackageId,
                "YAO 头像栏名单已复用剧本机械族机械师追加规则，保留上游结果并使用本 MOD 的结果缓冲区。");
        }

        private static bool TryResolveTarget(
            Type helperType,
            out MethodInfo? target,
            out string failure)
        {
            MethodInfo[] matches = helperType.GetMethods(
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(candidate => candidate.Name == TargetMethodName
                    && !candidate.IsAbstract
                    && !candidate.IsGenericMethod
                    && !candidate.ContainsGenericParameters
                    && candidate.ReturnType == typeof(List<Pawn>)
                    && candidate.GetParameters()
                        .Select(parameter => parameter.ParameterType)
                        .SequenceEqual(new[] { typeof(MapPawns) }))
                .ToArray();

            target = matches.Length == 1 ? matches[0] : null;
            failure = target == null
                ? $"预期在 {TargetTypeName} 中唯一找到 public static List<Pawn> "
                    + $"{TargetMethodName}(MapPawns)，实际找到 {matches.Length} 个。"
                : string.Empty;
            return target != null;
        }
    }
}
