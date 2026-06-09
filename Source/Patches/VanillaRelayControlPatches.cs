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
            if (!VanillaRelayControlUtility.IsVanillaRelayController(pawn) || mech == null)
            {
                return;
            }

            Pawn overseer = mech.GetOverseer();

            if (__result.Accepted)
            {
                if (!VanillaRelayControlUtility.CanRelayControlTarget(pawn, mech, out string rejectReason))
                {
                    __result = rejectReason;
                }

                return;
            }

            if (!VanillaRelayControlUtility.CanRelayControlTarget(pawn, mech, out string takeoverReason))
            {
                return;
            }

            if (overseer == null || overseer == pawn)
            {
                return;
            }

            if (!VanillaRelayControlUtility.PassesVanillaControlBasics(pawn, mech, out _))
            {
                return;
            }

            __result = true;
        }
    }
}
