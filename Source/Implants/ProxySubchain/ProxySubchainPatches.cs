using System;
using System.Collections.Generic;
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

    // 文化 DLC 塑形仓：进入塑形仓的短暂过程中，阻止 Pawn_MechanitorTracker.Notify_DeSpawned
    // 自动解除下属机械族征召。仅在真正进入塑形仓时给予这一受支持容器切換例外。
    [HarmonyPatch(typeof(CompBiosculpterPod), nameof(CompBiosculpterPod.TryAcceptPawn), new Type[] { typeof(Pawn), typeof(string) })]
    public static class ProxySubchainBiosculpterPatches
    {
        [HarmonyPrefix]
        public static void Prefix(CompBiosculpterPod __instance, Pawn pawn, out Pawn? __state)
        {
            __state = null;
            if (!ProxySubchainUtility.IsSupportedBiosculpterPod(__instance, pawn.Map))
            {
                return;
            }

            __state = ProxySubchainUtility.BeginSupportedHolderTransition(pawn);
        }

        [HarmonyFinalizer]
        public static Exception? Finalizer(Exception? __exception, Pawn? __state)
        {
            ProxySubchainUtility.EndSupportedHolderTransition(__state);
            return __exception;
        }
    }

    // 原版空投舱发射器：Pawn 通过 JobDriver_EnterTransporter 进入 CompTransporter 时，
    // DeSpawnOrDeselect 会触发 Notify_DeSpawned。仅在“当前 Pawn 为有效代理子链机械师、
    // 正执行 EnterTransporter、目标是原版空投舱发射器且发射器仍在当前地图”时才临时保留控制。
    // 该补丁不影响其他 Pawn 的 DeSpawnOrDeselect，也不影响非空投舱的离图逻辑。
    [HarmonyPatch(typeof(Thing), nameof(Thing.DeSpawnOrDeselect))]
    public static class ProxySubchainTransporterEnterDespawnPatches
    {
        [HarmonyPrefix]
        public static void Prefix(Thing __instance, out Pawn? __state)
        {
            __state = null;
            if (__instance is not Pawn pawn)
            {
                return;
            }

            if (!IsEnteringSupportedTransportPod(pawn))
            {
                return;
            }

            __state = ProxySubchainUtility.BeginSupportedHolderTransition(pawn);
        }

        [HarmonyFinalizer]
        public static Exception? Finalizer(Exception? __exception, Pawn? __state)
        {
            ProxySubchainUtility.EndSupportedHolderTransition(__state);
            return __exception;
        }

        private static bool IsEnteringSupportedTransportPod(Pawn pawn)
        {
            if (!ProxySubchainUtility.IsValidProxySubchainMechanitor(pawn))
            {
                return false;
            }

            if (pawn.jobs?.curDriver is not JobDriver_EnterTransporter enterDriver)
            {
                return false;
            }

            CompTransporter? transporter = enterDriver.Transporter;
            if (!ProxySubchainUtility.IsOriginalTransportPodTransporter(transporter))
            {
                return false;
            }

            Thing? parentBuilding = transporter.parent;
            return parentBuilding != null
                && parentBuilding.Spawned
                && parentBuilding.Map == pawn.Map;
        }
    }

    // 原版空投舱发射器真正发射：发射会将 Pawn 从地图内空投舱发射器转移到飞行空投舱/世界对象，
    // 此时代理子链不再能维持控制。发射前锁定本次发射组中处于原版空投舱发射器的有效代理子链机械师，
    // 发射后只对确实已离开白名单状态者解除下属机械族征召；发射被取消或仍停留地图内者不受影响。
    [HarmonyPatch(typeof(CompLaunchable), nameof(CompLaunchable.TryLaunch))]
    public static class ProxySubchainTransporterLaunchCleanupPatches
    {
        [HarmonyPrefix]
        public static void Prefix(CompLaunchable __instance, out List<Pawn>? __state)
        {
            __state = ProxySubchainUtility
                .CollectProxySubchainMechanitorsInOriginalTransportPods(__instance);
        }

        [HarmonyFinalizer]
        public static Exception? Finalizer(Exception? __exception, List<Pawn>? __state)
        {
            ProxySubchainUtility.UndraftProxySubchainMechsIfLeftSupportedHolders(__state);
            return __exception;
        }
    }
}
