using HarmonyLib;
using System;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_Pawn_Kill_EmergencyMechanicalConsciousnessTransfer
    {
        public struct KillPatchState
        {
            public bool OwnsAttemptGuard;
            public bool SetBeingKilled;
        }

        [HarmonyPrefix]
        public static bool Prefix(Pawn __instance, ref KillPatchState __state)
        {
            __state = default;

            if (EmergencyMechanicalConsciousnessTransferUtility.IsAttemptInProgress(__instance))
            {
                return false;
            }

            if (!EmergencyMechanicalConsciousnessTransferUtility
                    .TryBeginEmergencyTransferAttempt(__instance))
            {
                return true;
            }

            __state.OwnsAttemptGuard = true;

            if (__instance.health != null && !__instance.health.isBeingKilled)
            {
                __instance.health.isBeingKilled = true;
                __state.SetBeingKilled = true;
            }

            EmergencyMechanicalConsciousnessTransferUtility
                .TryExecuteEmergencyTransfer(__instance);
            return true;
        }

        [HarmonyFinalizer]
        public static Exception? Finalizer(
            Pawn __instance,
            KillPatchState __state,
            Exception? __exception)
        {
            if (__state.OwnsAttemptGuard)
            {
                EmergencyMechanicalConsciousnessTransferUtility.ClearAttemptGuard(__instance);
            }

            if (__state.SetBeingKilled && __instance.health != null)
            {
                __instance.health.isBeingKilled = false;
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
            __state = false;

            if (EmergencyMechanicalConsciousnessTransferUtility.IsAttemptInProgress(__instance))
            {
                return;
            }

            if (!EmergencyMechanicalConsciousnessTransferUtility
                    .TryBeginEmergencyTransferAttempt(__instance))
            {
                return;
            }

            __state = true;

            Pawn_HealthTracker? health = __instance.health;
            bool wasBeingKilled = health != null && health.isBeingKilled;
            try
            {
                if (health != null)
                {
                    health.isBeingKilled = true;
                }

                EmergencyMechanicalConsciousnessTransferUtility
                    .TryExecuteEmergencyTransfer(__instance);
            }
            finally
            {
                if (health != null)
                {
                    health.isBeingKilled = wasBeingKilled;
                }
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
