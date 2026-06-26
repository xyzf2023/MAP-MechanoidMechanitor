using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class HumanApparelRenderPatches
    {
        private static readonly FieldInfo PawnField = AccessTools.Field(typeof(PawnRenderTree), "pawn");
        private static readonly MethodInfo HumanlikeOnlyGetter = AccessTools.PropertyGetter(
            typeof(DynamicPawnRenderNodeSetup),
            nameof(DynamicPawnRenderNodeSetup.HumanlikeOnly));
        private static readonly MethodInfo HumanlikeGetter = AccessTools.PropertyGetter(
            typeof(RaceProperties),
            nameof(RaceProperties.Humanlike));

        public static bool ShouldSkipDynamicSetup(DynamicPawnRenderNodeSetup setup, Pawn pawn)
        {
            if (!setup.HumanlikeOnly)
            {
                return false;
            }

            if (pawn.RaceProps.Humanlike)
            {
                return false;
            }

            if (setup is DynamicPawnRenderNodeSetup_Apparel
                && HumanApparelUtility.CanRenderHumanApparel(pawn))
            {
                return false;
            }

            return true;
        }

        private static bool IsApparelRenderNodeProperties(PawnRenderNodeProperties props)
        {
            if (props.workerClass != null)
            {
                if (props.workerClass == typeof(PawnRenderNodeWorker_Apparel_Body)
                    || props.workerClass == typeof(PawnRenderNodeWorker_Apparel_Head))
                {
                    return true;
                }
            }

            if (props.nodeClass != null
                && typeof(PawnRenderNode_Apparel).IsAssignableFrom(props.nodeClass))
            {
                return true;
            }

            return props.parentTagDef == PawnRenderNodeTagDefOf.ApparelBody
                || props.parentTagDef == PawnRenderNodeTagDefOf.ApparelHead;
        }

        [HarmonyPatch]
        public static class Patch_PawnRenderTree_SetupDynamicNodes
        {
            private static MethodBase? TargetMethod()
            {
                return AccessTools.Method(typeof(PawnRenderTree), "SetupDynamicNodes");
            }

            private static bool Prepare()
            {
                return TargetMethod() != null
                    && HumanlikeOnlyGetter != null
                    && HumanlikeGetter != null
                    && PawnField != null;
            }

            [HarmonyTranspiler]
            public static IEnumerable<CodeInstruction> Transpiler(
                IEnumerable<CodeInstruction> instructions)
            {
                List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
                MethodInfo shouldSkipMethod = AccessTools.Method(
                    typeof(HumanApparelRenderPatches),
                    nameof(ShouldSkipDynamicSetup));

                for (int i = 0; i < codes.Count; i++)
                {
                    if (!codes[i].Calls(HumanlikeOnlyGetter) || i < 1)
                    {
                        continue;
                    }

                    CodeInstruction setupLoad = codes[i - 1];
                    int humanlikeGetterIndex = -1;
                    for (int j = i + 1; j < codes.Count && j < i + 12; j++)
                    {
                        if (codes[j].Calls(HumanlikeGetter))
                        {
                            humanlikeGetterIndex = j;
                            break;
                        }
                    }

                    if (humanlikeGetterIndex < 0)
                    {
                        continue;
                    }

                    CodeInstruction brFalse = codes[i + 1];
                    CodeInstruction? brContinue = null;
                    if (humanlikeGetterIndex + 2 < codes.Count)
                    {
                        OpCode continueOpcode = codes[humanlikeGetterIndex + 2].opcode;
                        if (continueOpcode == OpCodes.Br || continueOpcode == OpCodes.Br_S)
                        {
                            brContinue = codes[humanlikeGetterIndex + 2];
                        }
                    }

                    int removeCount = humanlikeGetterIndex - (i - 1) + 1;
                    codes.RemoveRange(i - 1, removeCount);

                    int insertIndex = i - 1;
                    codes.Insert(insertIndex++, new CodeInstruction(setupLoad.opcode, setupLoad.operand));
                    codes.Insert(insertIndex++, new CodeInstruction(OpCodes.Ldarg_0));
                    codes.Insert(insertIndex++, new CodeInstruction(OpCodes.Ldfld, PawnField));
                    codes.Insert(insertIndex++, new CodeInstruction(OpCodes.Call, shouldSkipMethod));
                    codes.Insert(insertIndex++, brFalse);
                    if (brContinue != null)
                    {
                        codes.Insert(insertIndex, brContinue);
                    }

                    break;
                }

                return codes;
            }
        }

        [HarmonyPatch(typeof(PawnRenderTree), nameof(PawnRenderTree.ShouldAddNodeToTree))]
        public static class Patch_PawnRenderTree_ShouldAddNodeToTree
        {
            [HarmonyPostfix]
            public static void Postfix(
                PawnRenderTree __instance,
                PawnRenderNodeProperties props,
                ref bool __result)
            {
                if (__result
                    || props == null
                    || props.pawnType != PawnRenderNodeProperties.RenderNodePawnType.HumanlikeOnly
                    || !HumanApparelUtility.CanRenderHumanApparel(__instance.pawn)
                    || !IsApparelRenderNodeProperties(props))
                {
                    return;
                }

                __result = true;
            }
        }

        [HarmonyPatch]
        public static class Patch_PawnRenderTree_AdjustParms
        {
            private static MethodBase? TargetMethod()
            {
                return AccessTools.Method(typeof(PawnRenderTree), "AdjustParms");
            }

            private static bool Prepare()
            {
                return TargetMethod() != null
                    && HumanlikeGetter != null
                    && PawnField != null;
            }

            [HarmonyTranspiler]
            public static IEnumerable<CodeInstruction> Transpiler(
                IEnumerable<CodeInstruction> instructions)
            {
                List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
                MethodInfo racePropsGetter = AccessTools.PropertyGetter(
                    typeof(Pawn),
                    nameof(Pawn.RaceProps));
                MethodInfo shouldAdjustMethod = AccessTools.Method(
                    typeof(HumanApparelUtility),
                    nameof(HumanApparelUtility.ShouldUseHumanlikeRenderAdjustments));

                for (int i = 0; i < codes.Count; i++)
                {
                    if (!codes[i].Calls(HumanlikeGetter))
                    {
                        continue;
                    }

                    if (i < 2
                        || !codes[i - 1].Calls(racePropsGetter)
                        || codes[i - 2].opcode != OpCodes.Ldfld
                        || codes[i - 2].operand is not FieldInfo pawnFieldOperand
                        || pawnFieldOperand != PawnField)
                    {
                        continue;
                    }

                    codes.RemoveRange(i - 2, 3);
                    codes.Insert(i - 2, new CodeInstruction(OpCodes.Ldarg_0));
                    codes.Insert(i - 1, new CodeInstruction(OpCodes.Ldfld, PawnField));
                    codes.Insert(i, new CodeInstruction(OpCodes.Call, shouldAdjustMethod));
                    break;
                }

                return codes;
            }
        }
    }
}
