using HarmonyLib;
using MAP_MechanoidMechanitor;
using RimWorld;
using Verse;

namespace MMT
{
    [HarmonyPatch(typeof(MechanitorUtility), nameof(MechanitorUtility.ShouldBeMechanitor))]
    public static class MechanitorIdentityPatches
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn pawn, ref bool __result)
        {
            if (__result || pawn == null)
            {
                return;
            }

            if (!ModsConfig.BiotechActive)
            {
                return;
            }

            if (pawn.Faction == null || !pawn.Faction.IsPlayerSafe())
            {
                return;
            }

            if (!OverseerlessMechanitorUtility.IsMAPMechanitorNodeController(pawn))
            {
                return;
            }

            OverseerlessMechanitorUtility.EnsureBasicTrackers(pawn);

            if (OverseerlessMechanitorUtility.ShouldClearOwnExternalOverseer(pawn))
            {
                OverseerlessMechanitorUtility.ClearExternalOverseerIfNode(pawn);
            }

            __result = true;
        }
    }

    [HarmonyPatch(typeof(MechanitorUtility), nameof(MechanitorUtility.IsMechanitor))]
    public static class MechanitorIdentity_IsMechanitor_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn pawn, ref bool __result)
        {
            if (__result || pawn == null)
            {
                return;
            }

            if (!ModsConfig.BiotechActive)
            {
                return;
            }

            if (pawn.Faction == null || !pawn.Faction.IsPlayerSafe())
            {
                return;
            }

            if (!OverseerlessMechanitorUtility.IsMAPMechanitorNodeController(pawn))
            {
                return;
            }

            OverseerlessMechanitorUtility.EnsureBasicTrackers(pawn);
            __result = pawn.mechanitor != null;
        }
    }
}
