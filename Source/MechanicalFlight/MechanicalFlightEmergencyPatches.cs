using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(Need_MechEnergy), nameof(Need_MechEnergy.NeedInterval))]
    internal static class MechanicalFlightEnergyDepletionPatch
    {
        private static readonly AccessTools.FieldRef<Need, Pawn> PawnField =
            AccessTools.FieldRefAccess<Need, Pawn>("pawn");

        public static bool Prefix(Need_MechEnergy __instance)
        {
            Pawn pawn = PawnField(__instance);
            if (!GameComponent_MechanicalFlightRegistry.TryGetRecord(
                    pawn, out MechanicalFlightAuthorizationRecord? record)
                || record == null || !record.IsRuntimeActive)
            {
                return true;
            }
            if (record.IsEmergencySequence)
            {
                // 紧急流程中冻结零能量，落地后再交还原版关机逻辑。
                return false;
            }

            float nextLevel = __instance.CurLevel - __instance.FallPerDay / 400f;
            if (nextLevel > 0f || pawn.flight?.Flying != true)
            {
                return true;
            }

            __instance.CurLevel = 0f;
            return !MechanicalFlightEmergencyUtility.TryBeginEmergencySequence(pawn);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.CanTakeOrder), MethodType.Getter)]
    internal static class MechanicalFlightEmergencyOrderPatch
    {
        public static void Postfix(Pawn __instance, ref bool __result)
        {
            if (MechanicalFlightEmergencyUtility.IsEmergencySequence(__instance))
            {
                __result = false;
            }
        }
    }
    [HarmonyPatch(typeof(Pawn_HealthTracker), "MakeDowned")]
    internal static class MechanicalFlightDownedCrashPatch
    {
        private static readonly AccessTools.FieldRef<Pawn_HealthTracker, Pawn> PawnField =
            AccessTools.FieldRefAccess<Pawn_HealthTracker, Pawn>("pawn");

        [HarmonyPostfix]
        public static void Postfix(Pawn_HealthTracker __instance)
        {
            MechanicalFlightEmergencyUtility.TryCrashFromDowned(PawnField(__instance));
        }
    }

    [HarmonyPatch(
        typeof(Pawn),
        nameof(Pawn.Kill),
        new[] { typeof(DamageInfo?), typeof(Hediff) })]
    internal static class MechanicalFlightDeathCrashPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        public static void Prefix(
            Pawn __instance,
            out MechanicalFlightEmergencyUtility.DeathCrashState __state)
        {
            MechanicalFlightEmergencyUtility.CaptureDeathCrash(
                __instance, out __state);
        }

        [HarmonyPostfix]
        public static void Postfix(
            Pawn __instance,
            MechanicalFlightEmergencyUtility.DeathCrashState __state)
        {
            MechanicalFlightEmergencyUtility.ResolveDeathCrash(__instance, __state);
        }
    }

}
