using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    [HarmonyPatch(typeof(ITab_Pawn_Visitor), "DoPrisonerTab")]
    public static class MechanoidMechanitorPurgeDirective_PrisonerTabPatch
    {
        private static readonly FieldInfo? TmpPrisonerInteractionModesField =
            AccessTools.Field(
                typeof(ITab_Pawn_Visitor),
                "tmpPrisonerInteractionModes");

        private static readonly MethodInfo? ListAddRangeMethod =
            AccessTools.Method(
                typeof(List<PrisonerInteractionModeDef>),
                nameof(List<PrisonerInteractionModeDef>.AddRange),
                new[] { typeof(IEnumerable<PrisonerInteractionModeDef>) });

        private static readonly MethodInfo FilterMethod =
            AccessTools.Method(
                typeof(MechanoidMechanitorPurgeDirective_PrisonerTabPatch),
                nameof(FilterPrisonerInteractionModes));

        [HarmonyPrefix]
        public static void Prefix()
        {
            if (!GameComponent_MechanoidMechanitorStoryState.IsPurgeDirectiveActive)
            {
                return;
            }

            SanitizeHiddenPrisonerInteractionMode(Find.Selector.SingleSelectedThing as Pawn);
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            if (TmpPrisonerInteractionModesField == null
                || ListAddRangeMethod == null
                || FilterMethod == null)
            {
                Log.Error(
                    "[MAP-机械族机械师] 肃清指令囚犯选项补丁无法解析目标成员，补丁未应用。");
                return codes;
            }

            List<CodeInstruction> original = new List<CodeInstruction>(codes);
            int injected = 0;
            for (int i = 0; i < codes.Count; i++)
            {
                CodeInstruction code = codes[i];
                MethodInfo? calledMethod = code.operand as MethodInfo;
                if ((code.opcode != OpCodes.Callvirt && code.opcode != OpCodes.Call)
                    || calledMethod == null
                    || !calledMethod.Equals(ListAddRangeMethod))
                {
                    continue;
                }

                if (!HasTmpListLoadBefore(codes, i, TmpPrisonerInteractionModesField))
                {
                    continue;
                }

                codes.Insert(
                    i + 1,
                    new CodeInstruction(OpCodes.Ldsfld, TmpPrisonerInteractionModesField));
                codes.Insert(i + 2, new CodeInstruction(OpCodes.Call, FilterMethod));
                injected++;
                i += 2;
            }

            if (injected != 2)
            {
                Log.Error(
                    "[MAP-机械族机械师] 肃清指令囚犯选项补丁预期注入 2 处过滤调用，实际注入 "
                    + injected
                    + " 处，补丁未应用。");
                return original;
            }

            return codes;
        }

        public static void FilterPrisonerInteractionModes(
            List<PrisonerInteractionModeDef> modes)
        {
            if (modes == null
                || !GameComponent_MechanoidMechanitorStoryState.IsPurgeDirectiveActive)
            {
                return;
            }

            for (int i = modes.Count - 1; i >= 0; i--)
            {
                PrisonerInteractionModeDef mode = modes[i];
                if (mode == PrisonerInteractionModeDefOf.AttemptRecruit)
                {
                    modes.RemoveAt(i);
                    continue;
                }

                if (ModsConfig.IdeologyActive
                    && mode == PrisonerInteractionModeDefOf.Enslave)
                {
                    modes.RemoveAt(i);
                }
            }
        }

        private static void SanitizeHiddenPrisonerInteractionMode(Pawn? pawn)
        {
            if (pawn?.guest == null)
            {
                return;
            }

            PrisonerInteractionModeDef mode = pawn.guest.ExclusiveInteractionMode;
            if (mode == PrisonerInteractionModeDefOf.AttemptRecruit)
            {
                pawn.guest.SetExclusiveInteraction(PrisonerInteractionModeDefOf.MaintainOnly);
                return;
            }

            if (ModsConfig.IdeologyActive
                && mode == PrisonerInteractionModeDefOf.Enslave)
            {
                pawn.guest.SetExclusiveInteraction(PrisonerInteractionModeDefOf.MaintainOnly);
            }
        }

        private static bool HasTmpListLoadBefore(
            List<CodeInstruction> codes,
            int callIndex,
            FieldInfo tmpListField)
        {
            for (int i = callIndex - 1; i >= 0 && i >= callIndex - 24; i--)
            {
                CodeInstruction code = codes[i];
                if (code.opcode == OpCodes.Ldsfld && code.operand as FieldInfo == tmpListField)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
