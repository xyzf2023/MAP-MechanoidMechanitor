using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch]
    public static class Patch_JobGiver_Work_PawnCanUseWorkGiver_JusticeWorkGiverRestriction
    {
        private const string LogPrefix = "[MAP_MechanoidMechanitor] MechWorkGiverRestrictionPatches:";

        private static MethodBase? TargetMethod()
        {
            MethodInfo? method = AccessTools.Method(
                typeof(JobGiver_Work),
                "PawnCanUseWorkGiver",
                new[] { typeof(Pawn), typeof(WorkGiver) });
            if (method == null)
            {
                Log.Error($"{LogPrefix} could not find JobGiver_Work.PawnCanUseWorkGiver(Pawn, WorkGiver). Patch not applied.");
            }

            return method;
        }

        private static bool Prepare()
        {
            return TargetMethod() != null;
        }

        private static bool CanBeDoneByMechsOrAuthorized(bool canBeDoneByMechs, Pawn pawn, WorkGiver workGiver)
        {
            return canBeDoneByMechs
                || CompJusticeSelfWorkMode.GetFor(pawn) != null
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
                typeof(Patch_JobGiver_Work_PawnCanUseWorkGiver_JusticeWorkGiverRestriction),
                nameof(CanBeDoneByMechsOrAuthorized));

            if (canBeDoneByMechsField == null)
            {
                Log.Error($"{LogPrefix} could not find WorkGiverDef.canBeDoneByMechs field. Patch not applied.");
                return codes;
            }

            if (helperMethod == null)
            {
                Log.Error($"{LogPrefix} could not find {nameof(CanBeDoneByMechsOrAuthorized)} helper method. Patch not applied.");
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
                Log.Error($"{LogPrefix} expected exactly 1 ldfld for WorkGiverDef.canBeDoneByMechs in JobGiver_Work.PawnCanUseWorkGiver, found {matchCount}. Patch not applied.");
                return codes;
            }

            codes.Insert(insertIndex, new CodeInstruction(OpCodes.Ldarg_1));
            codes.Insert(insertIndex + 1, new CodeInstruction(OpCodes.Ldarg_2));
            codes.Insert(insertIndex + 2, new CodeInstruction(OpCodes.Call, helperMethod));
            return codes;
        }
    }
}
