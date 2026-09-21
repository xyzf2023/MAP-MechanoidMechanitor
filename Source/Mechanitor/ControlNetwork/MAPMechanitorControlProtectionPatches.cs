using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(MechanitorUtility), nameof(MechanitorUtility.CanControlMech))]
    public static class MAPMechanitorControlProtectionPatches
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void CanControlMech_Postfix(Pawn pawn, Pawn mech, ref AcceptanceReport __result)
        {
            if (!ModsConfig.BiotechActive || mech == null)
            {
                return;
            }

            if (MAPMechanitorControlProtectionUtility.IsProtectedMechanitorTarget(mech, pawn))
            {
                __result = AutonomousMechUtility.IsAutonomousMech(mech)
                    ? "目标具有独立自律资格，无需监管者。"
                    : "Target is a MAP mechanitor node.";
            }
        }
    }
}
