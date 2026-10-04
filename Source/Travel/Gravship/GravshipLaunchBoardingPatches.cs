using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(
        typeof(RitualBehaviorWorker_GravshipLaunch),
        nameof(RitualBehaviorWorker_GravshipLaunch.TryExecuteOn))]
    public static class GravshipLaunchBoarding_TryExecuteOn_Patch
    {
        private const string LogPrefix =
            "[MAP-机械族机械师] GravshipLaunchBoardingPatches：";

        private const int ErrorKeyResolveMembers = 879349001;
        private const int ErrorKeyPatternMismatch = 879349002;
        private const int ErrorKeyArgResolve = 879349003;

        /// <summary>
        /// 仅当 Pawn 是 GravshipLaunch 中实际分配的 pilot/copilot 合法机械驾驶员时，
        /// 跳过原版访客离开/动物/机械体自动登船分类。
        /// </summary>
        public static bool ShouldSkipAutomaticBoardingForAssignedPilot(
            Pawn? pawn,
            Precept_Ritual? ritual,
            RitualRoleAssignments? assignments)
        {
            if (!ModsConfig.OdysseyActive)
            {
                return false;
            }

            if (pawn == null || ritual == null || assignments == null)
            {
                return false;
            }

            if (!GravshipLaunchRitualUtility.IsSupportedLaunch(ritual))
            {
                return false;
            }

            if (!GravshipRitualPilotCandidateUtility.IsEligiblePilotCandidate(pawn))
            {
                return false;
            }

            Pawn? pilot = assignments.FirstAssignedPawn("pilot");
            if (ReferenceEquals(pawn, pilot))
            {
                return true;
            }

            Pawn? copilot = assignments.FirstAssignedPawn("copilot");
            return ReferenceEquals(pawn, copilot);
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions,
            MethodBase original)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            MethodInfo? downedGetter = AccessTools.PropertyGetter(
                typeof(Pawn),
                nameof(Pawn.Downed));
            FieldInfo? forceVisitorsField = AccessTools.Field(
                typeof(RitualBehaviorWorker_GravshipLaunch),
                nameof(RitualBehaviorWorker_GravshipLaunch.forceVisitorsToLeave));
            FieldInfo? boardAnimalsField = AccessTools.Field(
                typeof(RitualBehaviorWorker_GravshipLaunch),
                nameof(RitualBehaviorWorker_GravshipLaunch.boardColonyAnimals));
            FieldInfo? boardMechsField = AccessTools.Field(
                typeof(RitualBehaviorWorker_GravshipLaunch),
                nameof(RitualBehaviorWorker_GravshipLaunch.boardColonyMechs));
            MethodInfo? isColonyMechGetter = AccessTools.PropertyGetter(
                typeof(Pawn),
                nameof(Pawn.IsColonyMech));
            MethodInfo? skipHelper = AccessTools.Method(
                typeof(GravshipLaunchBoarding_TryExecuteOn_Patch),
                nameof(ShouldSkipAutomaticBoardingForAssignedPilot));

            if (downedGetter == null
                || forceVisitorsField == null
                || boardAnimalsField == null
                || boardMechsField == null
                || isColonyMechGetter == null
                || skipHelper == null)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}无法解析 TryExecuteOn Transpiler 所需成员，补丁未应用。",
                    ErrorKeyResolveMembers);
                return codes;
            }

            if (!TryResolveArgumentIndices(
                    original,
                    out int ritualArgIndex,
                    out int assignmentsArgIndex))
            {
                return codes;
            }

            List<DownedBranchMatch> matches = new List<DownedBranchMatch>();
            for (int i = 0; i < codes.Count; i++)
            {
                if (!codes[i].Calls(downedGetter))
                {
                    continue;
                }

                if (i == 0 || !codes[i - 1].IsLdloc())
                {
                    continue;
                }

                if (i + 1 >= codes.Count
                    || (codes[i + 1].opcode != OpCodes.Brtrue
                        && codes[i + 1].opcode != OpCodes.Brtrue_S)
                    || codes[i + 1].operand is not Label continueLabel)
                {
                    continue;
                }

                int bodyStart = i + 2;
                int bodyEnd = FindLabelTargetIndex(codes, bodyStart, continueLabel);
                if (bodyEnd < 0 || bodyStart >= bodyEnd)
                {
                    continue;
                }

                if (!ContainsFieldLoad(codes, bodyStart, bodyEnd, forceVisitorsField)
                    || !ContainsFieldLoad(codes, bodyStart, bodyEnd, boardAnimalsField)
                    || !ContainsFieldLoad(codes, bodyStart, bodyEnd, boardMechsField)
                    || !ContainsMethodCall(codes, bodyStart, bodyEnd, isColonyMechGetter))
                {
                    continue;
                }

                matches.Add(new DownedBranchMatch
                {
                    LoadPawnInstruction = codes[i - 1],
                    InsertIndex = bodyStart,
                    ContinueLabel = continueLabel
                });
            }

            if (matches.Count != 1)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}TryExecuteOn 中 Downed→自动登船分类锚点预期仅 1 组，实际 {matches.Count} 组，补丁未应用。",
                    ErrorKeyPatternMismatch);
                return codes;
            }

            ApplyInjection(
                codes,
                matches[0],
                ritualArgIndex,
                assignmentsArgIndex,
                skipHelper);
            return codes;
        }

        private static void ApplyInjection(
            List<CodeInstruction> codes,
            DownedBranchMatch match,
            int ritualArgIndex,
            int assignmentsArgIndex,
            MethodInfo skipHelper)
        {
            int insertIndex = match.InsertIndex;
            CodeInstruction firstBody = codes[insertIndex];

            CodeInstruction loadPawn = new CodeInstruction(
                match.LoadPawnInstruction.opcode,
                match.LoadPawnInstruction.operand);

            // 将进入原分支体的跳转目标移到注入序列首位。
            if (firstBody.labels.Count > 0)
            {
                loadPawn.labels.AddRange(firstBody.labels);
                firstBody.labels.Clear();
            }

            if (firstBody.blocks.Count > 0)
            {
                loadPawn.blocks.AddRange(firstBody.blocks);
                firstBody.blocks.Clear();
            }

            CodeInstruction loadRitual = CreateLdarg(ritualArgIndex);
            CodeInstruction loadAssignments = CreateLdarg(assignmentsArgIndex);
            CodeInstruction callHelper = new CodeInstruction(OpCodes.Call, skipHelper);
            CodeInstruction branchContinue = new CodeInstruction(
                OpCodes.Brtrue,
                match.ContinueLabel);

            codes.Insert(insertIndex, loadPawn);
            codes.Insert(insertIndex + 1, loadRitual);
            codes.Insert(insertIndex + 2, loadAssignments);
            codes.Insert(insertIndex + 3, callHelper);
            codes.Insert(insertIndex + 4, branchContinue);
        }

        private static bool TryResolveArgumentIndices(
            MethodBase original,
            out int ritualArgIndex,
            out int assignmentsArgIndex)
        {
            ritualArgIndex = -1;
            assignmentsArgIndex = -1;

            ParameterInfo[] parameters;
            try
            {
                parameters = original.GetParameters();
            }
            catch (Exception ex)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}读取 TryExecuteOn 参数签名失败：{ex}",
                    ErrorKeyArgResolve);
                return false;
            }

            // 实例方法：ldarg 索引 = 参数序号 + 1（0 为 this）。
            int argOffset = original.IsStatic ? 0 : 1;
            for (int i = 0; i < parameters.Length; i++)
            {
                Type type = parameters[i].ParameterType;
                if (type == typeof(Precept_Ritual))
                {
                    ritualArgIndex = i + argOffset;
                }
                else if (type == typeof(RitualRoleAssignments))
                {
                    assignmentsArgIndex = i + argOffset;
                }
            }

            if (ritualArgIndex < 0 || assignmentsArgIndex < 0)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}无法从 TryExecuteOn 签名定位 ritual/assignments 参数，补丁未应用。",
                    ErrorKeyArgResolve);
                return false;
            }

            return true;
        }

        private static CodeInstruction CreateLdarg(int argIndex)
        {
            return argIndex switch
            {
                0 => new CodeInstruction(OpCodes.Ldarg_0),
                1 => new CodeInstruction(OpCodes.Ldarg_1),
                2 => new CodeInstruction(OpCodes.Ldarg_2),
                3 => new CodeInstruction(OpCodes.Ldarg_3),
                _ => new CodeInstruction(OpCodes.Ldarg, argIndex)
            };
        }

        private static int FindLabelTargetIndex(
            List<CodeInstruction> codes,
            int startInclusive,
            Label label)
        {
            for (int i = startInclusive; i < codes.Count; i++)
            {
                if (codes[i].labels.Contains(label))
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool ContainsFieldLoad(
            List<CodeInstruction> codes,
            int startInclusive,
            int endExclusive,
            FieldInfo field)
        {
            for (int i = startInclusive; i < endExclusive; i++)
            {
                if ((codes[i].opcode == OpCodes.Ldfld || codes[i].opcode == OpCodes.Ldflda)
                    && codes[i].operand is FieldInfo loaded
                    && loaded == field)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsMethodCall(
            List<CodeInstruction> codes,
            int startInclusive,
            int endExclusive,
            MethodInfo method)
        {
            for (int i = startInclusive; i < endExclusive; i++)
            {
                if (codes[i].Calls(method))
                {
                    return true;
                }
            }

            return false;
        }

        private sealed class DownedBranchMatch
        {
            public CodeInstruction LoadPawnInstruction = null!;
            public int InsertIndex;
            public Label ContinueLabel;
        }
    }
}
