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

            if (!TryResolveUniqueType(
                    mod,
                    typeFullName,
                    out Type? targetType,
                    out string typeFailure))
            {
                failureReason = typeFailure;
                return false;
            }

            BindingFlags flags =
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly;
            MethodInfo[] methods;

            try
            {
                methods = targetType!.GetMethods(flags);
            }
            catch (Exception ex)
            {
                failureReason =
                    $"读取类型{typeFullName}的方法时发生异常：" +
                    $"{ex.GetType().Name}：{ex.Message}";
                return false;
            }

            List<MethodInfo> matchingMethods = new List<MethodInfo>();
            List<string> sameNameMethods = new List<string>();
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo candidate = methods[i];
                if (string.Equals(
                        candidate.Name,
                        methodName,
                        StringComparison.Ordinal))
                {
                    sameNameMethods.Add(FormatMethod(candidate));
                }

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
                    $"实际找到{matchingMethods.Count}个。" +
                    FormatCandidates("同名方法", sameNameMethods);
                return false;
            }

            method = matchingMethods[0];
            return true;
        }

        /// <summary>
        /// 从目标 MOD 的全部已加载程序集中按完整类型名精确解析类型。
        /// 必须恰好唯一，不 fallback 到“随便找一个同名类型”。
        /// </summary>
        public static bool TryResolveUniqueType(
            ModContentPack mod,
            string typeFullName,
            out Type? type,
            out string failureReason)
        {
            type = null;
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
                List<string> assemblyNames = new List<string>();
                for (int i = 0; i < assemblies.Count; i++)
                {
                    assemblyNames.Add(assemblies[i].FullName);
                }

                failureReason =
                    $"预期在目标MOD程序集中唯一找到类型{typeFullName}，" +
                    $"实际找到{matchingTypes.Count}个。" +
                    FormatCandidates("目标MOD已加载程序集", assemblyNames);
                return false;
            }

            type = matchingTypes[0];
            return true;
        }

        /// <summary>
        /// 在指定类型上按精确参数类型列表解析构造函数，必须恰好唯一。
        /// optional 参数不会减少 CLR 参数数量，调用方必须按真实 CLR 签名传入。
        /// </summary>
        public static bool TryResolveUniqueConstructor(
            Type type,
            Type[] parameterTypes,
            out ConstructorInfo? constructor,
            out string failureReason)
        {
            constructor = null;
            failureReason = string.Empty;

            if (type == null)
            {
                failureReason = "目标类型为空。";
                return false;
            }

            ConstructorInfo[] constructors;
            BindingFlags flags =
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly;

            try
            {
                constructors = type.GetConstructors(flags);
            }
            catch (Exception ex)
            {
                failureReason =
                    $"读取类型{type.FullName}的构造函数时发生异常：" +
                    $"{ex.GetType().Name}：{ex.Message}";
                return false;
            }

            List<ConstructorInfo> matchingConstructors = new List<ConstructorInfo>();
            List<string> availableConstructors = new List<string>();
            for (int i = 0; i < constructors.Length; i++)
            {
                ConstructorInfo candidate = constructors[i];
                availableConstructors.Add(FormatConstructor(candidate));
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
                    matchingConstructors.Add(candidate);
                }
            }

            if (matchingConstructors.Count != 1)
            {
                failureReason =
                    $"预期在{type.FullName}中唯一找到构造函数" +
                    $"{FormatSignature(".ctor", typeof(void), parameterTypes)}，" +
                    $"实际找到{matchingConstructors.Count}个。" +
                    FormatCandidates("实际构造函数", availableConstructors);
                return false;
            }

            constructor = matchingConstructors[0];
            return true;
        }

        /// <summary>
        /// 在指定类型上按字段名与精确字段类型解析字段，必须恰好唯一。
        /// 由调用方通过 bindingFlags 明确限定可见性与 DeclaredOnly 范围。
        /// </summary>
        public static bool TryResolveUniqueField(
            Type type,
            string fieldName,
            Type expectedFieldType,
            BindingFlags bindingFlags,
            out FieldInfo? field,
            out string failureReason)
        {
            field = null;
            failureReason = string.Empty;

            if (type == null)
            {
                failureReason = "目标类型为空。";
                return false;
            }

            if (string.IsNullOrEmpty(fieldName))
            {
                failureReason = "字段名为空。";
                return false;
            }

            if (expectedFieldType == null)
            {
                failureReason = "预期字段类型为空。";
                return false;
            }

            FieldInfo[] fields;

            try
            {
                fields = type.GetFields(bindingFlags);
            }
            catch (Exception ex)
            {
                failureReason =
                    $"读取类型{type.FullName}的字段时发生异常：" +
                    $"{ex.GetType().Name}：{ex.Message}";
                return false;
            }

            List<FieldInfo> matchingFields = new List<FieldInfo>();
            List<string> sameNameFields = new List<string>();
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo candidate = fields[i];
                if (string.Equals(candidate.Name, fieldName, StringComparison.Ordinal))
                {
                    sameNameFields.Add(
                        FormatType(candidate.FieldType)
                        + " "
                        + (candidate.DeclaringType?.FullName ?? "<无声明类型>")
                        + "."
                        + candidate.Name);
                }

                if (string.Equals(candidate.Name, fieldName, StringComparison.Ordinal)
                    && candidate.FieldType == expectedFieldType)
                {
                    matchingFields.Add(candidate);
                }
            }

            if (matchingFields.Count != 1)
            {
                failureReason =
                    $"预期在{type.FullName}中唯一找到字段" +
                    $"{fieldName}（{FormatType(expectedFieldType)}），" +
                    $"实际找到{matchingFields.Count}个。" +
                    FormatCandidates("同名字段", sameNameFields);
                return false;
            }

            field = matchingFields[0];
            return true;
        }

        private static string FormatCandidates(
            string label,
            List<string> candidates)
        {
            if (candidates.Count == 0)
            {
                return $" {label}：无。";
            }

            const int maxEntries = 16;
            int count = Math.Min(candidates.Count, maxEntries);
            string[] displayed = new string[count];
            for (int i = 0; i < count; i++)
            {
                displayed[i] = candidates[i];
            }

            string suffix =
                candidates.Count > maxEntries
                    ? $"；另有{candidates.Count - maxEntries}项已省略"
                    : string.Empty;
            return $" {label}：{string.Join("；", displayed)}{suffix}。";
        }

        private static string FormatConstructor(ConstructorInfo constructor)
        {
            ParameterInfo[] parameters = constructor.GetParameters();
            Type[] parameterTypes = new Type[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                parameterTypes[i] = parameters[i].ParameterType;
            }

            return FormatSignature(".ctor", typeof(void), parameterTypes);
        }

        private static string FormatMethod(MethodInfo method)
        {
            ParameterInfo[] parameters = method.GetParameters();
            Type[] parameterTypes = new Type[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                parameterTypes[i] = parameters[i].ParameterType;
            }

            return
                (method.IsStatic ? "static " : "instance ")
                + FormatSignature(
                    method.Name,
                    method.ReturnType,
                    parameterTypes);
        }

        private static string FormatType(Type type)
        {
            return type.FullName ?? type.Name;
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
