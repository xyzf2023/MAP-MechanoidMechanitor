using HarmonyLib;
using MAP_MechanoidMechanitor.Scenarios;
using System;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_Pawn_Kill_EmergencyMechanicalConsciousnessTransfer
    {
        [HarmonyPrefix]
        public static void Prefix(Pawn __instance, ref bool __state)
        {
            __state = EmergencyMechanicalConsciousnessTransferUtility
                .TryBeginEmergencyTransferAttempt(__instance);
            if (__state)
            {
                EmergencyMechanicalConsciousnessTransferUtility
                    .TryExecuteEmergencyTransfer(__instance);
            }
        }

        [HarmonyFinalizer]
        public static Exception? Finalizer(Pawn __instance, bool __state, Exception? __exception)
        {
            if (__state)
            {
                EmergencyMechanicalConsciousnessTransferUtility.ClearAttemptGuard(__instance);
            }

            return __exception;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Destroy))]
    public static class Patch_Pawn_Destroy_EmergencyMechanicalConsciousnessTransfer
    {
        [HarmonyPrefix]
        public static void Prefix(Pawn __instance, ref bool __state)
        {
            __state = EmergencyMechanicalConsciousnessTransferUtility
                .TryBeginEmergencyTransferAttempt(__instance);
            if (__state)
            {
                EmergencyMechanicalConsciousnessTransferUtility
                    .TryExecuteEmergencyTransfer(__instance);
            }
        }

        [HarmonyFinalizer]
        public static Exception? Finalizer(Pawn __instance, bool __state, Exception? __exception)
        {
            if (__state)
            {
                EmergencyMechanicalConsciousnessTransferUtility.ClearAttemptGuard(__instance);
            }

            return __exception;
        }
    }
}
