using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// Full 模式下文化仪式参与资格与质量统计适配。
    /// 统一原则：仅把原版 Humanlike 身份判断放宽为 “Humanlike 或 Full 模式正式机械族机械师”，
    /// 之后交由原版控制流继续执行意识形态匹配、spectatorFilter、PawnCanFillRole、温度、精神状态、
    /// 倒地、受伤、Lord 占用、囚犯与其他全部原版条件；不删除拒绝字符串后直接返回可分配。
    /// </summary>
    public static class MechanoidMechanitorIdeologyRitualPatches
    {
        /// <summary>
        /// 身份放宽辅助：原版 Humanlike 继续通过，Full 模式正式机械族机械师也视为通过，其他 Pawn 保持
        /// 原判定。供 Transpiler 替换原版 RaceProps.Humanlike 调用后由原版控制流自然继续。
        /// </summary>
        public static bool IsHumanlikeOrFullMechanitor(Pawn pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            if (pawn.RaceProps.Humanlike)
            {
                return true;
            }

            return ModsConfig.IdeologyActive
                && MechanoidMechanitorIdeologyAdaptationUtility
                    .CanServeAsIdeologyRoleOrRitualParticipant(pawn);
        }

        private static bool IsCallTo(CodeInstruction instruction, string methodName)
        {
            return (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt)
                && instruction.operand is MethodInfo method
                && method.Name == methodName;
        }

        /// <summary>
        /// 将方法中第一处 <c>pawn.RaceProps.Humanlike</c> 替换为
        /// <see cref="IsHumanlikeOrFullMechanitor"/>，保持其余原版指令与控制流不变。
        /// </summary>
        private static IEnumerable<CodeInstruction> RelaxFirstHumanlikeCheck(
            IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo replacement = AccessTools.Method(
                typeof(MechanoidMechanitorIdeologyRitualPatches),
                nameof(IsHumanlikeOrFullMechanitor));

            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            bool replaced = false;
            for (int i = 0; i < code.Count; i++)
            {
                if (!replaced
                    && i + 1 < code.Count
                    && IsCallTo(code[i], "get_RaceProps")
                    && IsCallTo(code[i + 1], "get_Humanlike"))
                {
                    yield return new CodeInstruction(OpCodes.Call, replacement)
                    {
                        labels = code[i].labels,
                        blocks = code[i].blocks
                    };
                    i++;
                    replaced = true;
                    continue;
                }

                yield return code[i];
            }
        }

        [HarmonyPatch(typeof(RitualRoleColonist), nameof(RitualRoleColonist.AppliesToPawn))]
        public static class Patch_RitualRoleColonist_AppliesToPawn
        {
            [HarmonyTranspiler]
            public static IEnumerable<CodeInstruction> Transpiler(
                IEnumerable<CodeInstruction> instructions)
            {
                return RelaxFirstHumanlikeCheck(instructions);
            }
        }

        [HarmonyPatch(typeof(RitualRoleAssignments), nameof(RitualRoleAssignments.CanEverSpectate))]
        public static class Patch_CanEverSpectate
        {
            [HarmonyTranspiler]
            public static IEnumerable<CodeInstruction> Transpiler(
                IEnumerable<CodeInstruction> instructions)
            {
                return RelaxFirstHumanlikeCheck(instructions);
            }
        }

        [HarmonyPatch]
        public static class Patch_PawnNotAssignableReason_Humanlike
        {
            private static MethodBase? cachedTarget;

            private static bool Prepare()
            {
                return TargetMethod() != null;
            }

            private static MethodBase? TargetMethod()
            {
                return cachedTarget ??= AccessTools.Method(
                    typeof(RitualRoleAssignments),
                    nameof(RitualRoleAssignments.PawnNotAssignableReason),
                    new[]
                    {
                        typeof(Pawn),
                        typeof(RitualRole),
                        typeof(Precept_Ritual),
                        typeof(RitualRoleAssignments),
                        typeof(TargetInfo),
                        typeof(bool).MakeByRefType()
                    });
            }

            [HarmonyTranspiler]
            public static IEnumerable<CodeInstruction> Transpiler(
                IEnumerable<CodeInstruction> instructions)
            {
                return RelaxFirstHumanlikeCheck(instructions);
            }
        }

        [HarmonyPatch(typeof(RitualOutcomeComp_ParticipantCount))]
        public static class Patch_ParticipantCount_Counts
        {
            private static MethodBase? cachedTarget;

            private static bool Prepare()
            {
                return TargetMethod() != null;
            }

            private static MethodBase? TargetMethod()
            {
                return cachedTarget ??= AccessTools.Method(
                    typeof(RitualOutcomeComp_ParticipantCount),
                    "Counts");
            }

            [HarmonyTranspiler]
            public static IEnumerable<CodeInstruction> Transpiler(
                IEnumerable<CodeInstruction> instructions)
            {
                return RelaxFirstHumanlikeCheck(instructions);
            }
        }

        /// <summary>
        /// 质量预览：<c>GetQualityFactor</c> 中的 Humanlike 统计位于编译器生成的 lambda 内，
        /// Transpiler 无法直接触及，故按原版规则重算参与者数量，并在“统计 Humanlike”的位置追加
        /// Full 模式机械族机械师；不计入 Partial。其余字段完全对齐原版计算方式。
        /// </summary>
        [HarmonyPatch(
            typeof(RitualOutcomeComp_NumParticipantsWithTag),
            nameof(RitualOutcomeComp_NumParticipantsWithTag.GetQualityFactor))]
        public static class Patch_NumParticipantsWithTag_QualityFactor
        {
            private static readonly MethodInfo? ExpectedOffsetDescMethod =
                AccessTools.Method(
                    typeof(RitualOutcomeComp),
                    "ExpectedOffsetDesc",
                    new[] { typeof(bool), typeof(float) });

            [HarmonyPostfix]
            public static void Postfix(
                RitualOutcomeComp_NumParticipantsWithTag __instance,
                RitualRoleAssignments assignments,
                ref QualityFactor __result)
            {
                if (!ModsConfig.IdeologyActive
                    || __result == null
                    || assignments == null
                    || __instance.curve == null
                    || !MechanoidMechanitorIdeologyAdaptationUtility.IsAtLeast(
                        MechanoidMechanitorIdeologyAdaptationLevel.Full))
                {
                    return;
                }

                int num = 0;
                foreach (Pawn pawn in assignments.Participants)
                {
                    if (IsHumanlikeOrFullMechanitor(pawn))
                    {
                        num++;
                    }
                }

                float maxValue = __instance.curve.Points[__instance.curve.PointsCount - 1].x;
                float quality = __instance.curve.Evaluate(num);
                __result.count = Mathf.Min(num, maxValue) + " / " + maxValue;
                __result.quality = quality;
                __result.positive = num > 0;
                if (ExpectedOffsetDescMethod != null)
                {
                    __result.qualityChange = (string)ExpectedOffsetDescMethod.Invoke(
                        __instance,
                        new object[] { num > 0, quality });
                }
            }
        }
    }
}
