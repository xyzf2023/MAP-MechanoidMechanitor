using HarmonyLib;
using MAP_MechanoidMechanitor.Scenarios;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_Pawn_Kill_EmergencyMechanicalConsciousnessTransfer
    {
        [HarmonyPrefix]
        public static void Prefix(Pawn __instance)
        {
            EmergencyMechanicalConsciousnessTransferUtility.TryTriggerFromKillPrefix(__instance);
        }

        [HarmonyPostfix]
        public static void Postfix(Pawn __instance)
        {
            EmergencyMechanicalConsciousnessTransferUtility.ClearAttemptGuard(__instance);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Destroy))]
    public static class Patch_Pawn_Destroy_EmergencyMechanicalConsciousnessTransfer
    {
        [HarmonyPrefix]
        public static void Prefix(Pawn __instance)
        {
            EmergencyMechanicalConsciousnessTransferUtility.TryTriggerFromDestroyPrefix(__instance);
        }

        [HarmonyPostfix]
        public static void Postfix(Pawn __instance)
        {
            EmergencyMechanicalConsciousnessTransferUtility.ClearAttemptGuard(__instance);
        }
    }
}
