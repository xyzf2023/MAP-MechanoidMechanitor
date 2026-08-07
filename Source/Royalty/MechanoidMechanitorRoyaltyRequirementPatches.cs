using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(
        typeof(Pawn_RoyaltyTracker),
        nameof(Pawn_RoyaltyTracker.CanRequireThroneroom))]
    public static class Patch_PawnRoyaltyTracker_CanRequireThroneroom_Mechanitor
    {
        [HarmonyPostfix]
        public static void Postfix(
            Pawn_RoyaltyTracker __instance,
            ref bool __result)
        {
            if (__result
                || !MechanoidMechanitorRoyaltyUtility
                    .IsRoyaltyEligibleMechanitor(__instance.pawn))
            {
                return;
            }

            __result = __instance.allowRoomRequirements
                && !__instance.pawn.IsQuestLodger();
        }
    }

    [HarmonyPatch(
        typeof(Pawn_RoyaltyTracker),
        nameof(Pawn_RoyaltyTracker.CanRequireBedroom))]
    public static class Patch_PawnRoyaltyTracker_CanRequireBedroom_Mechanitor
    {
        [HarmonyPostfix]
        public static void Postfix(
            Pawn_RoyaltyTracker __instance,
            ref bool __result)
        {
            if (MechanoidMechanitorRoyaltyUtility
                .IsRoyaltyEligibleMechanitor(__instance.pawn))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch(
        typeof(RoyalTitleDef),
        nameof(RoyalTitleDef.GetBedroomRequirements))]
    public static class Patch_RoyalTitleDef_GetBedroomRequirements_Mechanitor
    {
        [HarmonyPostfix]
        public static void Postfix(
            Pawn p,
            ref IEnumerable<RoomRequirement> __result)
        {
            if (MechanoidMechanitorRoyaltyUtility
                .IsRoyaltyEligibleMechanitor(p))
            {
                __result = null!;
            }
        }
    }
}
