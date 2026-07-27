using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    internal static class MAPHostilityResponseUtility
    {
        internal static bool CanUseConfigurableHostilityResponse(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead)
            {
                return false;
            }

            if (pawn.Faction != Faction.OfPlayer)
            {
                return false;
            }

            if (pawn.HostFaction != null)
            {
                return false;
            }

            if (pawn.RaceProps == null || !pawn.RaceProps.IsMechanoid)
            {
                return false;
            }

            return MAPMechanitorNodeUtility.IsMechanitorNodeController(pawn);
        }
    }

    [HarmonyPatch(
        typeof(Pawn_PlayerSettings),
        nameof(Pawn_PlayerSettings.UsesConfigurableHostilityResponse),
        MethodType.Getter)]
    internal static class Patch_Pawn_PlayerSettings_UsesConfigurableHostilityResponse
    {
        [HarmonyPostfix]
        private static void Postfix(Pawn ___pawn, ref bool __result)
        {
            if (__result)
            {
                return;
            }

            if (MAPHostilityResponseUtility.CanUseConfigurableHostilityResponse(___pawn))
            {
                __result = true;
            }
        }
    }
}
