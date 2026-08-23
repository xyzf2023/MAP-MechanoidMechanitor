using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty
{
    internal static class ThirdPartyCompatibilityTargetResolver
    {
        public static ModContentPack? FindRunningMod(string packageId)
        {
            List<ModContentPack> runningMods =
                LoadedModManager.RunningModsListForReading;
            for (int i = 0; i < runningMods.Count; i++)
            {
                ModContentPack mod = runningMods[i];
                if (string.Equals(
                        mod.PackageId,
                        packageId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return mod;
                }
            }

            return null;
        }

        public static bool TryResolveUniqueInstanceMethod(
            ModContentPack mod,
            string typeFullName,
            string methodName,
            Type returnType,
            Type[] parameterTypes,
            out MethodInfo? method,
            out string failureReason)
        {
            method = null;
            failureReason = string.Empty;

            if (mod == null)
            {
                failureReason = "目标ModContentPack为空。";
                return false;
            }

            List<Type> matchingTypes = new List<Type>();
            List<Assembly> assemblies = mod.assemblies.loadedAssemblies;
            for (int i = 0; i < assemblies.Count; i++)
            {
                Assembly assembly = assemblies[i];
                Type? candidate;

                try
                {
                    candidate = assembly.GetType(
                        typeFullName,
                        throwOnError: false,
                        ignoreCase: false);
                }
                catch (Exception ex)
                {
                    failureReason =
                        $"从目标MOD程序集{assembly.FullName}解析类型" +
                        $"{typeFullName}时发生异常：{ex.GetType().Name}：{ex.Message}";
                    return false;
                }

                if (candidate != null && !matchingTypes.Contains(candidate))
                {
                    matchingTypes.Add(candidate);
                }
            }

            if (matchingTypes.Count != 1)
            {
                failureReason =
                    $"预期在目标MOD程序集中唯一找到类型{typeFullName}，" +
                    $"实际找到{matchingTypes.Count}个。";
                return false;
            }

            Type targetType = matchingTypes[0];
            BindingFlags flags =
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly;
            MethodInfo[] methods;

            try
            {
                methods = targetType.GetMethods(flags);
            }
            catch (Exception ex)
            {
                failureReason =
                    $"读取类型{typeFullName}的方法时发生异常：" +
                    $"{ex.GetType().Name}：{ex.Message}";
                return false;
            }

            List<MethodInfo> matchingMethods = new List<MethodInfo>();
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo candidate = methods[i];
                if (!string.Equals(
                        candidate.Name,
                        methodName,
                        StringComparison.Ordinal)
                    || candidate.IsStatic
                    || candidate.ReturnType != returnType)
                {
                    continue;
                }

                ParameterInfo[] parameters = candidate.GetParameters();
                if (parameters.Length != parameterTypes.Length)
                {
                    continue;
                }

                bool parametersMatch = true;
                for (int j = 0; j < parameters.Length; j++)
                {
                    if (parameters[j].ParameterType != parameterTypes[j])
                    {
                        parametersMatch = false;
                        break;
                    }
                }

                if (parametersMatch)
                {
                    matchingMethods.Add(candidate);
                }
            }

            if (matchingMethods.Count != 1)
            {
                failureReason =
                    $"预期在{typeFullName}中唯一找到实例方法" +
                    $"{FormatSignature(methodName, returnType, parameterTypes)}，" +
                    $"实际找到{matchingMethods.Count}个。";
                return false;
            }

            method = matchingMethods[0];
            return true;
        }

        private static string FormatSignature(
            string methodName,
            Type returnType,
            Type[] parameterTypes)
        {
            string[] parameterNames = new string[parameterTypes.Length];
            for (int i = 0; i < parameterTypes.Length; i++)
            {
                parameterNames[i] =
                    parameterTypes[i].FullName
                    ?? parameterTypes[i].Name;
            }

            return
                $"{returnType.FullName ?? returnType.Name} " +
                $"{methodName}({string.Join(", ", parameterNames)})";
        }
    }
}
