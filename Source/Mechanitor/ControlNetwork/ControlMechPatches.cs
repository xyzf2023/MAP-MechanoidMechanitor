using HarmonyLib;
using MAP_MechanoidMechanitor;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(MechanitorUtility), nameof(MechanitorUtility.CanControlMech))]
    public static class ControlMechPatches_CanControlMech
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn pawn, Pawn mech, ref AcceptanceReport __result)
        {
            if (pawn != null && mech != null
                && MAPMechanitorControlUtility.CanUseControlSystem(pawn)
                && MAPMechanitorControlUtility.WouldCreateControlCycle(pawn, mech))
            {
                __result = "MAP_MechanoidMechanitor.ControlNetwork.CircularOverseer".Translate();
                return false;
            }

            return true;
        }
    }
}
