using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    // UI 修复：自律战争女皇等 CompMechCarrier 持有者在无外部监管者时仍应显示
    // 原版 Carrier 资源 Gizmo 与「释放战争死卫」按钮。
    //
    // 原版 CompMechCarrier.CompGetGizmosExtra 在 GetOverseer() == null 时提前 yield break。
    // 后天机械师节点按设计对外 GetOverseer() 为 null，但仍是合法玩家可控殖民地机械体。
    //
    // 本补丁仅在 Carrier Gizmo 门控的状态机 MoveNext 中，将 GetOverseer 值生产点
    // 替换为局部 resolver；不修改全局 GetOverseer、监管者关系或 Gizmo 实现。
    [HarmonyPatch]
    public static class Patch_CompMechCarrier_CompGetGizmosExtra_CarrierOverseerGate
    {
        private const string LogPrefix = "[MAP-机械族机械师] MechCarrierCompatibilityPatches：";

        private const int ErrorKeyResolveIterator = 879345401;
        private const int ErrorKeyResolveMembers = 879345402;
        private const int ErrorKeyMatchCount = 879345403;
        private const int ErrorKeyExceptionBlock = 879345404;

        private static MethodBase? TargetMethod()
        {
            MethodInfo? iteratorMethod = AccessTools.Method(
                typeof(CompMechCarrier),
                nameof(CompMechCarrier.CompGetGizmosExtra));
            if (iteratorMethod == null)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}未找到 CompMechCarrier.CompGetGizmosExtra，补丁未应用。",
                    ErrorKeyResolveIterator);
                return null;
            }

            MethodInfo? moveNext = ResolveIteratorMoveNext(iteratorMethod);
            if (moveNext == null)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}无法解析 CompMechCarrier.CompGetGizmosExtra 迭代器 MoveNext，补丁未应用。",
                    ErrorKeyResolveIterator);
                return null;
            }

            return moveNext;
        }

        private static bool Prepare()
        {
            return TargetMethod() != null;
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            MethodInfo? getOverseerMethod = AccessTools.Method(
                typeof(MechanitorUtility),
                nameof(MechanitorUtility.GetOverseer),
                new[] { typeof(Pawn) });
            MethodInfo? isColonyMechGetter = AccessTools.PropertyGetter(
                typeof(Pawn),
                nameof(Pawn.IsColonyMech));
            MethodInfo? resolverMethod = AccessTools.Method(
                typeof(Patch_CompMechCarrier_CompGetGizmosExtra_CarrierOverseerGate),
                nameof(ResolveOverseerForCarrierGizmo));

            if (getOverseerMethod == null
                || isColonyMechGetter == null
                || resolverMethod == null)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}无法解析 GetOverseer / IsColonyMech / resolver 成员，补丁未应用。",
                    ErrorKeyResolveMembers);
                return codes;
            }

            List<int> candidates = CollectOverseerGateCandidates(
                codes,
                getOverseerMethod,
                isColonyMechGetter);

            if (candidates.Count != 1)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}CompMechCarrier.CompGetGizmosExtra MoveNext 中 GetOverseer 门控语义锚点预期仅 1 处，实际找到 {candidates.Count} 处，补丁未应用。",
                    ErrorKeyMatchCount);
                return codes;
            }

            int targetIndex = candidates[0];
            CodeInstruction targetInstruction = codes[targetIndex];
            if (targetInstruction.blocks.Count > 0)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}GetOverseer 门控调用位于 exception block 内，补丁未应用。",
                    ErrorKeyExceptionBlock);
                return codes;
            }

            targetInstruction.opcode = OpCodes.Call;
            targetInstruction.operand = resolverMethod;
            return codes;
        }

        private static Pawn? ResolveOverseerForCarrierGizmo(Pawn pawn)
        {
            Pawn? overseer = MechanitorUtility.GetOverseer(pawn);
            if (overseer != null)
            {
                return overseer;
            }

            if (AutonomousMechUtility.IsPlayerAutonomousMech(pawn))
            {
                return pawn;
            }

            return null;
        }

        private static MethodInfo? ResolveIteratorMoveNext(MethodInfo iteratorMethod)
        {
            MethodInfo? moveNext = AccessTools.EnumeratorMoveNext(iteratorMethod);
            if (moveNext != null)
            {
                return moveNext;
            }

            object[] attributes = iteratorMethod.GetCustomAttributes(
                typeof(IteratorStateMachineAttribute),
                inherit: false);
            if (attributes.Length == 0)
            {
                return null;
            }

            if (attributes[0] is not IteratorStateMachineAttribute stateMachineAttribute)
            {
                return null;
            }

            return AccessTools.Method(stateMachineAttribute.StateMachineType, "MoveNext");
        }

        private static List<int> CollectOverseerGateCandidates(
            List<CodeInstruction> codes,
            MethodInfo getOverseerMethod,
            MethodInfo isColonyMechGetter)
        {
            List<int> candidates = new List<int>();
            for (int i = 0; i < codes.Count; i++)
            {
                if (!MatchesOverseerGateAnchor(codes, i, getOverseerMethod, isColonyMechGetter))
                {
                    continue;
                }

                candidates.Add(i);
            }

            return candidates;
        }

        private static bool MatchesOverseerGateAnchor(
            List<CodeInstruction> codes,
            int callIndex,
            MethodInfo getOverseerMethod,
            MethodInfo isColonyMechGetter)
        {
            if (!codes[callIndex].Calls(getOverseerMethod))
            {
                return false;
            }

            if (!HasPawnLoadBeforeCall(codes, callIndex))
            {
                return false;
            }

            if (!HasIsColonyMechCheckBefore(codes, callIndex, isColonyMechGetter))
            {
                return false;
            }

            if (!IsNullCheckImmediatelyAfterCall(codes, callIndex))
            {
                return false;
            }

            if (UsesOverseerResultForMoreThanNullCheck(codes, callIndex))
            {
                return false;
            }

            return true;
        }

        private static bool HasPawnLoadBeforeCall(List<CodeInstruction> codes, int callIndex)
        {
            int searchStart = System.Math.Max(0, callIndex - 4);
            for (int i = callIndex - 1; i >= searchStart; i--)
            {
                if (IsPawnLocalOrArgumentLoad(codes[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasIsColonyMechCheckBefore(
            List<CodeInstruction> codes,
            int callIndex,
            MethodInfo isColonyMechGetter)
        {
            int searchStart = System.Math.Max(0, callIndex - 25);
            for (int i = callIndex - 1; i >= searchStart; i--)
            {
                if (codes[i].Calls(isColonyMechGetter))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsNullCheckImmediatelyAfterCall(List<CodeInstruction> codes, int callIndex)
        {
            int nextIndex = callIndex + 1;
            if (nextIndex >= codes.Count)
            {
                return false;
            }

            if (IsUnconditionalBranch(codes[nextIndex]))
            {
                return false;
            }

            if (IsBranchIfTrue(codes[nextIndex]) || IsBranchIfFalse(codes[nextIndex]))
            {
                return true;
            }

            if (nextIndex + 2 >= codes.Count)
            {
                return false;
            }

            return codes[nextIndex].opcode == OpCodes.Ldnull
                && codes[nextIndex + 1].opcode == OpCodes.Ceq
                && (IsBranchIfTrue(codes[nextIndex + 2]) || IsBranchIfFalse(codes[nextIndex + 2]));
        }

        private static bool UsesOverseerResultForMoreThanNullCheck(List<CodeInstruction> codes, int callIndex)
        {
            int inspectEnd = System.Math.Min(codes.Count, callIndex + 4);
            for (int i = callIndex + 1; i < inspectEnd; i++)
            {
                CodeInstruction instruction = codes[i];
                if (instruction.opcode == OpCodes.Ldnull || instruction.opcode == OpCodes.Ceq)
                {
                    continue;
                }

                if (IsBranchIfTrue(instruction) || IsBranchIfFalse(instruction))
                {
                    return false;
                }

                if (instruction.opcode == OpCodes.Stloc
                    || instruction.opcode == OpCodes.Stloc_0
                    || instruction.opcode == OpCodes.Stloc_1
                    || instruction.opcode == OpCodes.Stloc_2
                    || instruction.opcode == OpCodes.Stloc_3
                    || instruction.opcode == OpCodes.Stloc_S
                    || instruction.opcode == OpCodes.Call
                    || instruction.opcode == OpCodes.Callvirt)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsPawnLocalOrArgumentLoad(CodeInstruction instruction)
        {
            return instruction.opcode == OpCodes.Ldarg_0
                || instruction.opcode == OpCodes.Ldarg_1
                || instruction.opcode == OpCodes.Ldarg_2
                || instruction.opcode == OpCodes.Ldarg_3
                || instruction.opcode == OpCodes.Ldarg
                || instruction.opcode == OpCodes.Ldarg_S
                || instruction.opcode == OpCodes.Ldloc_0
                || instruction.opcode == OpCodes.Ldloc_1
                || instruction.opcode == OpCodes.Ldloc_2
                || instruction.opcode == OpCodes.Ldloc_3
                || instruction.opcode == OpCodes.Ldloc
                || instruction.opcode == OpCodes.Ldloc_S;
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

        private static bool IsUnconditionalBranch(CodeInstruction instruction)
        {
            return instruction.opcode == OpCodes.Br
                || instruction.opcode == OpCodes.Br_S;
        }
    }
}
