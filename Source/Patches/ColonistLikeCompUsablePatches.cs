using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    // CompUsable normally rejects every non-flesh pawn before running its other checks.
    // Authorized player mechanoids may bypass only that generic race gate; power, path,
    // reservation, required hediffs and every CompUseEffect check remain vanilla.
    [HarmonyPatch]
    public static class Patch_CompUsable_CanBeUsedBy_ColonistLikeMechanoid
    {
        private const string LogPrefix =
            "[MAP-机械族机械师] ColonistLikeCompUsablePatches：";

        private static MethodBase? TargetMethod()
        {
            MethodInfo? method = AccessTools.Method(
                typeof(CompUsable),
                nameof(CompUsable.CanBeUsedBy),
                new[] { typeof(Pawn), typeof(bool), typeof(bool) });

            if (method == null)
            {
                Log.Error(
                    $"{LogPrefix}未找到 CompUsable.CanBeUsedBy(Pawn, bool, bool)，补丁未应用。");
            }

            return method;
        }

        private static bool Prepare()
        {
            return TargetMethod() != null;
        }

        private static bool IsFleshOrAuthorizedCompUsableUser(Pawn pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            if (pawn.RaceProps.IsFlesh)
            {
                return true;
            }

            return pawn.RaceProps.IsMechanoid
                && CompColonistLikeFloatMenuUser.PawnCanUseColonistLikeFloatMenu(pawn);
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            MethodInfo? racePropsGetter = AccessTools.PropertyGetter(
                typeof(Pawn),
                nameof(Pawn.RaceProps));
            MethodInfo? isFleshGetter = AccessTools.PropertyGetter(
                typeof(RaceProperties),
                nameof(RaceProperties.IsFlesh));
            MethodInfo? helperMethod = AccessTools.Method(
                typeof(Patch_CompUsable_CanBeUsedBy_ColonistLikeMechanoid),
                nameof(IsFleshOrAuthorizedCompUsableUser));

            if (racePropsGetter == null || isFleshGetter == null || helperMethod == null)
            {
                Log.Error(
                    $"{LogPrefix}无法解析种族门控相关方法，补丁未应用。");
                return codes;
            }

            int matchCount = 0;
            int matchIndex = -1;

            for (int i = 0; i < codes.Count - 1; i++)
            {
                if (codes[i].operand is not MethodInfo firstMethod
                    || !firstMethod.Equals(racePropsGetter)
                    || codes[i + 1].operand is not MethodInfo secondMethod
                    || !secondMethod.Equals(isFleshGetter))
                {
                    continue;
                }

                matchCount++;
                matchIndex = i;
            }

            if (matchCount != 1)
            {
                Log.Error(
                    $"{LogPrefix}CompUsable.CanBeUsedBy 中 Pawn.RaceProps -> RaceProperties.IsFlesh 序列预期仅 1 处，实际找到 {matchCount} 处，补丁未应用。");
                return codes;
            }

            // The Pawn instance is already on the evaluation stack before get_RaceProps.
            // Removing that getter leaves the Pawn for our helper to consume instead.
            codes[matchIndex].opcode = OpCodes.Nop;
            codes[matchIndex].operand = null;
            codes[matchIndex + 1].opcode = OpCodes.Call;
            codes[matchIndex + 1].operand = helperMethod;

            return codes;
        }
    }
}
