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
                    ? "MAP_MechanoidMechanitor.ControlNetwork.AutonomousNoOverseer"
                        .Translate().ToString()
                    : "Target is a MAP mechanitor node.";
            }
        }
    }
}
