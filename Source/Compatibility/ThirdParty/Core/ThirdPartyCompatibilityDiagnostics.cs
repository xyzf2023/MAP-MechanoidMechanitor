using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty
{
    /// <summary>
    /// 仅在兼容补丁失败且用户开启详细日志时构造诊断报告。
    /// Harmony 现场只能用于定位共同修改同一目标的补丁及高可信候选，
    /// 不能仅凭“补丁了同一个方法”就断定其他 MOD 是唯一原因。
    /// </summary>
    internal static class ThirdPartyCompatibilityDiagnostics
    {
        public static string BuildFailureReport(
            ThirdPartyCompatibilityResult result)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("兼容层详细诊断：");
            builder.AppendLine("- 模块ID：" + result.ModuleId);
            builder.AppendLine("- 显示名称：" + result.DisplayName);
            builder.AppendLine("- 目标PackageId：" + result.PackageId);
            builder.AppendLine("- 失败状态：" + result.Status);
            builder.AppendLine("- 初步原因：" + NormalizeDetail(result.Detail));

            if (result.Exception != null)
            {
                builder.AppendLine("- 异常链：");
                AppendExceptionChain(builder, result.Exception);
            }
            else
            {
                builder.AppendLine("- 异常链：无捕获异常。");
            }

            builder.AppendLine("- Harmony目标与现有补丁：");
            if (result.DiagnosticTargets.Count == 0)
            {
                builder.AppendLine(
                    "  未取得可用于查询 Harmony 现场的目标方法；"
                    + "本次更可能发生在依赖检查、反射解析或补丁安装前的初始化阶段。");
                return builder.ToString().TrimEnd();
            }

            for (int i = 0; i < result.DiagnosticTargets.Count; i++)
            {
                AppendTargetReport(
                    builder,
                    result.DiagnosticTargets[i],
                    i + 1);
            }

            builder.AppendLine(
                "- 归因说明：以上列表只能证明这些补丁共同修改了目标方法。"
                + "若异常栈直接指向某个补丁，或本兼容的 IL 锚点在该补丁之前存在、"
                + "之后消失，才可将其视为高可信原因；否则只能作为冲突候选。");
            return builder.ToString().TrimEnd();
        }

        private static string NormalizeDetail(string detail)
        {
            return string.IsNullOrWhiteSpace(detail)
                ? "未提供进一步原因。"
                : detail.Trim();
        }

        private static void AppendExceptionChain(
            StringBuilder builder,
            Exception exception)
        {
            Exception? current = exception;
            int depth = 0;
            while (current != null && depth < 12)
            {
                builder.Append("  [");
                builder.Append(depth);
                builder.Append("] ");
                builder.Append(current.GetType().FullName);
                builder.Append("：");
                builder.AppendLine(current.Message);

                if (!string.IsNullOrWhiteSpace(current.StackTrace))
                {
                    builder.AppendLine(current.StackTrace);
                }

                current = current.InnerException;
                depth++;
            }

            if (current != null)
            {
                builder.AppendLine("  异常链超过12层，后续内容已省略。");
            }
        }

        private static void AppendTargetReport(
            StringBuilder builder,
            MethodBase target,
            int number)
        {
            builder.Append("  目标");
            builder.Append(number);
            builder.Append("：");
            builder.AppendLine(FormatMethod(target));
            builder.AppendLine(
                "    目标程序集："
                + target.Module.Assembly.GetName().FullName);

            Patches? patches;
            try
            {
                patches = Harmony.GetPatchInfo(target);
            }
            catch (Exception ex)
            {
                builder.AppendLine(
                    "    读取 Harmony 补丁现场失败："
                    + ex.GetType().FullName
                    + "："
                    + ex.Message);
                return;
            }

            if (patches == null || patches.Owners.Count == 0)
            {
                builder.AppendLine("    当前未发现已登记的 Harmony 补丁。");
                return;
            }

            AppendPatchGroup(builder, "Prefix", patches.Prefixes);
            AppendPatchGroup(builder, "Postfix", patches.Postfixes);
            AppendPatchGroup(builder, "Transpiler", patches.Transpilers);
            AppendPatchGroup(builder, "Finalizer", patches.Finalizers);
        }

        private static void AppendPatchGroup(
            StringBuilder builder,
            string patchType,
            IEnumerable<Patch> patches)
        {
            int number = 0;
            foreach (Patch patch in patches)
            {
                number++;
                builder.Append("    ");
                builder.Append(patchType);
                builder.Append(number);
                builder.Append("：Harmony ID=");
                builder.Append(patch.owner);
                builder.Append("，优先级=");
                builder.Append(patch.priority);
                builder.Append("，登记序号=");
                builder.AppendLine(patch.index.ToString());

                try
                {
                    MethodInfo patchMethod = patch.PatchMethod;
                    builder.AppendLine(
                        "      补丁方法：" + FormatMethod(patchMethod));
                    builder.AppendLine(
                        "      补丁程序集："
                        + patchMethod.Module.Assembly.GetName().FullName);
                    builder.AppendLine(
                        "      可能所属MOD："
                        + FindOwningMods(patchMethod.Module.Assembly));
                }
                catch (Exception ex)
                {
                    builder.AppendLine(
                        "      读取补丁方法失败："
                        + ex.GetType().FullName
                        + "："
                        + ex.Message);
                }

                if (patch.before.Length > 0)
                {
                    builder.AppendLine(
                        "      before：" + string.Join(", ", patch.before));
                }

                if (patch.after.Length > 0)
                {
                    builder.AppendLine(
                        "      after：" + string.Join(", ", patch.after));
                }
            }

            if (number == 0)
            {
                builder.AppendLine("    " + patchType + "：无。");
            }
        }

        private static string FindOwningMods(Assembly assembly)
        {
            List<string> matches = new List<string>();
            List<ModContentPack> mods =
                LoadedModManager.RunningModsListForReading;

            for (int i = 0; i < mods.Count; i++)
            {
                ModContentPack mod = mods[i];
                if (mod.assemblies.loadedAssemblies.Contains(assembly))
                {
                    matches.Add(
                        "「"
                        + mod.Name
                        + "」("
                        + mod.PackageId
                        + ")");
                }
            }

            return matches.Count == 0
                ? "未能从已加载程序集映射到具体MOD"
                : string.Join("；", matches);
        }

        private static string FormatMethod(MethodBase method)
        {
            string declaringType =
                method.DeclaringType?.FullName
                ?? "<无声明类型>";
            ParameterInfo[] parameters = method.GetParameters();
            string[] parameterNames = new string[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                Type parameterType = parameters[i].ParameterType;
                parameterNames[i] =
                    parameterType.FullName
                    ?? parameterType.Name;
            }

            string returnType =
                method is MethodInfo methodInfo
                    ? methodInfo.ReturnType.FullName
                        ?? methodInfo.ReturnType.Name
                    : "System.Void";

            return
                returnType
                + " "
                + declaringType
                + "."
                + method.Name
                + "("
                + string.Join(", ", parameterNames)
                + ")";
        }
    }
}
