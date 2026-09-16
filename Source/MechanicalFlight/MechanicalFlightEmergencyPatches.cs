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
                if (record.IsExternallyPowered)
                {
                    // 受控退出仍有日常耗能；只延后零能量关机，不清空原有能源。
                    float next = __instance.CurLevel - __instance.FallPerDay / 400f;
                    if (next > 0f)
                        return true;
                    __instance.CurLevel = 0f;
                    record.PendingShutdownAfterLanding = true;
                }
                return false;
            }
            if (record.Phase == MechanicalFlightPhase.Landing
                && pawn.flight?.Flying == true)
            {
                // 普通降落期间能量归零：继续完成当前降落，落地后再执行原版关机。
                float landingNextLevel = __instance.CurLevel - __instance.FallPerDay / 400f;
                if (landingNextLevel > 0f)
                {
                    return true;
                }
                __instance.CurLevel = 0f;
                record.PendingShutdownAfterLanding = true;
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

    [HarmonyPatch(typeof(Ability), nameof(Ability.CanCast), MethodType.Getter)]
    internal static class MechanicalFlightAbilityCastPatch
    {
        public static void Postfix(Ability __instance, ref AcceptanceReport __result)
        {
            if (MechanicalFlightEmergencyUtility.IsEmergencySequence(__instance.pawn))
            {
                __result = "MAP_MechanicalFlight_EmergencyLandingBlocked".Translate();
            }
        }
    }

    [HarmonyPatch(
        typeof(Verb_CastAbility),
        nameof(Verb_CastAbility.TryStartCastOn),
        new[]
        {
            typeof(LocalTargetInfo),
            typeof(LocalTargetInfo),
            typeof(bool),
            typeof(bool),
            typeof(bool),
            typeof(bool)
        })]
    internal static class MechanicalFlightAbilityExecutionPatch
    {
        public static bool Prefix(Verb_CastAbility __instance, ref bool __result)
        {
            Ability? ability = __instance?.Ability;
            if (!MechanicalFlightEmergencyUtility.IsEmergencySequence(ability?.pawn))
            {
                return true;
            }

            __result = false;
            return false;
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
            // 先终止提供者的会话，避免其坠毁爆炸重入时重复处理乘员。
            if (__instance.Dead)
                GroupFlightUtility.NotifyProviderDied(__instance);
            MechanicalFlightEmergencyUtility.ResolveDeathCrash(__instance, __state);
        }
    }

}
