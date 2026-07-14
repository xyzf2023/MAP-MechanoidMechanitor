using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 在 CheckForErrors 调用期间建立 ThreadStatic 作用域，
    /// 使物资可达性 Lambda 在重组且存在独立领队时扩展机械搬运者资格。
    /// </summary>
    [HarmonyPatch(
        typeof(Dialog_FormCaravan),
        "CheckForErrors",
        new[] { typeof(List<Pawn>) })]
    public static class MAPCaravanCheckForErrorsCollectorScopePatch
    {
        private const string LogPrefix =
            "[MAP-机械族机械师] MAPCaravanItemReachabilityPatches：";

        private const int ErrorKeyReformFieldMissing = 879346001;

        [ThreadStatic]
        private static Stack<bool>? collectorEnabledStack;

        private static FieldInfo? reformField;
        private static bool reformFieldResolved;

        [HarmonyPrefix]
        public static void Prefix(
            Dialog_FormCaravan __instance,
            List<Pawn> pawns,
            out bool __state)
        {
            __state = false;
            bool enabled = false;
            try
            {
                enabled = ShouldEnableCollectorExpansion(__instance, pawns);
            }
            catch (Exception ex)
            {
                Log.Error(
                    $"{LogPrefix}计算机械搬运者作用域失败，本次保留原版行为：{ex}");
                enabled = false;
            }

            GetCollectorEnabledStack().Push(enabled);
            __state = true;
        }

        [HarmonyFinalizer]
        public static Exception? Finalizer(Exception? __exception, bool __state)
        {
            if (__state)
            {
                Stack<bool> stack = GetCollectorEnabledStack();
                if (stack.Count > 0)
                {
                    stack.Pop();
                }
            }

            return __exception;
        }

        /// <summary>
        /// 替换原版物资可达性 Lambda 中的 Pawn.IsColonist 判断。
        /// </summary>
        public static bool IsCollectorForCurrentCheck(Pawn? pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            if (!IsCollectorScopeEnabled())
            {
                return pawn.IsColonist;
            }

            return MAPTravelUtility.CanActAsCaravanCollector(pawn);
        }

        private static bool IsCollectorScopeEnabled()
        {
            Stack<bool>? stack = collectorEnabledStack;
            return stack != null && stack.Count > 0 && stack.Peek();
        }

        private static Stack<bool> GetCollectorEnabledStack()
        {
            return collectorEnabledStack ??= new Stack<bool>();
        }

        private static bool ShouldEnableCollectorExpansion(
            Dialog_FormCaravan? dialog,
            List<Pawn>? pawns)
        {
            if (dialog == null || pawns == null || pawns.Count == 0)
            {
                return false;
            }

            if (!TryGetReform(dialog, out bool reform) || !reform)
            {
                return false;
            }

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn? pawn = pawns[i];
                if (pawn != null
                    && MAPTravelUtility.CanActAsIndependentCaravanOwner(pawn))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetReform(Dialog_FormCaravan dialog, out bool reform)
        {
            reform = false;
            FieldInfo? field = GetReformField();
            if (field == null)
            {
                return false;
            }

            try
            {
                object? value = field.GetValue(dialog);
                if (value is bool typed)
                {
                    reform = typed;
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"{LogPrefix}读取 Dialog_FormCaravan.reform 失败：{ex}");
            }

            return false;
        }

        private static FieldInfo? GetReformField()
        {
            if (reformFieldResolved)
            {
                return reformField;
            }

            reformFieldResolved = true;
            reformField = AccessTools.Field(typeof(Dialog_FormCaravan), "reform");
            if (reformField == null)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}未找到 Dialog_FormCaravan.reform 字段，机械搬运者扩展未启用。",
                    ErrorKeyReformFieldMissing);
            }

            return reformField;
        }
    }

    /// <summary>
    /// 仅改写 CheckForErrors 物资可达性 Lambda 中的一处 IsColonist 调用。
    /// </summary>
    [HarmonyPatch]
    public static class MAPCaravanCheckForErrorsReachabilityColonistPatch
    {
        private const string LogPrefix =
            "[MAP-机械族机械师] MAPCaravanItemReachabilityPatches：";

        private const int ErrorKeyTargetNotFound = 879346101;
        private const int ErrorKeyAmbiguousTarget = 879346102;
        private const int ErrorKeyResolveMembers = 879346103;
        private const int ErrorKeyMatchCount = 879346104;
        private const int ErrorKeyReadInstructions = 879346105;

        private static MethodBase? cachedTargetMethod;
        private static bool targetMethodResolved;

        private static bool Prepare()
        {
            return TargetMethod() != null;
        }

        private static MethodBase? TargetMethod()
        {
            if (targetMethodResolved)
            {
                return cachedTargetMethod;
            }

            targetMethodResolved = true;
            cachedTargetMethod = ResolveReachabilityColonistLambda();
            return cachedTargetMethod;
        }

        private static MethodBase? ResolveReachabilityColonistLambda()
        {
            MethodInfo? checkForErrors = AccessTools.Method(
                typeof(Dialog_FormCaravan),
                "CheckForErrors",
                new[] { typeof(List<Pawn>) });
            if (checkForErrors == null)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}未找到 Dialog_FormCaravan.CheckForErrors(List<Pawn>)，物资可达性补丁未应用。",
                    ErrorKeyTargetNotFound);
                return null;
            }

            HashSet<MethodInfo> candidates = new HashSet<MethodInfo>();
            CollectReferencedBoolPawnMethods(checkForErrors, candidates);
            CollectDeclaredBoolPawnMethods(typeof(Dialog_FormCaravan), candidates);

            List<MethodInfo> matches = new List<MethodInfo>();
            foreach (MethodInfo candidate in candidates)
            {
                if (IsReachabilityColonistPredicate(candidate))
                {
                    matches.Add(candidate);
                }
            }

            if (matches.Count == 0)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}未定位到 CheckForErrors 物资可达性 IsColonist Lambda，补丁未应用。",
                    ErrorKeyTargetNotFound);
                return null;
            }

            if (matches.Count > 1)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}定位到多个物资可达性 IsColonist Lambda 候选（{matches.Count}），补丁未应用。",
                    ErrorKeyAmbiguousTarget);
                return null;
            }

            return matches[0];
        }

        private static void CollectReferencedBoolPawnMethods(
            MethodInfo checkForErrors,
            HashSet<MethodInfo> candidates)
        {
            List<CodeInstruction>? instructions = TryReadInstructions(checkForErrors);
            if (instructions == null)
            {
                return;
            }

            for (int i = 0; i < instructions.Count; i++)
            {
                if (instructions[i].operand is MethodInfo method
                    && IsBoolPawnPredicate(method))
                {
                    candidates.Add(method);
                }
            }
        }

        private static void CollectDeclaredBoolPawnMethods(
            Type type,
            HashSet<MethodInfo> candidates)
        {
            MethodInfo[] methods;
            try
            {
                methods = type.GetMethods(
                    BindingFlags.Instance
                    | BindingFlags.Static
                    | BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}反射枚举 {type.FullName} 方法失败：{ex}",
                    ErrorKeyReadInstructions);
                return;
            }

            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (IsBoolPawnPredicate(method))
                {
                    candidates.Add(method);
                }
            }

            Type[] nestedTypes;
            try
            {
                nestedTypes = type.GetNestedTypes(
                    BindingFlags.Public | BindingFlags.NonPublic);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}反射枚举 {type.FullName} 嵌套类型失败：{ex}",
                    ErrorKeyReadInstructions);
                return;
            }

            for (int i = 0; i < nestedTypes.Length; i++)
            {
                CollectDeclaredBoolPawnMethods(nestedTypes[i], candidates);
            }
        }

        private static bool IsBoolPawnPredicate(MethodInfo method)
        {
            if (method.ReturnType != typeof(bool))
            {
                return false;
            }

            ParameterInfo[] parameters = method.GetParameters();
            return parameters.Length == 1
                && parameters[0].ParameterType == typeof(Pawn);
        }

        private static bool IsReachabilityColonistPredicate(MethodInfo method)
        {
            if (!IsBoolPawnPredicate(method))
            {
                return false;
            }

            MethodInfo? isColonistGetter = AccessTools.PropertyGetter(
                typeof(Pawn),
                nameof(Pawn.IsColonist));
            if (isColonistGetter == null)
            {
                return false;
            }

            List<CodeInstruction>? instructions = TryReadInstructions(method);
            if (instructions == null)
            {
                return false;
            }

            bool hasIsColonist = false;
            bool hasCanReach = false;
            for (int i = 0; i < instructions.Count; i++)
            {
                CodeInstruction instruction = instructions[i];
                if (instruction.Calls(isColonistGetter))
                {
                    hasIsColonist = true;
                }

                if (instruction.operand is MethodInfo called
                    && IsPawnCanReachMethod(called))
                {
                    hasCanReach = true;
                }
            }

            return hasIsColonist && hasCanReach;
        }

        private static bool IsPawnCanReachMethod(MethodInfo method)
        {
            return method.Name == nameof(ReachabilityUtility.CanReach)
                && method.DeclaringType == typeof(ReachabilityUtility);
        }

        private static List<CodeInstruction>? TryReadInstructions(MethodBase method)
        {
            try
            {
                return new List<CodeInstruction>(
                    PatchProcessor.GetOriginalInstructions(method));
            }
            catch (Exception ex)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}无法通过 PatchProcessor 读取方法 IL：" +
                    $"{method.DeclaringType?.FullName}.{method.Name}：{ex}",
                    ErrorKeyReadInstructions);
                return null;
            }
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            MethodInfo? isColonistGetter = AccessTools.PropertyGetter(
                typeof(Pawn),
                nameof(Pawn.IsColonist));
            MethodInfo? helperMethod = AccessTools.Method(
                typeof(MAPCaravanCheckForErrorsCollectorScopePatch),
                nameof(MAPCaravanCheckForErrorsCollectorScopePatch.IsCollectorForCurrentCheck));

            if (isColonistGetter == null || helperMethod == null)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}无法解析 IsColonist / IsCollectorForCurrentCheck，补丁未应用。",
                    ErrorKeyResolveMembers);
                return codes;
            }

            int matchCount = 0;
            int matchIndex = -1;
            for (int i = 0; i < codes.Count; i++)
            {
                if (!codes[i].Calls(isColonistGetter))
                {
                    continue;
                }

                matchCount++;
                matchIndex = i;
            }

            if (matchCount != 1)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}物资可达性 Lambda 中 IsColonist 预期仅 1 处，实际找到 {matchCount} 处，补丁未应用。",
                    ErrorKeyMatchCount);
                return codes;
            }

            CodeInstruction instruction = codes[matchIndex];
            instruction.opcode = OpCodes.Call;
            instruction.operand = helperMethod;
            return codes;
        }
    }
}
