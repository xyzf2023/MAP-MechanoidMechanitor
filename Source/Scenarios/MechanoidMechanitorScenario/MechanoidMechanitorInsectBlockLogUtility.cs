using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using RimWorld;
using RimWorld.QuestGen;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 虫巢盟友拦截专用日志工具。
    /// 任何一次真正被本 MOD 阻止的虫灾行为都会输出黄色警告（Log.Warning），
    /// 并在 best-effort 基础上尽量识别调用来源 MOD / packageId / 组件 / 方法。
    /// 日志解析失败只退化为“原版/程序集/未知调用来源”，绝不影响拦截本身。
    /// </summary>
    public static class MechanoidMechanitorInsectBlockLogUtility
    {
        private const string Prefix = "[MAP-机械族机械师] [虫巢盟友拦截]";

        // 同一种高频拦截最多每游戏日记录一次。
        private const int EligibilityWarningThrottleTicks = 60000;

        // 需要跳过本 MOD 自身拦截基础设施帧的类型与方法名。
        private static readonly HashSet<string> SkippedTypeNames = new HashSet<string>
        {
            "MAP_MechanoidMechanitor.Scenarios.MechanoidMechanitorInsectBlockLogUtility",
            "MAP_MechanoidMechanitor.Scenarios.MechanoidMechanitorInsectIncidentPolicy",
            "MAP_MechanoidMechanitor.Scenarios.MechanoidMechanitorInsectIncident_CanFireNow_Patch",
            "MAP_MechanoidMechanitor.Scenarios.MechanoidMechanitorInsectIncident_TryExecute_Patch",
            "MAP_MechanoidMechanitor.Scenarios.MechanoidMechanitorInsectQuest_QuestNodeInfestation_Patch",
            "MAP_MechanoidMechanitor.Scenarios.MechanoidMechanitorInsectQuest_QuestPartInfestation_Patch"
        };

        private static readonly List<string> SkippedNamespacePrefixes = new List<string>
        {
            "HarmonyLib",
            "System.Reflection",
            "System.Runtime",
            "MonoMod"
        };

        private static readonly Dictionary<string, int>
            LastThrottledWarningTickByKey = new Dictionary<string, int>();

        public static void WarnIncidentBlocked(
            IncidentDef? incidentDef,
            string interceptionPoint,
            bool throttled)
        {
            string key = "incident-canfire:" + (incidentDef?.defName ?? "null");
            if (throttled && !ShouldEmitThrottled(key))
            {
                return;
            }

            string defName = incidentDef?.defName ?? "null";
            string caller = ResolveCallerDescription();
            Log.Warning(
                Prefix + " " + caller
                + " 尝试调用虫灾 Incident「"
                + defName
                + "」，已阻止。拦截点："
                + interceptionPoint
                + "。");
        }

        public static void WarnQuestInfestationBlocked(
            string actionDescription,
            string interceptionPoint,
            bool throttled)
        {
            string key = "quest-infestation-testrun";
            if (throttled && !ShouldEmitThrottled(key))
            {
                return;
            }

            string caller = ResolveCallerDescription();
            Log.Warning(
                Prefix + " " + caller
                + " "
                + actionDescription
                + "，已阻止。拦截点："
                + interceptionPoint
                + "。");
        }

        private static bool ShouldEmitThrottled(string key)
        {
            if (Find.TickManager == null)
            {
                return true;
            }

            int now = Find.TickManager.TicksGame;

            if (LastThrottledWarningTickByKey.TryGetValue(key, out int previous)
                && now >= previous
                && now - previous < EligibilityWarningThrottleTicks)
            {
                return false;
            }

            LastThrottledWarningTickByKey[key] = now;
            return true;
        }

        /// <summary>
        /// 集中判断某一调用栈帧是否应被跳过。
        /// 覆盖：本 MOD 日志/Policy/四个 Harmony Prefix 基础设施类型、
        /// Harmony/反射/运行时包装命名空间，以及被本 MOD Patch 的
        /// 原版目标方法与其 Harmony wrapper（如 IncidentWorker.CanFireNow_Patch1）。
        /// 不会跳过第三方 MOD 或本 MOD 的非基础设施组件。
        /// </summary>
        private static bool ShouldSkipFrame(
            Type type,
            string typeName,
            MethodBase method)
        {
            if (SkippedTypeNames.Contains(typeName))
            {
                return true;
            }

            for (int i = 0; i < SkippedNamespacePrefixes.Count; i++)
            {
                if (typeName.StartsWith(
                        SkippedNamespacePrefixes[i],
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            if (IsPatchedTargetInfrastructureFrame(type, method))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 精确识别被本 MOD Patch 的原版目标方法及其 Harmony 生成的 wrapper。
        /// 只有当 DeclaringType 是我们明确 Patch 的目标类型时才跳过，
        /// 避免误伤第三方 MOD 中恰好名字含 “_Patch” 的真实业务方法。
        /// </summary>
        private static bool IsPatchedTargetInfrastructureFrame(
            Type type,
            MethodBase method)
        {
            string methodName = method.Name;

            if (type == typeof(IncidentWorker))
            {
                if (methodName == nameof(IncidentWorker.CanFireNow)
                    || methodName.StartsWith(
                        nameof(IncidentWorker.CanFireNow) + "_Patch",
                        StringComparison.Ordinal))
                {
                    return true;
                }

                if (methodName == nameof(IncidentWorker.TryExecute)
                    || methodName.StartsWith(
                        nameof(IncidentWorker.TryExecute) + "_Patch",
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            if (type == typeof(QuestNode_Infestation))
            {
                if (methodName == "TestRunInt"
                    || methodName.StartsWith(
                        "TestRunInt_Patch",
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            if (type == typeof(QuestPart_Infestation))
            {
                string targetMethod =
                    nameof(QuestPart_Infestation.Notify_QuestSignalReceived);

                if (methodName == targetMethod
                    || methodName.StartsWith(
                        targetMethod + "_Patch",
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string ResolveCallerDescription()
        {
            try
            {
                StackTrace trace = new StackTrace();
                StackFrame[] frames = trace.GetFrames();
                if (frames == null)
                {
                    return "未知调用来源";
                }

                for (int i = 0; i < frames.Length; i++)
                {
                    StackFrame frame = frames[i];
                    MethodBase? method = frame.GetMethod();
                    if (method == null)
                    {
                        continue;
                    }

                    Type? type = method.DeclaringType;
                    if (type == null)
                    {
                        // Harmony wrapper 等 DeclaringType 为 null 的包装层直接跳过。
                        continue;
                    }

                    string typeName = type.FullName ?? type.ToString();

                    if (ShouldSkipFrame(type, typeName, method))
                    {
                        continue;
                    }

                    Assembly assembly = method.Module.Assembly;
                    ModContentPack? mod = TryFindOwningMod(assembly);

                    if (mod != null)
                    {
                        string packageId = !string.IsNullOrEmpty(
                                mod.PackageIdPlayerFacing)
                            ? mod.PackageIdPlayerFacing
                            : (mod.PackageId ?? string.Empty);

                        return "MOD「" + mod.Name + "」(" + packageId + ") / 组件 "
                            + typeName + "." + method.Name;
                    }

                    if (assembly == typeof(IncidentWorker).Assembly)
                    {
                        return "原版 RimWorld / 组件 " + typeName + "." + method.Name;
                    }

                    return "程序集「" + assembly.GetName().Name + "」 / 组件 "
                        + typeName + "." + method.Name;
                }

                return "未知调用来源";
            }
            catch
            {
                return "未知调用来源";
            }
        }

        private static ModContentPack? TryFindOwningMod(Assembly assembly)
        {
            try
            {
                List<ModContentPack> mods =
                    LoadedModManager.RunningModsListForReading;

                for (int i = 0; i < mods.Count; i++)
                {
                    ModContentPack mod = mods[i];
                    if (mod?.assemblies?.loadedAssemblies == null)
                    {
                        continue;
                    }

                    if (mod.assemblies.loadedAssemblies.Contains(assembly))
                    {
                        return mod;
                    }
                }
            }
            catch
            {
                // 解析失败不影响拦截逻辑。
            }

            return null;
        }
    }
}
