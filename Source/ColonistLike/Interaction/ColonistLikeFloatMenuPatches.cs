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
        private const string LogPrefix = "[MAP-机械族机械师] ColonistLikeFloatMenuPatches：";

        private const int ErrorKeyResolveFailed = 879345301;
        private const int ErrorKeyMatchCount = 879345302;
        private const int ErrorKeyExpandFailed = 879345303;

        // Instance method: arg0 = this, arg1 = Pawn pawn (MCP: SelectedPawnValid).
        private const int PawnParameterIndex = 1;

        private static bool IsDisallowedMechanoid(RaceProperties raceProps, Pawn pawn)
        {
            if (pawn == null)
            {
                return raceProps?.IsMechanoid ?? false;
            }

            if (!raceProps.IsMechanoid)
            {
                return false;
            }

            return !CompColonistLikeFloatMenuUser.PawnCanUseColonistLikeFloatMenu(pawn);
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            MethodInfo? racePropsGetter = AccessTools.PropertyGetter(
                typeof(Pawn),
                nameof(Pawn.RaceProps));
            MethodInfo? isMechanoidGetter = AccessTools.PropertyGetter(
                typeof(RaceProperties),
                nameof(RaceProperties.IsMechanoid));
            MethodInfo? helperMethod = AccessTools.Method(
                typeof(ColonistLikeFloatMenuPatches),
                nameof(IsDisallowedMechanoid));

            if (racePropsGetter == null || isMechanoidGetter == null || helperMethod == null)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}无法解析 SelectedPawnValid 机械体门控相关方法，补丁未应用。",
                    ErrorKeyResolveFailed);
                return codes;
            }

            if (!TryFindUniqueMechanoidGateAnchor(
                    codes,
                    racePropsGetter,
                    isMechanoidGetter,
                    out int isMechanoidIndex,
                    out int matchCount))
            {
                Log.ErrorOnce(
                    $"{LogPrefix}FloatMenuOptionProvider.SelectedPawnValid 中 RaceProperties.IsMechanoid 语义锚点预期仅 1 处，实际找到 {matchCount} 处，补丁未应用。",
                    ErrorKeyMatchCount);
                return codes;
            }

            if (!TryExpandIsMechanoidGetter(codes, isMechanoidIndex, helperMethod))
            {
                Log.ErrorOnce(
                    $"{LogPrefix}无法安全扩展 IsMechanoid 值生产点（目标 属性读取方法 上存在 异常处理块），补丁未应用。",
                    ErrorKeyExpandFailed);
                return codes;
            }

            return codes;
        }

        private static bool TryFindUniqueMechanoidGateAnchor(
            List<CodeInstruction> codes,
            MethodInfo racePropsGetter,
            MethodInfo isMechanoidGetter,
            out int isMechanoidIndex,
            out int matchCount)
        {
            isMechanoidIndex = -1;
            matchCount = 0;

            for (int i = 1; i < codes.Count - 1; i++)
            {
                if (!MatchesMechanoidGateAnchor(codes, i, racePropsGetter, isMechanoidGetter))
                {
                    continue;
                }

                matchCount++;
                isMechanoidIndex = i;
            }

            return matchCount == 1;
        }

        private static bool MatchesMechanoidGateAnchor(
            List<CodeInstruction> codes,
            int isMechanoidIndex,
            MethodInfo racePropsGetter,
            MethodInfo isMechanoidGetter)
        {
            int racePropsIndex = isMechanoidIndex - 1;
            int branchIfFalseIndex = isMechanoidIndex + 1;

            if (!codes[isMechanoidIndex].Calls(isMechanoidGetter))
            {
                return false;
            }

            if (!IsBranchIfFalse(codes[branchIfFalseIndex]))
            {
                return false;
            }

            if (!codes[racePropsIndex].Calls(racePropsGetter))
            {
                return false;
            }

            if (!HasPawnLoadBeforeRaceProps(codes, racePropsIndex))
            {
                return false;
            }

            return true;
        }

        private static bool HasPawnLoadBeforeRaceProps(List<CodeInstruction> codes, int racePropsIndex)
        {
            int searchStart = System.Math.Max(0, racePropsIndex - 3);
            for (int i = racePropsIndex - 1; i >= searchStart; i--)
            {
                if (LoadsPawnParameter(codes[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryExpandIsMechanoidGetter(
            List<CodeInstruction> codes,
            int isMechanoidIndex,
            MethodInfo helperMethod)
        {
            if (!CanSafelyInsertBefore(codes, isMechanoidIndex))
            {
                return false;
            }

            CodeInstruction getterInstruction = codes[isMechanoidIndex];
            CodeInstruction loadPawn = CreateLoadPawnParameterInstruction();

            TransferEntryLabels(getterInstruction, loadPawn);

            codes.Insert(isMechanoidIndex, loadPawn);

            CodeInstruction helperCall = codes[isMechanoidIndex + 1];
            helperCall.opcode = OpCodes.Call;
            helperCall.operand = helperMethod;

            return true;
        }

        private static bool LoadsPawnParameter(CodeInstruction instruction)
        {
            return PawnParameterIndex switch
            {
                1 => instruction.opcode == OpCodes.Ldarg_1,
                2 => instruction.opcode == OpCodes.Ldarg_2,
                3 => instruction.opcode == OpCodes.Ldarg_3,
                _ => instruction.opcode == OpCodes.Ldarg
                    && instruction.operand is int index
                    && index == PawnParameterIndex
            };
        }

        private static CodeInstruction CreateLoadPawnParameterInstruction()
        {
            return PawnParameterIndex switch
            {
                0 => new CodeInstruction(OpCodes.Ldarg_0),
                1 => new CodeInstruction(OpCodes.Ldarg_1),
                2 => new CodeInstruction(OpCodes.Ldarg_2),
                3 => new CodeInstruction(OpCodes.Ldarg_3),
                _ => new CodeInstruction(OpCodes.Ldarg, PawnParameterIndex)
            };
        }

        private static void TransferEntryLabels(
            CodeInstruction source,
            CodeInstruction target)
        {
            if (source.labels.Count == 0)
            {
                return;
            }

            target.labels.AddRange(source.labels);
            source.labels.Clear();
        }

        private static bool CanSafelyInsertBefore(List<CodeInstruction> codes, int insertIndex)
        {
            if (insertIndex < 0 || insertIndex >= codes.Count)
            {
                return false;
            }

            return codes[insertIndex].blocks.Count == 0;
        }

        private static bool IsBranchIfFalse(CodeInstruction instruction)
        {
            return instruction.opcode == OpCodes.Brfalse
                || instruction.opcode == OpCodes.Brfalse_S;
        }
    }
}
