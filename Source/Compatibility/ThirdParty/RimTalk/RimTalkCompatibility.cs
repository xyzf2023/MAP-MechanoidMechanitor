using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.RimTalk
{
    internal sealed class RimTalkCompatibility : IThirdPartyCompatibilityModule
    {
        public string ModuleId => "RimTalk.IntrinsicDialogue";
        public string DisplayName => "RimTalk：机械族机械师自带对话资格";
        public string PackageId => "cj.rimtalk";

        public ThirdPartyCompatibilityResult Apply(Harmony harmony)
        {
            ModContentPack? mod = ThirdPartyCompatibilityTargetResolver.FindRunningMod(PackageId);
            if (mod == null)
                return ThirdPartyCompatibilityResult.CreateInactive(ModuleId, DisplayName, PackageId);

            MethodInfo? target = null;
            MethodInfo? postfix = null;
            bool attempted = false;
            try
            {
                // 目标、设置读取入口和开关必须全部校验成功后才配置和安装补丁。
                if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(
                        mod, "RimTalk.Util.PawnUtil", out Type? pawnUtilType, out string failure)
                    || !TryResolveStaticMethod(pawnUtilType!, "HasVocalLink", typeof(bool),
                        new[] { typeof(Pawn) }, out target, out failure)
                    || !ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(
                        mod, "RimTalk.Settings", out Type? settingsType, out failure)
                    || !ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(
                        mod, "RimTalk.RimTalkSettings", out Type? settingsDataType, out failure))
                {
                    return ThirdPartyCompatibilityResult.CreateTargetChanged(
                        ModuleId, DisplayName, PackageId, failure);
                }

                if (!typeof(Mod).IsAssignableFrom(settingsType)
                    || !typeof(ModSettings).IsAssignableFrom(settingsDataType))
                {
                    return ThirdPartyCompatibilityResult.CreateTargetChanged(
                        ModuleId, DisplayName, PackageId, "RimTalk 设置类型不再继承 Mod / ModSettings。");
                }

                if (!TryResolveStaticMethod(settingsType!, "Get", settingsDataType!, Type.EmptyTypes,
                        out MethodInfo? getSettings, out failure)
                    || !ThirdPartyCompatibilityTargetResolver.TryResolveUniqueField(
                        settingsDataType!, "AllowNonHumanToTalk", typeof(bool),
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                        out FieldInfo? allowNonHuman, out failure))
                {
                    return ThirdPartyCompatibilityResult.CreateTargetChanged(
                        ModuleId, DisplayName, PackageId, failure);
                }

                postfix = typeof(RimTalkVocalLinkPatch).GetMethod(
                    nameof(RimTalkVocalLinkPatch.Postfix),
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly,
                    null, new[] { typeof(Pawn), typeof(bool).MakeByRefType() }, null);
                if (postfix == null || postfix.ReturnType != typeof(void))
                    throw new MissingMethodException(typeof(RimTalkVocalLinkPatch).FullName, "Postfix");

                // 返回值协变为 object；不静态引用第三方类型，不捕获设置或 Game 实例。
                var settingsGetter = (Func<object?>)Delegate.CreateDelegate(
                    typeof(Func<object>), getSettings!);
                RimTalkVocalLinkPatch.Configure(settingsGetter, allowNonHuman!);
                attempted = true;
                harmony.Patch(target!, postfix: new HarmonyMethod(postfix));
            }
            catch (Exception ex)
            {
                // 先关闭新增资格；即使撤销失败，残留 Postfix 也只保留上游结果。
                RimTalkVocalLinkPatch.Disable();
                if (attempted && target != null && postfix != null)
                {
                    try { harmony.Unpatch(target, postfix); }
                    catch { /* 保留安装异常，仅尝试撤销本模块补丁。 */ }
                }
                return ThirdPartyCompatibilityResult.CreateFailed(
                    ModuleId, DisplayName, PackageId,
                    "RimTalk 对话资格兼容安装失败，已关闭新增资格并尝试回滚本模块补丁。",
                    ex, target == null ? Array.Empty<MethodBase>() : new MethodBase[] { target });
            }

            return ThirdPartyCompatibilityResult.CreateApplied(
                ModuleId, DisplayName, PackageId,
                "已扩展 HasVocalLink：正式玩家机械族机械师自带对话资格，保留非人类对话开关。");
        }

        private static bool TryResolveStaticMethod(
            Type type, string name, Type returnType, Type[] parameters,
            out MethodInfo? method, out string failure)
        {
            MethodInfo[] matches = type.GetMethods(
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(candidate => candidate.Name == name
                    && !candidate.IsGenericMethod && !candidate.ContainsGenericParameters
                    && candidate.ReturnType == returnType
                    && candidate.GetParameters().Select(parameter => parameter.ParameterType)
                        .SequenceEqual(parameters))
                .ToArray();
            method = matches.Length == 1 ? matches[0] : null;
            failure = method == null
                ? $"RimTalk 静态方法 {type.FullName}.{name} 的完整签名匹配预期唯一，实际 {matches.Length} 个。"
                : string.Empty;
            return method != null;
        }
    }
}
