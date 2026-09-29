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
    /// 修改 TryFindExitSpot 外层方法中的出口可达人数统计，
    /// 让机械族旅行成员参与出口选择。
    /// </summary>
    [HarmonyPatch]
    public static class MAPDialogFormCaravanExitSpotOuterPatch
    {
        private const int ErrorKeyMatchCount = 879349101;

        private static bool Prepare()
        {
            return TargetMethod() != null;
        }

        private static MethodBase? TargetMethod()
        {
            return MAPCaravanExitSpotPatchUtility.GetOuterMethod();
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return MAPCaravanExitSpotPatchUtility
                .ReplaceIsColonistCalls(
                    instructions,
                    expectedCount: 1,
                    "Dialog_FormCaravan.TryFindExitSpot 外层出口统计",
                    ErrorKeyMatchCount);
        }
    }

    /// <summary>
    /// 修改 TryFindExitSpot 使用的“全员可达候选出口”Lambda，
    /// 让机械族旅行成员参与全员出口可达性检查。
    /// </summary>
    [HarmonyPatch]
    public static class MAPDialogFormCaravanExitSpotPredicatePatch
    {
        private const int ErrorKeyMatchCount = 879349102;

        private static bool Prepare()
        {
            return TargetMethod() != null;
        }

        private static MethodBase? TargetMethod()
        {
            return MAPCaravanExitSpotPatchUtility
                .GetAllReachablePredicateMethod();
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return MAPCaravanExitSpotPatchUtility
                .ReplaceIsColonistCalls(
                    instructions,
                    expectedCount: 1,
                    "Dialog_FormCaravan.TryFindExitSpot 全员可达 Lambda",
                    ErrorKeyMatchCount);
        }
    }

    internal static class MAPCaravanExitSpotPatchUtility
    {
        private const string LogPrefix =
            "[MAP-机械族机械师] MAPCaravanExitSpotPatches：";

        private const int ErrorKeyOuterMethodMissing = 879349021;
        private const int ErrorKeyPredicateMissing = 879349022;
        private const int ErrorKeyPredicateAmbiguous = 879349023;
        private const int ErrorKeyReadInstructions = 879349004;
        private const int ErrorKeyResolveMembers = 879349005;

        private static MethodBase? cachedOuterMethod;
        private static bool outerMethodResolved;

        private static MethodBase? cachedPredicateMethod;
        private static bool predicateMethodResolved;

        internal static MethodBase? GetOuterMethod()
        {
            if (outerMethodResolved)
            {
                return cachedOuterMethod;
            }

            outerMethodResolved = true;

            cachedOuterMethod = AccessTools.Method(
                typeof(Dialog_FormCaravan),
                "TryFindExitSpot",
                new[]
                {
                    typeof(List<Pawn>),
                    typeof(bool),
                    typeof(Rot4),
                    typeof(IntVec3).MakeByRefType()
                });

            if (cachedOuterMethod == null)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}未找到精确重载 "
                    + "Dialog_FormCaravan.TryFindExitSpot("
                    + "List<Pawn>, bool, Rot4, out IntVec3)，"
                    + "出口可达性补丁未应用。",
                    ErrorKeyOuterMethodMissing);
            }

            return cachedOuterMethod;
        }

        internal static MethodBase?
            GetAllReachablePredicateMethod()
        {
            if (predicateMethodResolved)
            {
                return cachedPredicateMethod;
            }

            predicateMethodResolved = true;

            MethodBase? outerMethod = GetOuterMethod();
            if (outerMethod == null)
            {
                return null;
            }

            List<CodeInstruction>? outerInstructions =
                TryReadInstructions(outerMethod);

            if (outerInstructions == null)
            {
                return null;
            }

            HashSet<MethodInfo> candidates =
                new HashSet<MethodInfo>();

            for (int i = 0; i < outerInstructions.Count; i++)
            {
                if (outerInstructions[i].operand is MethodInfo method
                    && IsBoolIntVec3Predicate(method))
                {
                    candidates.Add(method);
                }
            }

            List<MethodInfo> matches =
                new List<MethodInfo>();

            foreach (MethodInfo candidate in candidates)
            {
                if (IsExitReachabilityPredicate(candidate))
                {
                    matches.Add(candidate);
                }
            }

            if (matches.Count == 0)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}未定位到 TryFindExitSpot "
                    + "直接引用的全员出口可达性 Lambda，"
                    + "该部分补丁未应用。",
                    ErrorKeyPredicateMissing);

                return null;
            }

            if (matches.Count > 1)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}定位到多个 TryFindExitSpot "
                    + $"出口可达性 Lambda 候选（{matches.Count}），"
                    + "为避免误补丁，本次不应用该部分补丁。",
                    ErrorKeyPredicateAmbiguous);

                return null;
            }

            cachedPredicateMethod = matches[0];
            return cachedPredicateMethod;
        }

        private static bool IsBoolIntVec3Predicate(
            MethodInfo method)
        {
            if (method.ReturnType != typeof(bool))
            {
                return false;
            }

            ParameterInfo[] parameters =
                method.GetParameters();

            return parameters.Length == 1
                && parameters[0].ParameterType
                    == typeof(IntVec3);
        }

        private static bool IsExitReachabilityPredicate(
            MethodInfo method)
        {
            List<CodeInstruction>? instructions =
                TryReadInstructions(method);

            if (instructions == null)
            {
                return false;
            }

            MethodInfo? isColonistGetter =
                AccessTools.PropertyGetter(
                    typeof(Pawn),
                    nameof(Pawn.IsColonist));

            if (isColonistGetter == null)
            {
                return false;
            }

            bool hasIsColonist = false;
            bool hasCanReach = false;

            for (int i = 0; i < instructions.Count; i++)
            {
                CodeInstruction instruction =
                    instructions[i];

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

        private static bool IsPawnCanReachMethod(
            MethodInfo method)
        {
            return method.Name
                    == nameof(ReachabilityUtility.CanReach)
                && method.DeclaringType
                    == typeof(ReachabilityUtility);
        }

        private static List<CodeInstruction>?
            TryReadInstructions(MethodBase method)
        {
            try
            {
                return new List<CodeInstruction>(
                    PatchProcessor
                        .GetOriginalInstructions(method));
            }
            catch (Exception ex)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}无法读取方法 IL："
                    + $"{method.DeclaringType?.FullName}."
                    + $"{method.Name}：{ex}",
                    ErrorKeyReadInstructions);

                return null;
            }
        }

        internal static IEnumerable<CodeInstruction>
            ReplaceIsColonistCalls(
                IEnumerable<CodeInstruction> instructions,
                int expectedCount,
                string targetDescription,
                int errorKeyMatchCount)
        {
            List<CodeInstruction> codes =
                new List<CodeInstruction>(instructions);

            MethodInfo? isColonistGetter =
                AccessTools.PropertyGetter(
                    typeof(Pawn),
                    nameof(Pawn.IsColonist));

            MethodInfo? helperMethod =
                AccessTools.Method(
                    typeof(MAPTravelUtility),
                    nameof(
                        MAPTravelUtility
                            .ShouldCheckCaravanExitReachability));

            if (isColonistGetter == null
                || helperMethod == null)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}无法解析 Pawn.IsColonist 或 "
                    + "MAPTravelUtility."
                    + "ShouldCheckCaravanExitReachability，"
                    + $"{targetDescription} 保留原版行为。",
                    ErrorKeyResolveMembers);

                return codes;
            }

            List<int> matchIndices =
                new List<int>();

            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].Calls(isColonistGetter))
                {
                    matchIndices.Add(i);
                }
            }

            if (matchIndices.Count != expectedCount)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}{targetDescription} 中 "
                    + "Pawn.IsColonist 属性读取方法 "
                    + $"预期匹配 {expectedCount} 处，"
                    + $"实际匹配 {matchIndices.Count} 处。"
                    + "本次不进行替换并保留原版行为。",
                    errorKeyMatchCount);

                return codes;
            }

            for (int i = 0; i < matchIndices.Count; i++)
            {
                CodeInstruction instruction =
                    codes[matchIndices[i]];

                instruction.opcode = OpCodes.Call;
                instruction.operand = helperMethod;
            }

            return codes;
        }
    }
}
