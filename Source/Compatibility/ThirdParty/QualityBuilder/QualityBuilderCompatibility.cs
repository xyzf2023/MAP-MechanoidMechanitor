using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.QualityBuilder
{
    internal sealed class QualityBuilderCompatibility : IThirdPartyCompatibilityModule
    {
        private const string TargetTypeName = "QualityBuilder.QualityBuilder";

        public string ModuleId => "QualityBuilder.AutonomousConstruction";
        public string DisplayName => "QualityBuilder：自律机械族施工与实际技能";
        public string PackageId => "hatti.qualitybuilder";

        public ThirdPartyCompatibilityResult Apply(Harmony harmony)
        {
            QualityBuilderCompatibilityPatches.Enabled = false;
            ModContentPack? mod = ThirdPartyCompatibilityTargetResolver.FindRunningMod(PackageId);
            if (mod == null)
                return ThirdPartyCompatibilityResult.CreateInactive(ModuleId, DisplayName, PackageId);

            MethodInfo? qualificationTarget = null;
            MethodInfo? skillTarget = null;
            MethodInfo? qualificationPostfix = null;
            MethodInfo? skillPostfix = null;
            bool qualificationAttempted = false;
            bool skillAttempted = false;
            try
            {
                // 两个完整签名均校验成功后才安装，不静态引用 QualityBuilder 程序集。
                if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(
                        mod, TargetTypeName, out Type? targetType, out string failure)
                    || !TryResolveTarget(targetType!, "pawnCanConstruct", typeof(bool),
                        out qualificationTarget, out failure)
                    || !TryResolveTarget(targetType!, "getPawnConstructionSkill", typeof(int),
                        out skillTarget, out failure))
                {
                    return ThirdPartyCompatibilityResult.CreateTargetChanged(
                        ModuleId, DisplayName, PackageId, failure);
                }

                qualificationPostfix = ResolvePostfix(
                    nameof(QualityBuilderCompatibilityPatches.PawnCanConstructPostfix), typeof(bool));
                skillPostfix = ResolvePostfix(
                    nameof(QualityBuilderCompatibilityPatches.GetPawnConstructionSkillPostfix), typeof(int));

                qualificationAttempted = true;
                harmony.Patch(qualificationTarget!, postfix: new HarmonyMethod(qualificationPostfix));
                skillAttempted = true;
                harmony.Patch(skillTarget!, postfix: new HarmonyMethod(skillPostfix));
                QualityBuilderCompatibilityPatches.Enabled = true;
            }
            catch (Exception ex)
            {
                // 先关闭修正；回滚失败时残留 Postfix 同样保留上游结果。
                QualityBuilderCompatibilityPatches.Enabled = false;
                if (skillAttempted && skillTarget != null && skillPostfix != null)
                {
                    try { harmony.Unpatch(skillTarget, skillPostfix); }
                    catch { /* 保留原始异常，仅撤销本模块补丁。 */ }
                }
                if (qualificationAttempted && qualificationTarget != null && qualificationPostfix != null)
                {
                    try { harmony.Unpatch(qualificationTarget, qualificationPostfix); }
                    catch { /* 不移除 QualityBuilder 或其他 MOD 的补丁。 */ }
                }

                MethodBase[] targets = new MethodInfo?[] { qualificationTarget, skillTarget }
                    .Where(target => target != null).Cast<MethodBase>().ToArray();
                return ThirdPartyCompatibilityResult.CreateFailed(
                    ModuleId, DisplayName, PackageId,
                    "QualityBuilder 兼容安装失败，已关闭修正并尝试回滚本模块补丁。", ex, targets);
            }

            return ThirdPartyCompatibilityResult.CreateApplied(
                ModuleId, DisplayName, PackageId,
                "已补充玩家自律机械族施工资格并接入实际建造技能，保留 QualityBuilder 原有筛选流程。");
        }

        private static bool TryResolveTarget(
            Type type, string name, Type returnType, out MethodInfo? target, out string failure)
        {
            MethodInfo[] matches = type.GetMethods(
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(candidate => candidate.Name == name
                    && !candidate.IsAbstract && !candidate.IsGenericMethod
                    && !candidate.ContainsGenericParameters
                    && candidate.ReturnType == returnType
                    && candidate.GetParameters().Select(parameter => parameter.ParameterType)
                        .SequenceEqual(new[] { typeof(Pawn) }))
                .ToArray();
            target = matches.Length == 1 ? matches[0] : null;
            failure = target == null
                ? $"预期在 {TargetTypeName} 中唯一找到 public static {returnType.Name} {name}(Pawn)，实际找到 {matches.Length} 个。"
                : string.Empty;
            return target != null;
        }

        private static MethodInfo ResolvePostfix(string name, Type resultType)
        {
            MethodInfo? postfix = AccessTools.DeclaredMethod(
                typeof(QualityBuilderCompatibilityPatches), name,
                new[] { typeof(Pawn), resultType.MakeByRefType() });
            if (postfix == null || !postfix.IsStatic || postfix.ReturnType != typeof(void))
                throw new MissingMethodException(typeof(QualityBuilderCompatibilityPatches).FullName, name);
            return postfix;
        }
    }
}
