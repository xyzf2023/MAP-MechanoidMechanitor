using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(Pawn_MechanitorTracker), nameof(Pawn_MechanitorTracker.CanControlMechs), MethodType.Getter)]
    public static class ProxySubchainCanControlPatches
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn_MechanitorTracker __instance, ref AcceptanceReport __result)
        {
            if (!__result.Accepted && ProxySubchainUtility.CanMaintainControl(__instance.Pawn))
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_MechanitorTracker), nameof(Pawn_MechanitorTracker.Notify_Downed))]
    public static class ProxySubchainDownedPatches
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn_MechanitorTracker __instance)
        {
            return !ProxySubchainUtility.CanMaintainControl(__instance.Pawn);
        }
    }

    [HarmonyPatch(typeof(Pawn_MechanitorTracker), nameof(Pawn_MechanitorTracker.Notify_DeSpawned))]
    public static class ProxySubchainDespawnedPatches
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn_MechanitorTracker __instance, DestroyMode mode)
        {
            return mode == DestroyMode.WillReplace
                || !ProxySubchainUtility.ShouldPreserveControlDuringCurrentHolderTransition(
                    __instance.Pawn);
        }
    }

    [HarmonyPatch(typeof(Pawn_CarryTracker), nameof(Pawn_CarryTracker.TryStartCarry), new Type[] { typeof(Thing) })]
    public static class ProxySubchainCarryPatches
    {
        [HarmonyPrefix]
        public static void Prefix(Thing item, out Pawn? __state)
        {
            __state = ProxySubchainUtility.BeginSupportedHolderTransition(item);
        }

        [HarmonyFinalizer]
        public static Exception? Finalizer(Exception? __exception, Pawn? __state)
        {
            ProxySubchainUtility.EndSupportedHolderTransition(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Pawn_CarryTracker), nameof(Pawn_CarryTracker.TryStartCarry), new Type[] { typeof(Thing), typeof(int), typeof(bool) })]
    public static class ProxySubchainCarryCountPatches
    {
        [HarmonyPrefix]
        public static void Prefix(Thing item, out Pawn? __state)
        {
            __state = ProxySubchainUtility.BeginSupportedHolderTransition(item);
        }

        [HarmonyFinalizer]
        public static Exception? Finalizer(Exception? __exception, Pawn? __state)
        {
            ProxySubchainUtility.EndSupportedHolderTransition(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Building_CryptosleepCasket), nameof(Building_CryptosleepCasket.TryAcceptThing), new Type[] { typeof(Thing), typeof(bool) })]
    public static class ProxySubchainCryptosleepPatches
    {
        [HarmonyPrefix]
        public static void Prefix(Thing thing, out Pawn? __state)
        {
            __state = ProxySubchainUtility.BeginSupportedHolderTransition(thing);
        }

        [HarmonyFinalizer]
        public static Exception? Finalizer(Exception? __exception, Pawn? __state)
        {
            ProxySubchainUtility.EndSupportedHolderTransition(__state);
            return __exception;
        }
    }
}
