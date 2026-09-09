using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.GlitterworldDestroyer5ExpansionMechFusion
{
    /// <summary>
    /// 仅替换 MechFusion / SuperMechFusion 的 Apply() 中机师链接门槛：
    /// 原版有 Mechlink 继续通过；没有 Mechlink 但属于机械族机械师时也通过。
    /// 不改融合配方、阵营、生成、监管关系或其他执行逻辑。
    /// </summary>
    internal static class MechFusionCastEligibilityCompatibilityPatch
    {
        private const string MechlinkDefName = "MechlinkImplant";

        internal static bool FusionApplyAnchorMatched { get; private set; }

        internal static bool SuperFusionApplyAnchorMatched { get; private set; }

        internal static void ResetInstallState()
        {
            FusionApplyAnchorMatched = false;
            SuperFusionApplyAnchorMatched = false;
        }

        internal static IEnumerable<CodeInstruction> Transpiler_MechFusionApply(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            FusionApplyAnchorMatched = PatchMechlinkGate(codes);
            return codes;
        }

        internal static IEnumerable<CodeInstruction> Transpiler_SuperMechFusionApply(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            SuperFusionApplyAnchorMatched = PatchMechlinkGate(codes);
            return codes;
        }

        internal static bool HasHediffForMechFusion(
            HediffSet hediffSet,
            HediffDef hediffDef,
            bool mustBeVisible)
        {
            if (hediffSet == null || hediffDef == null)
            {
                return false;
            }

            if (hediffSet.HasHediff(hediffDef, mustBeVisible))
            {
                return true;
            }

            if (hediffDef != HediffDefOf.MechlinkImplant)
            {
                return false;
            }

            return MechFusionCompatibleMechanitorUtility.IsEligibleMechanitor(
                hediffSet.pawn);
        }

        private static bool PatchMechlinkGate(List<CodeInstruction> codes)
        {
            MethodInfo? hasHediff = AccessTools.Method(
                typeof(HediffSet),
                nameof(HediffSet.HasHediff),
                new[] { typeof(HediffDef), typeof(bool) });
            MethodInfo? replacement = AccessTools.Method(
                typeof(MechFusionCastEligibilityCompatibilityPatch),
                nameof(HasHediffForMechFusion),
                new[] { typeof(HediffSet), typeof(HediffDef), typeof(bool) });

            if (hasHediff == null || replacement == null)
            {
                return false;
            }

            List<int> candidates = new List<int>();
            for (int i = 0; i < codes.Count; i++)
            {
                if (!codes[i].Calls(hasHediff))
                {
                    continue;
                }

                if (!HasNearbyMechlinkLiteral(codes, i))
                {
                    continue;
                }

                candidates.Add(i);
            }

            if (candidates.Count != 1)
            {
                return false;
            }

            CodeInstruction target = codes[candidates[0]];
            if (target.blocks.Count > 0)
            {
                return false;
            }

            target.opcode = OpCodes.Call;
            target.operand = replacement;
            return true;
        }

        private static bool HasNearbyMechlinkLiteral(
            List<CodeInstruction> codes,
            int callIndex)
        {
            int searchStart = Math.Max(0, callIndex - 8);
            for (int i = callIndex - 1; i >= searchStart; i--)
            {
                CodeInstruction instruction = codes[i];
                if (instruction.opcode == OpCodes.Ldstr
                    && instruction.operand is string value
                    && string.Equals(
                        value,
                        MechlinkDefName,
                        StringComparison.Ordinal))
                {
                    return true;
                }

                if (IsConditionalOrUnconditionalBranch(instruction))
                {
                    break;
                }
            }

            return false;
        }

        private static bool IsConditionalOrUnconditionalBranch(
            CodeInstruction instruction)
        {
            FlowControl flowControl = instruction.opcode.FlowControl;
            return flowControl == FlowControl.Branch
                || flowControl == FlowControl.Cond_Branch;
        }
    }
}
