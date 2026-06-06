using HarmonyLib;
using RimWorld;
using Verse;

namespace MMT
{
    [HarmonyPatch(typeof(MechanitorUtility), nameof(MechanitorUtility.InMechanitorCommandRange))]
    public static class CommandRangePatches
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn mech, ref bool __result)
        {
            if (__result || mech == null || !ModsConfig.BiotechActive)
            {
                return;
            }

            if (mech.Faction == null || !mech.Faction.IsPlayerSafe())
            {
                return;
            }

            if (!OverseerlessMechanitorUtility.IsNode(mech))
            {
                return;
            }

            OverseerlessMechanitorUtility.EnsureBasicTrackers(mech);
            OverseerlessMechanitorUtility.ClearExternalOverseerIfNode(mech);
            __result = true;
        }
    }
}
