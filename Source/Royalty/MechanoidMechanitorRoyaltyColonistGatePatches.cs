using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(
        typeof(Pawn_RoyaltyTracker),
        nameof(Pawn_RoyaltyTracker.ApplyRewardsForTitle))]
    public static class Patch_PawnRoyaltyTracker_ApplyRewardsForTitle_Mechanitor
    {
        private const int ErrorKeyMatchCount = 927451001;

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return MechanoidMechanitorRoyaltyColonistGateTranspiler
                .ReplaceSingleIsColonistCall(
                    instructions,
                    nameof(Pawn_RoyaltyTracker.ApplyRewardsForTitle),
                    ErrorKeyMatchCount);
        }
    }

    [HarmonyPatch]
    public static class Patch_PawnRoyaltyTracker_OnPreTitleChanged_Mechanitor
    {
        private const int ErrorKeyMatchCount = 927451002;

        private static MethodBase? TargetMethod()
        {
            return AccessTools.Method(
                typeof(Pawn_RoyaltyTracker),
                "OnPreTitleChanged");
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return MechanoidMechanitorRoyaltyColonistGateTranspiler
                .ReplaceSingleIsColonistCall(
                    instructions,
                    "Pawn_RoyaltyTracker.OnPreTitleChanged",
                    ErrorKeyMatchCount);
        }
    }

    internal static class MechanoidMechanitorRoyaltyColonistGateTranspiler
    {
        private const string LogPrefix =
            "[MAP-机械族机械师] RoyaltyColonistGatePatches：";

        private const int ErrorKeyResolveMembers = 927451000;

        internal static IEnumerable<CodeInstruction>
            ReplaceSingleIsColonistCall(
                IEnumerable<CodeInstruction> instructions,
                string targetMethodName,
                int errorKeyMatchCount)
        {
            List<CodeInstruction> codes =
                new List<CodeInstruction>(instructions);

            MethodInfo? isColonistGetter = AccessTools.PropertyGetter(
                typeof(Pawn),
                nameof(Pawn.IsColonist));
            MethodInfo? helperMethod = AccessTools.Method(
                typeof(MechanoidMechanitorRoyaltyUtility),
                nameof(MechanoidMechanitorRoyaltyUtility
                    .IsColonistOrSupportedMechanicalRoyal));

            if (isColonistGetter == null || helperMethod == null)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}无法解析 Pawn.IsColonist / " +
                    "IsColonistOrSupportedMechanicalRoyal，" +
                    $"目标方法 {targetMethodName} 未应用机械贵族补丁。",
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
                    $"{LogPrefix}{targetMethodName} 中 Pawn.IsColonist 属性读取方法 " +
                    $"预期仅 1 处，实际匹配 {matchCount} 处，" +
                    "未应用机械贵族补丁。",
                    errorKeyMatchCount);
                return codes;
            }

            CodeInstruction instruction = codes[matchIndex];
            instruction.opcode = OpCodes.Call;
            instruction.operand = helperMethod;
            return codes;
        }
    }
}
