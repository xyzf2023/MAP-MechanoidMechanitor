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
        private const int ErrorKeyMechanoidGateExceptionBlocks = 879345303;

        private const int MechanoidGateReplaceStartOffset = 1;
        private const int MechanoidGateReplaceCount = 5;

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

            if (!TryFindUniqueMechanoidGate(
                    codes,
                    mechanoidCanDoGetter,
                    racePropsGetter,
                    isMechanoidGetter,
                    out int matchIndex,
                    out int matchCount))
            {
                Log.ErrorOnce(
                    $"{LogPrefix} expected exactly one mechanoid gate in FloatMenuOptionProvider.SelectedPawnValid, found {matchCount}. Patch not applied.",
                    ErrorKeyMechanoidGateMatchCount);
                return codes;
            }

            if (!TryReplaceMechanoidGatePreservingMetadata(codes, matchIndex, helperMethod))
            {
                Log.ErrorOnce(
                    $"{LogPrefix} could not safely replace mechanoid gate while preserving labels/exception blocks. Patch not applied.",
                    ErrorKeyMechanoidGateExceptionBlocks);
                return codes;
            }

            return codes;
        }

        private static bool TryFindUniqueMechanoidGate(
            List<CodeInstruction> codes,
            MethodInfo mechanoidCanDoGetter,
            MethodInfo racePropsGetter,
            MethodInfo isMechanoidGetter,
            out int matchIndex,
            out int matchCount)
        {
            matchIndex = -1;
            matchCount = 0;

            for (int i = 0; i <= codes.Count - MechanoidGateReplaceStartOffset - MechanoidGateReplaceCount; i++)
            {
                if (!MatchesMechanoidGateSequence(
                        codes,
                        i,
                        mechanoidCanDoGetter,
                        racePropsGetter,
                        isMechanoidGetter))
                {
                    continue;
                }

                matchCount++;
                matchIndex = i;
            }

            return matchCount == 1;
        }

        private static bool MatchesMechanoidGateSequence(
            List<CodeInstruction> codes,
            int mechanoidCanDoIndex,
            MethodInfo mechanoidCanDoGetter,
            MethodInfo racePropsGetter,
            MethodInfo isMechanoidGetter)
        {
            int branchIfTrueIndex = mechanoidCanDoIndex + 1;
            int loadPawnIndex = mechanoidCanDoIndex + 2;
            int racePropsIndex = mechanoidCanDoIndex + 3;
            int isMechanoidIndex = mechanoidCanDoIndex + 4;
            int branchIfFalseIndex = mechanoidCanDoIndex + 5;

            if (!codes[mechanoidCanDoIndex].Calls(mechanoidCanDoGetter))
            {
                return false;
            }

            if (!IsBranchIfTrue(codes[branchIfTrueIndex]))
            {
                return false;
            }

            if (codes[loadPawnIndex].opcode != OpCodes.Ldarg_1)
            {
                return false;
            }

            if (!codes[racePropsIndex].Calls(racePropsGetter))
            {
                return false;
            }

            if (!codes[isMechanoidIndex].Calls(isMechanoidGetter))
            {
                return false;
            }

            if (!IsBranchIfFalse(codes[branchIfFalseIndex]))
            {
                return false;
            }

            object? firstTarget = codes[branchIfTrueIndex].operand;
            object? secondTarget = codes[branchIfFalseIndex].operand;

            if (!IsValidBranchLabel(firstTarget) || !IsValidBranchLabel(secondTarget))
            {
                return false;
            }

            return ReferenceEquals(firstTarget, secondTarget)
                || firstTarget!.Equals(secondTarget);
        }

        private static bool TryReplaceMechanoidGatePreservingMetadata(
            List<CodeInstruction> codes,
            int matchIndex,
            MethodInfo helperMethod)
        {
            int replaceStart = matchIndex + MechanoidGateReplaceStartOffset;

            if (!CanSafelyReplaceInstructionRange(codes, replaceStart, MechanoidGateReplaceCount))
            {
                return false;
            }

            CodeInstruction branchIfTrue = codes[replaceStart];
            object skipLabel = branchIfTrue.operand!;
            OpCode branchOpcode = branchIfTrue.opcode == OpCodes.Brtrue_S
                ? OpCodes.Brtrue_S
                : OpCodes.Brtrue;

            List<CodeInstruction> replacement = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Call, helperMethod),
                new CodeInstruction(branchOpcode, skipLabel)
            };

            List<CodeInstruction> replacedRange = codes.GetRange(
                replaceStart,
                MechanoidGateReplaceCount);
            TransferEntryLabels(replacedRange, replacement[0]);
            TransferExceptionBlocksForReplacement(replacedRange, replacement);

            codes.RemoveRange(replaceStart, MechanoidGateReplaceCount);
            codes.InsertRange(replaceStart, replacement);
            return true;
        }

        private static bool CanSafelyReplaceInstructionRange(
            List<CodeInstruction> codes,
            int start,
            int count)
        {
            int end = start + count - 1;

            for (int i = start; i <= end; i++)
            {
                for (int blockIndex = 0; blockIndex < codes[i].blocks.Count; blockIndex++)
                {
                    ExceptionBlock block = codes[i].blocks[blockIndex];
                    if (block.blockType == ExceptionBlockType.EndExceptionBlock && i < end)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static void TransferEntryLabels(
            List<CodeInstruction> sourceInstructions,
            CodeInstruction targetFirst)
        {
            for (int i = 0; i < sourceInstructions.Count; i++)
            {
                CodeInstruction source = sourceInstructions[i];
                if (source.labels.Count == 0)
                {
                    continue;
                }

                targetFirst.labels.AddRange(source.labels);
                source.labels.Clear();
            }
        }

        private static void TransferExceptionBlocksForReplacement(
            List<CodeInstruction> replacedRange,
            List<CodeInstruction> replacement)
        {
            CodeInstruction replacementFirst = replacement[0];
            CodeInstruction replacementLast = replacement[replacement.Count - 1];

            for (int i = 0; i < replacedRange.Count; i++)
            {
                CodeInstruction source = replacedRange[i];
                bool isLastSource = i == replacedRange.Count - 1;

                for (int blockIndex = source.blocks.Count - 1; blockIndex >= 0; blockIndex--)
                {
                    ExceptionBlock block = source.blocks[blockIndex];
                    source.blocks.RemoveAt(blockIndex);

                    if (ShouldMoveExceptionBlockToReplacementEntry(block))
                    {
                        replacementFirst.blocks.Add(block);
                    }
                    else if (isLastSource)
                    {
                        replacementLast.blocks.Add(block);
                    }
                }
            }
        }

        private static bool ShouldMoveExceptionBlockToReplacementEntry(ExceptionBlock block)
        {
            switch (block.blockType)
            {
                case ExceptionBlockType.BeginExceptionBlock:
                case ExceptionBlockType.BeginCatchBlock:
                case ExceptionBlockType.BeginExceptFilterBlock:
                case ExceptionBlockType.BeginFaultBlock:
                case ExceptionBlockType.BeginFinallyBlock:
                    return true;
                case ExceptionBlockType.EndExceptionBlock:
                    return false;
                default:
                    return false;
            }
        }

        private static bool IsBranchIfTrue(CodeInstruction instruction)
        {
            return instruction.opcode == OpCodes.Brtrue
                || instruction.opcode == OpCodes.Brtrue_S;
        }

        private static bool IsBranchIfFalse(CodeInstruction instruction)
        {
            return instruction.opcode == OpCodes.Brfalse
                || instruction.opcode == OpCodes.Brfalse_S;
        }

        private static bool IsValidBranchLabel(object? operand)
        {
            return operand is Label;
        }
    }
}
