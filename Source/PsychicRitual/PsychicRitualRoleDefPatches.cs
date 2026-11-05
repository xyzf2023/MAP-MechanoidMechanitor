using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch]
    public static class PsychicRitualRoleDef_PawnCanDo_Patch
    {
        private const int ResolveErrorKey = 879347351;
        private const int StructureErrorKey = 879347352;
        private static MethodInfo? targetMethod;
        private static readonly MethodInfo? ConditionAllowedMethod = AccessTools.Method(
            typeof(PsychicRitualRoleDef), nameof(PsychicRitualRoleDef.ConditionAllowed),
            new[] { typeof(PsychicRitualRoleDef.Condition) });
        private static readonly MethodInfo? RacePropsGetter =
            AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.RaceProps));
        private static readonly MethodInfo? HumanlikeGetter =
            AccessTools.PropertyGetter(typeof(RaceProperties), nameof(RaceProperties.Humanlike));
        private static readonly MethodInfo? ConditionAllowedForPawnMethod = AccessTools.Method(
            typeof(PsychicRitualRoleDef_PawnCanDo_Patch), nameof(ConditionAllowedForPawn));

        public static bool Prepare()
        {
            targetMethod = AccessTools.Method(
                typeof(PsychicRitualRoleDef),
                nameof(PsychicRitualRoleDef.PawnCanDo),
                new[]
                {
                    typeof(PsychicRitualRoleDef.Context),
                    typeof(Pawn),
                    typeof(TargetInfo),
                    typeof(AnyEnum).MakeByRefType()
                });

            if (targetMethod == null || targetMethod.DeclaringType != typeof(PsychicRitualRoleDef)
                || !targetMethod.IsFamily || targetMethod.IsStatic || targetMethod.ReturnType != typeof(bool)
                || ConditionAllowedMethod == null || RacePropsGetter == null || HumanlikeGetter == null
                || ConditionAllowedForPawnMethod == null)
            {
                Log.WarningOnce("[MAP-机械族机械师] 心灵仪式角色校验签名发生变化，已跳过机械体种族放行。", ResolveErrorKey);
                return false;
            }

            try
            {
                List<CodeInstruction> original = PatchProcessor.GetOriginalInstructions(targetMethod);
                if (FindNonHumanlikeCheck(original) >= 0)
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.WarningOnce("[MAP-机械族机械师] 无法验证心灵仪式角色校验，已跳过机械体种族放行：" + ex, ResolveErrorKey);
                return false;
            }

            Log.WarningOnce("[MAP-机械族机械师] 心灵仪式种族校验结构发生变化，已跳过机械体种族放行。", StructureErrorKey);
            return false;
        }

        public static MethodBase TargetMethod() => targetMethod!;

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            int callIndex = FindNonHumanlikeCheck(codes);
            if (callIndex < 0 || ConditionAllowedForPawnMethod == null)
            {
                Log.WarningOnce("[MAP-机械族机械师] 心灵仪式种族校验已被其他补丁改变，保留原有校验。", StructureErrorKey);
                return codes;
            }

            // 只扩展 NonHumanlike 的允许条件，让精神状态、燃烧、寻路等后续检查继续执行。
            CodeInstruction loadPawn = new CodeInstruction(OpCodes.Ldarg_2);
            loadPawn.labels.AddRange(codes[callIndex].labels);
            codes[callIndex].labels.Clear();
            codes[callIndex].opcode = OpCodes.Call;
            codes[callIndex].operand = ConditionAllowedForPawnMethod;
            codes.Insert(callIndex, loadPawn);
            return codes;
        }

        private static int FindNonHumanlikeCheck(List<CodeInstruction> codes)
        {
            int found = -1;
            for (int i = 0; i <= codes.Count - 8; i++)
            {
                if (codes[i].opcode != OpCodes.Ldarg_0
                    || !codes[i + 1].LoadsConstant((int)PsychicRitualRoleDef.Condition.NonHumanlike)
                    || !codes[i + 2].Calls(ConditionAllowedMethod)
                    || !IsBranchTrue(codes[i + 3])
                    || codes[i + 4].opcode != OpCodes.Ldarg_2
                    || !codes[i + 5].Calls(RacePropsGetter)
                    || !codes[i + 6].Calls(HumanlikeGetter)
                    || !IsBranchTrue(codes[i + 7])
                    || !Equals(codes[i + 3].operand, codes[i + 7].operand))
                {
                    continue;
                }

                for (int j = i; j < i + 8; j++)
                {
                    if (codes[j].blocks.Count != 0)
                    {
                        return -1;
                    }
                }

                if (found >= 0)
                {
                    return -1;
                }

                found = i + 2;
            }

            return found;
        }

        private static bool IsBranchTrue(CodeInstruction code) =>
            code.opcode == OpCodes.Brtrue || code.opcode == OpCodes.Brtrue_S;

        private static bool ConditionAllowedForPawn(
            PsychicRitualRoleDef role,
            PsychicRitualRoleDef.Condition condition,
            Pawn pawn)
        {
            return role.ConditionAllowed(condition)
                || (condition == PsychicRitualRoleDef.Condition.NonHumanlike
                    && ModsConfig.AnomalyActive
                    && MAPPsychicRitualUtility.PawnCanDoSafeRole(pawn, role));
        }
    }
}
