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
            if (pawn != null
                && mech != null
                && pawn == mech
                && MAPMechanitorNodeUtility.IsMechanitorNodeController(pawn))
            {
                __result = false;
                if (Prefs.DevMode)
                {
                    Log.Message($"[MAP-MechanoidMechanitor] Prevented node self-control: pawn={pawn.LabelShort}");
                }

                return false;
            }

            return true;
        }
    }
}
