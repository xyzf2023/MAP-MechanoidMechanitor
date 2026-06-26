using System;
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
        private static readonly MethodInfo HumanlikeOnlyGetter = AccessTools.PropertyGetter(
            typeof(DynamicPawnRenderNodeSetup),
            nameof(DynamicPawnRenderNodeSetup.HumanlikeOnly));
        private static readonly MethodInfo HumanlikeGetter = AccessTools.PropertyGetter(
            typeof(RaceProperties),
            nameof(RaceProperties.Humanlike));
        private static readonly MethodInfo EffectiveHumanlikeOnlyMethod = AccessTools.Method(
            typeof(HumanApparelRenderPatches),
            nameof(EffectiveHumanlikeOnly));
        private static readonly MethodInfo EffectiveHumanlikeMethod = AccessTools.Method(
            typeof(HumanApparelRenderPatches),
            nameof(EffectiveHumanlike));

        private static bool EffectiveHumanlikeOnly(
            DynamicPawnRenderNodeSetup setup,
            PawnRenderTree tree)
        {
            if (setup is DynamicPawnRenderNodeSetup_Apparel
                && HumanApparelUtility.CanRenderHumanApparel(tree.pawn))
            {
                return false;
            }

            return setup.HumanlikeOnly;
        }

        private static bool EffectiveHumanlike(
            RaceProperties raceProps,
            PawnRenderTree tree)
        {
            return raceProps.Humanlike
                || HumanApparelUtility.CanRenderHumanApparel(tree.pawn);
        }

        private static void TransferLabelsAndBlocks(
            CodeInstruction from,
            CodeInstruction to)
        {
            if (from.labels != null && from.labels.Count > 0)
            {
                to.labels.AddRange(from.labels);
                from.labels.Clear();
            }

            if (from.blocks != null && from.blocks.Count > 0)
            {
                to.blocks.AddRange(from.blocks);
                from.blocks.Clear();
            }
        }

        private static IEnumerable<CodeInstruction> ReplaceGetterWithHelper(
            IEnumerable<CodeInstruction> instructions,
            MethodInfo getter,
            MethodInfo helper,
            string errorContext)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            int matchCount = 0;

            for (int i = 0; i < codes.Count; i++)
            {
                if (!codes[i].Calls(getter))
                {
                    continue;
                }

                matchCount++;
                CodeInstruction getterInstruction = codes[i];
                CodeInstruction loadThis = new CodeInstruction(OpCodes.Ldarg_0);
                TransferLabelsAndBlocks(getterInstruction, loadThis);
                codes.Insert(i, loadThis);

                getterInstruction.opcode = OpCodes.Call;
                getterInstruction.operand = helper;
            }

            if (matchCount != 1)
            {
                throw new InvalidOperationException(
                    $"{errorContext} transpiler expected 1 getter, found {matchCount}.");
            }

            return codes;
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
                    && EffectiveHumanlikeOnlyMethod != null;
            }

            [HarmonyTranspiler]
            public static IEnumerable<CodeInstruction> Transpiler(
                IEnumerable<CodeInstruction> instructions)
            {
                return ReplaceGetterWithHelper(
                    instructions,
                    HumanlikeOnlyGetter,
                    EffectiveHumanlikeOnlyMethod,
                    "PawnRenderTree.SetupDynamicNodes HumanlikeOnly getter");
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
                    && EffectiveHumanlikeMethod != null;
            }

            [HarmonyTranspiler]
            public static IEnumerable<CodeInstruction> Transpiler(
                IEnumerable<CodeInstruction> instructions)
            {
                return ReplaceGetterWithHelper(
                    instructions,
                    HumanlikeGetter,
                    EffectiveHumanlikeMethod,
                    "PawnRenderTree.AdjustParms Humanlike getter");
            }
        }
    }
}
