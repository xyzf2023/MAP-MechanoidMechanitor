using HarmonyLib;
using MAP_MechanoidMechanitor;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
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

            if (!MAPMechanitorNodeUtility.IsMechanitorNodeController(pawn)
                && !MechanoidMechanitorCapabilityUtility.HasCapability(
                    pawn,
                    MechanoidMechanitorCapability.MechanitorControl))
            {
                return;
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

            if (!MAPMechanitorNodeUtility.IsMechanitorNodeController(pawn)
                && !MechanoidMechanitorCapabilityUtility.HasCapability(
                    pawn,
                    MechanoidMechanitorCapability.MechanitorControl))
            {
                return;
            }

            if (pawn.mechanitor != null)
            {
                __result = true;
            }
        }
    }
}
