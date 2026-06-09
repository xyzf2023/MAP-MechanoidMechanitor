using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(MechanitorUtility), nameof(MechanitorUtility.CanControlMech))]
    public static class VanillaRelayControlPatches
    {
        [HarmonyPostfix]
        public static void CanControlMech_Postfix(Pawn pawn, Pawn mech, ref AcceptanceReport __result)
        {
            if (!VanillaRelayControlUtility.IsVanillaRelayController(pawn))
            {
                return;
            }

            if (!__result.Accepted)
            {
                return;
            }

            if (!VanillaRelayControlUtility.CanRelayControlTarget(pawn, mech, out string rejectReason))
            {
                __result = rejectReason;
            }
        }
    }
}
