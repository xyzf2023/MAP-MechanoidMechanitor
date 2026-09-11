using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch]
    public static class Patch_JobGiver_Work_PawnCanUseWorkGiver_MechWorkGiverRestriction
    {
        private const string LogPrefix = "[MAP-机械族机械师] MechWorkGiverRestrictionPatches：";

        private static MethodBase? TargetMethod()
        {
            MethodInfo? method = AccessTools.Method(
                typeof(JobGiver_Work),
                "PawnCanUseWorkGiver",
                new[] { typeof(Pawn), typeof(WorkGiver) });
            if (method == null)
            {
                Log.Error($"{LogPrefix}未找到 JobGiver_Work.PawnCanUseWorkGiver(Pawn, WorkGiver)，补丁未应用。");
            }

            return method;
        }

        private static bool Prepare()
        {
            return TargetMethod() != null;
        }

        private static bool CanBeDoneByMechsOrAuthorized(bool canBeDoneByMechs, Pawn pawn, WorkGiver workGiver)
        {
            WorkTypeDef? workType = workGiver?.def?.workType;
            if (workType == WardenWorkUtility.WardenWorkType)
            {
                return canBeDoneByMechs || WardenWorkUtility.IsAuthorized(pawn);
            }

            if (workType == AnimalHandlingWorkUtility.HandlingWorkType)
            {
                if (AnimalHandlingWorkUtility.IsAuthorized(pawn))
                {
                    return true;
                }

                return canBeDoneByMechs
                    || CompMechRestrictedWorkGiverUser.Allows(pawn, workGiver);
            }

            if (workType == MechanicalChildcareUtility.ChildcareWorkType)
            {
                // 已授权保育：仅走白名单，不得被 canBeDoneByMechs 绕过；授权只查一次。
                if (MechanicalChildcareUtility.IsAuthorized(pawn))
                {
                    return MechanicalChildcareUtility.IsAllowedWorkGiver(workGiver?.def);
                }

                return canBeDoneByMechs
                    || CompMechRestrictedWorkGiverUser.Allows(pawn, workGiver);
            }

            // 其他工作：原版已允许机械族时立即放行，避免额外身份/组件查询。
            if (canBeDoneByMechs)
            {
                return true;
            }

            if (pawn == null || pawn.RaceProps?.IsMechanoid != true)
            {
                return false;
            }

            return MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn)
                || CompMechRestrictedWorkGiverUser.Allows(pawn, workGiver);
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            FieldInfo? canBeDoneByMechsField = AccessTools.Field(
                typeof(WorkGiverDef),
                nameof(WorkGiverDef.canBeDoneByMechs));
            MethodInfo? helperMethod = AccessTools.Method(
                typeof(Patch_JobGiver_Work_PawnCanUseWorkGiver_MechWorkGiverRestriction),
                nameof(CanBeDoneByMechsOrAuthorized));

            if (canBeDoneByMechsField == null)
            {
                Log.Error($"{LogPrefix}未找到 WorkGiverDef.canBeDoneByMechs 字段，补丁未应用。");
                return codes;
            }

            if (helperMethod == null)
            {
                Log.Error($"{LogPrefix}未找到 {nameof(CanBeDoneByMechsOrAuthorized)} 辅助方法，补丁未应用。");
                return codes;
            }

            int matchCount = 0;
            int insertIndex = -1;
            for (int i = 0; i < codes.Count; i++)
            {
                CodeInstruction instruction = codes[i];
                if (instruction.opcode != OpCodes.Ldfld
                    || instruction.operand is not FieldInfo field
                    || !field.Equals(canBeDoneByMechsField))
                {
                    continue;
                }

                matchCount++;
                insertIndex = i + 1;
            }

            if (matchCount != 1)
            {
                Log.Error($"{LogPrefix}JobGiver_Work.PawnCanUseWorkGiver 中 WorkGiverDef.canBeDoneByMechs 的 ldfld 预期仅 1 处，实际找到 {matchCount} 处，补丁未应用。");
                return codes;
            }

            codes.Insert(insertIndex, new CodeInstruction(OpCodes.Ldarg_1));
            codes.Insert(insertIndex + 1, new CodeInstruction(OpCodes.Ldarg_2));
            codes.Insert(insertIndex + 2, new CodeInstruction(OpCodes.Call, helperMethod));
            return codes;
        }
    }
}
