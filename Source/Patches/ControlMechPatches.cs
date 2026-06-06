using HarmonyLib;
using RimWorld;
using Verse;

namespace MMT
{
    [HarmonyPatch(typeof(MechanitorUtility), nameof(MechanitorUtility.CanControlMech))]
    public static class ControlMechPatches_CanControlMech
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn pawn, Pawn mech, ref AcceptanceReport __result)
        {
            if (pawn != null && mech != null && pawn == mech && OverseerlessMechanitorUtility.IsNode(pawn))
            {
                __result = false;
                if (Prefs.DevMode)
                {
                    Log.Message($"[MMT] Prevented node self-control: pawn={pawn.LabelShort}");
                }

                return false;
            }

            if (pawn == null || mech == null)
            {
                return true;
            }

            if (!ModsConfig.BiotechActive)
            {
                return true;
            }

            if (pawn.Faction == null || !pawn.Faction.IsPlayerSafe())
            {
                return true;
            }

            if (!OverseerlessMechanitorUtility.IsNode(pawn))
            {
                return true;
            }

            OverseerlessMechanitorUtility.EnsureBasicTrackers(pawn);
            OverseerlessMechanitorUtility.ClearExternalOverseerIfNode(pawn);
            return true;
        }
    }
}
