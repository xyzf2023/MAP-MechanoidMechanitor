using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(Bill_ResurrectMech), nameof(Bill_ResurrectMech.CreateProducts))]
    internal static class MechResurrectionHealthPatches
    {
        private static bool replacementApplied;

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            MethodInfo? original = AccessTools.Method(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.RemoveAllHediffs));
            MethodInfo? replacement = AccessTools.Method(typeof(MechResurrectionHealthUtility),
                nameof(MechResurrectionHealthUtility.RemoveUnprotectedOrAllHediffs));
            int index = -1;
            int matches = 0;
            replacementApplied = false;
            for (int i = 0; i < codes.Count; i++)
            {
                if (original == null || !codes[i].Calls(original)) continue;
                index = i;
                matches++;
            }

            if (matches != 1 || replacement == null)
            {
                Log.ErrorOnce("[MAP-机械族机械师] 机械培育复活保护未应用：预期一处 RemoveAllHediffs 调用，实际 "
                    + matches + " 处，或替换方法不可用。保留原版指令。", 0x4D525350);
                return codes;
            }

            // 原位替换，保留跳转标签和异常块边界。
            codes[index].opcode = OpCodes.Call;
            codes[index].operand = replacement;
            replacementApplied = true;
            return codes;
        }

        [HarmonyPostfix]
        private static void Postfix(Thing __result)
        {
            if (replacementApplied && __result is Pawn pawn)
                MechResurrectionHealthUtility.SynchronizeAfterResurrection(pawn);
        }
    }
}
