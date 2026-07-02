using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(FloatMenuOptionProvider), nameof(FloatMenuOptionProvider.SelectedPawnValid))]
    public static class ColonistLikeFloatMenuPatches
    {
        private const string LogPrefix = "[MAP_MechanoidMechanitor] ColonistLikeFloatMenuPatches:";

        private const int ErrorKeyMechanoidGateResolveFailed = 879345301;
        private const int ErrorKeyMechanoidGateMatchCount = 879345302;

        public static bool PassesMechanoidSelectedPawnCheck(bool mechanoidCanDo, Pawn pawn)
        {
            if (mechanoidCanDo)
            {
                return true;
            }

            if (!pawn.RaceProps.IsMechanoid)
            {
                return true;
            }

            return CompColonistLikeFloatMenuUser.PawnCanUseColonistLikeFloatMenu(pawn);
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            MethodInfo? mechanoidCanDoGetter = AccessTools.PropertyGetter(
                typeof(FloatMenuOptionProvider),
                "MechanoidCanDo");
            MethodInfo? racePropsGetter = AccessTools.PropertyGetter(
                typeof(Pawn),
                nameof(Pawn.RaceProps));
            MethodInfo? isMechanoidGetter = AccessTools.PropertyGetter(
                typeof(RaceProperties),
                nameof(RaceProperties.IsMechanoid));
            MethodInfo? helperMethod = AccessTools.Method(
                typeof(ColonistLikeFloatMenuPatches),
                nameof(PassesMechanoidSelectedPawnCheck));

            if (mechanoidCanDoGetter == null
                || racePropsGetter == null
                || isMechanoidGetter == null
                || helperMethod == null)
            {
                Log.ErrorOnce(
                    $"{LogPrefix} could not resolve SelectedPawnValid mechanoid-gate methods. Patch not applied.",
                    ErrorKeyMechanoidGateResolveFailed);
                return codes;
            }

            int matchCount = 0;
            int matchIndex = -1;

            for (int i = 0; i < codes.Count - 5; i++)
            {
                if (!codes[i].Calls(mechanoidCanDoGetter))
                {
                    continue;
                }

                if (!IsUnconditionalBranch(codes[i + 1])
                    || codes[i + 2].opcode != OpCodes.Ldarg_1
                    || !codes[i + 3].Calls(racePropsGetter)
                    || !codes[i + 4].Calls(isMechanoidGetter)
                    || !IsUnconditionalBranch(codes[i + 5]))
                {
                    continue;
                }

                matchCount++;
                matchIndex = i;
            }

            if (matchCount != 1)
            {
                Log.ErrorOnce(
                    $"{LogPrefix} expected exactly one mechanoid gate in FloatMenuOptionProvider.SelectedPawnValid, found {matchCount}. Patch not applied.",
                    ErrorKeyMechanoidGateMatchCount);
                return codes;
            }

            CodeInstruction skipTarget = codes[matchIndex + 1];
            object? skipLabel = skipTarget.operand;
            OpCode branchOpcode = skipTarget.opcode == OpCodes.Brtrue_S
                || skipTarget.opcode == OpCodes.Brfalse_S
                ? OpCodes.Brtrue_S
                : OpCodes.Brtrue;

            codes[matchIndex + 1] = new CodeInstruction(OpCodes.Ldarg_1);
            codes[matchIndex + 2] = new CodeInstruction(OpCodes.Call, helperMethod);
            codes[matchIndex + 3] = new CodeInstruction(branchOpcode, skipLabel);
            codes.RemoveAt(matchIndex + 4);
            codes.RemoveAt(matchIndex + 4);

            return codes;
        }

        private static bool IsUnconditionalBranch(CodeInstruction instruction)
        {
            return instruction.opcode == OpCodes.Brtrue
                || instruction.opcode == OpCodes.Brtrue_S
                || instruction.opcode == OpCodes.Brfalse
                || instruction.opcode == OpCodes.Brfalse_S;
        }
    }
}
