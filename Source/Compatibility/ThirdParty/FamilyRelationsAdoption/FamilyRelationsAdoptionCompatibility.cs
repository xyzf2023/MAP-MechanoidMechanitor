using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.FamilyRelationsAdoption
{
    internal sealed class FamilyRelationsAdoptionCompatibility : IThirdPartyCompatibilityModule
    {
        private const string TargetTypeName = "FamilyRelationsAdoption.FRA_PawnRelationUtility";

        public string ModuleId => "FamilyRelationsAdoption.NonFleshParentQueries";
        public string DisplayName => "Family Relations: Adoption：非血肉角色社交关系查询保护";
        public string PackageId => "Rycon.FamilyRelationsAdoption";

        public ThirdPartyCompatibilityResult Apply(Harmony harmony)
        {
            FamilyRelationsAdoptionParentQueryPatch.Enabled = false;
            ModContentPack? mod = ThirdPartyCompatibilityTargetResolver.FindRunningMod(PackageId);
            if (mod == null)
                return ThirdPartyCompatibilityResult.CreateInactive(ModuleId, DisplayName, PackageId);

            MethodInfo? target = null;
            MethodInfo? postfix = null;
            bool attempted = false;
            try
            {
                if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(
                        mod, TargetTypeName, out Type? targetType, out string failure)
                    || !TryResolveTarget(targetType!, out target, out failure))
                {
                    return ThirdPartyCompatibilityResult.CreateTargetChanged(
                        ModuleId, DisplayName, PackageId, failure);
                }

                postfix = typeof(FamilyRelationsAdoptionParentQueryPatch).GetMethod(
                    nameof(FamilyRelationsAdoptionParentQueryPatch.Postfix),
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly,
                    null, new[] { typeof(Pawn), typeof(List<Pawn>).MakeByRefType() }, null);
                if (postfix == null || postfix.ReturnType != typeof(void))
                    throw new MissingMethodException(typeof(FamilyRelationsAdoptionParentQueryPatch).FullName, "Postfix");

                attempted = true;
                harmony.Patch(target!, postfix: new HarmonyMethod(postfix));
                FamilyRelationsAdoptionParentQueryPatch.Enabled = true;
            }
            catch (Exception ex)
            {
                // 先关闭保护；即使回滚失败，残留补丁也只保留上游结果。
                FamilyRelationsAdoptionParentQueryPatch.Enabled = false;
                if (attempted && target != null && postfix != null)
                {
                    try { harmony.Unpatch(target, postfix); }
                    catch { /* 保留安装异常，仅尝试撤销本模块补丁。 */ }
                }
                return ThirdPartyCompatibilityResult.CreateFailed(
                    ModuleId, DisplayName, PackageId,
                    "领养关系查询兼容安装失败，已关闭保护并尝试回滚本模块补丁。",
                    ex, target == null ? Array.Empty<MethodBase>() : new MethodBase[] { target });
            }

            return ThirdPartyCompatibilityResult.CreateApplied(
                ModuleId, DisplayName, PackageId,
                "已保护具有社交面板能力的非血肉角色养父母列表查询，保留上游非空结果。");
        }

        private static bool TryResolveTarget(Type type, out MethodInfo? target, out string failure)
        {
            // 按上游完整 CLR 签名匹配，bool 可选参数仍必须存在；不依赖其参数名称。
            MethodInfo[] matches = type.GetMethods(
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(candidate => candidate.Name == "GetAdoptiveParents"
                    && !candidate.IsAbstract && !candidate.IsGenericMethod
                    && !candidate.ContainsGenericParameters
                    && candidate.ReturnType == typeof(List<Pawn>)
                    && candidate.GetParameters().Select(parameter => parameter.ParameterType)
                        .SequenceEqual(new[] { typeof(Pawn), typeof(bool) }))
                .ToArray();
            target = matches.Length == 1 ? matches[0] : null;
            failure = target == null
                ? $"预期在 {TargetTypeName} 中唯一找到 public static List<Pawn> GetAdoptiveParents(Pawn, bool)，实际找到 {matches.Length} 个。"
                : string.Empty;
            return target != null;
        }
    }
}
