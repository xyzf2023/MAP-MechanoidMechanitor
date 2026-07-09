using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(
        typeof(PawnCapacityUtility),
        nameof(PawnCapacityUtility.CalculateCapacityLevel))]
    public static class Patch_PawnCapacityUtility_CalculateCapacityLevel_DataProcessingAllocation
    {
        [HarmonyPostfix]
        public static void Postfix(
            HediffSet diffSet,
            PawnCapacityDef capacity,
            ref float __result)
        {
            if (capacity != PawnCapacityDefOf.Consciousness)
            {
                return;
            }

            Pawn? pawn = diffSet?.pawn;
            if (pawn == null)
            {
                return;
            }

            GameComponent_DataProcessingAllocationRegistry? registry =
                GameComponent_DataProcessingAllocationRegistry.CurrentRegistry;
            if (registry == null)
            {
                return;
            }

            if (MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
            {
                int totalSteps = registry.GetTotalStepsForOverseer(pawn);
                if (totalSteps > 0)
                {
                    __result -= totalSteps * DataProcessingAllocationUtility.StepPercent;
                }
            }

            int targetSteps = registry.GetStepsForTarget(pawn);
            if (targetSteps > 0)
            {
                __result += targetSteps * DataProcessingAllocationUtility.StepPercent;
            }

            __result = Mathf.Max(__result, capacity.minValue);
            __result = GenMath.RoundedHundredth(__result);
        }
    }
}
