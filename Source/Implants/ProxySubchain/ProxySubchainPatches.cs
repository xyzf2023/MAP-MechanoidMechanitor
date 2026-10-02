using System;
using System.Collections.Generic;
using System.Reflection;
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
        public static void Prefix(Building_CryptosleepCasket __instance, Thing thing, out Pawn? __state)
        {
            __state = ProxySubchainUtility.IsSupportedHolder(__instance, thing?.MapHeld)
                ? ProxySubchainUtility.BeginSupportedHolderTransition(thing)
                : null;
        }

        [HarmonyFinalizer]
        public static Exception? Finalizer(Exception? __exception, Pawn? __state)
        {
            ProxySubchainUtility.CompleteSupportedHolderTransition(__state);
            return __exception;
        }
    }

    // 塑形仓的字符串入口和直接周期入口都需要保护；嵌套调用由深度计数处理。
    [HarmonyPatch]
    public static class ProxySubchainBiosculpterPatches
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(CompBiosculpterPod),
                nameof(CompBiosculpterPod.TryAcceptPawn), new[] { typeof(Pawn), typeof(string) });
            yield return AccessTools.Method(typeof(CompBiosculpterPod),
                nameof(CompBiosculpterPod.TryAcceptPawn), new[] { typeof(Pawn), typeof(CompBiosculpterPod_Cycle) });
        }

        [HarmonyPrefix]
        public static void Prefix(CompBiosculpterPod __instance, Pawn pawn, out Pawn? __state)
        {
            __state = null;
            if (!ProxySubchainUtility.IsSupportedBiosculpterPod(__instance, pawn?.MapHeld))
            {
                return;
            }

            __state = ProxySubchainUtility.BeginSupportedHolderTransition(pawn);
        }

        [HarmonyFinalizer]
        public static Exception? Finalizer(Exception? __exception, Pawn? __state)
        {
            ProxySubchainUtility.CompleteSupportedHolderTransition(__state);
            return __exception;
        }
    }

    // 基因提取器、成长舱和子核心扫描仪在加入容器前先将 Pawn 移出地图。
    [HarmonyPatch]
    public static class ProxySubchainEnterablePatches
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            Type[] parameters = { typeof(Pawn) };
            yield return AccessTools.Method(typeof(Building_GeneExtractor), nameof(Building_Enterable.TryAcceptPawn), parameters);
            yield return AccessTools.Method(typeof(Building_GrowthVat), nameof(Building_Enterable.TryAcceptPawn), parameters);
            yield return AccessTools.Method(typeof(Building_SubcoreScanner), nameof(Building_Enterable.TryAcceptPawn), parameters);
        }

        [HarmonyPrefix]
        public static void Prefix(Building_Enterable __instance, Pawn pawn, out Pawn? __state)
        {
            __state = ProxySubchainUtility.IsSupportedHolder(__instance, pawn?.MapHeld)
                ? ProxySubchainUtility.BeginSupportedHolderTransition(pawn)
                : null;
        }

        [HarmonyFinalizer]
        public static Exception? Finalizer(Exception? __exception, Pawn? __state)
        {
            ProxySubchainUtility.CompleteSupportedHolderTransition(__state);
            return __exception;
        }
    }

    // 吞噬者内仍存活的机械师沿用吞噬者的地图位置，不绕过伤害或死亡通知。
    [HarmonyPatch(typeof(CompDevourer), nameof(CompDevourer.StartDigesting))]
    public static class ProxySubchainDevourerPatches
    {
        [HarmonyPrefix]
        public static void Prefix(CompDevourer __instance, LocalTargetInfo target, out Pawn? __state)
        {
            Pawn? pawn = target.Thing as Pawn;
            __state = pawn != null && ProxySubchainUtility.IsSupportedHolder(__instance, pawn.MapHeld)
                ? ProxySubchainUtility.BeginSupportedHolderTransition(pawn)
                : null;
        }

        [HarmonyFinalizer]
        public static Exception? Finalizer(Exception? __exception, Pawn? __state)
        {
            ProxySubchainUtility.CompleteSupportedHolderTransition(__state);
            return __exception;
        }
    }

    // 跳跃本体使用 WillReplace，原版已保留征召；随行搬运物却会额外 DeSpawn。
    // MakeFlyer 返回后才由调用方生成载体，此处只结束保护，不在尚未生成时误判离图。
    [HarmonyPatch(typeof(PawnFlyer), nameof(PawnFlyer.MakeFlyer))]
    public static class ProxySubchainFlyerCarriedPawnPatches
    {
        [HarmonyPrefix]
        public static void Prefix(Pawn pawn, bool flyWithCarriedThing, out Pawn? __state)
        {
            __state = flyWithCarriedThing && pawn?.Spawned == true
                ? ProxySubchainUtility.BeginSupportedHolderTransition(pawn.carryTracker?.CarriedThing)
                : null;
        }

        [HarmonyFinalizer]
        public static Exception? Finalizer(Exception? __exception, Pawn? __state)
        {
            ProxySubchainUtility.EndSupportedHolderTransition(__state);
            return __exception;
        }
    }

    // 登入运输器或休眠舱时，原版先 DeSpawnOrDeselect，之后才加入实际容器。
    // 必须在此保护，不能仅在容器的 TryAcceptThing 中才开始保护。
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

            if (!IsEnteringSupportedContainer(pawn))
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

        private static bool IsEnteringSupportedContainer(Pawn pawn)
        {
            if (!ProxySubchainUtility.IsValidProxySubchainMechanitor(pawn))
            {
                return false;
            }

            if (pawn.jobs?.curDriver is JobDriver_EnterTransporter enterDriver)
            {
                return ProxySubchainUtility.IsSupportedTransporter(enterDriver.Transporter, pawn.Map);
            }

            return pawn.jobs?.curDriver is JobDriver_EnterCryptosleepCasket
                && pawn.CurJob?.targetA.Thing is Building_CryptosleepCasket casket
                && ProxySubchainUtility.IsSupportedHolder(casket, pawn.Map);
        }
    }

    // 玩家发射空投舱或穿梭机：转移到飞行运输舱后结束代理控制。
    [HarmonyPatch(typeof(CompLaunchable), nameof(CompLaunchable.TryLaunch))]
    public static class ProxySubchainTransporterLaunchCleanupPatches
    {
        [HarmonyPrefix]
        public static void Prefix(CompLaunchable __instance, out List<Pawn>? __state)
        {
            __state = ProxySubchainUtility
                .CollectProxySubchainMechanitorsInSupportedTransporters(__instance);
        }

        [HarmonyFinalizer]
        public static Exception? Finalizer(Exception? __exception, List<Pawn>? __state)
        {
            ProxySubchainUtility.UndraftProxySubchainMechsIfLeftSupportedHolders(__state);
            return __exception;
        }
    }

    // 帝国/任务穿梭机的起飞不经过 CompLaunchable，须单独覆盖其实际转移入口。
    [HarmonyPatch(typeof(ShipJob_FlyAway), nameof(ShipJob_FlyAway.TryStart))]
    public static class ProxySubchainShuttleLaunchCleanupPatches
    {
        [HarmonyPrefix]
        public static void Prefix(ShipJob_FlyAway __instance, out List<Pawn>? __state)
        {
            Thing? ship = __instance.transportShip?.shipThing;
            __state = ProxySubchainUtility.CollectProxySubchainMechanitorsInTransporter(
                ship?.TryGetComp<CompTransporter>());
        }

        [HarmonyFinalizer]
        public static Exception? Finalizer(Exception? __exception, List<Pawn>? __state)
        {
            ProxySubchainUtility.UndraftProxySubchainMechsIfLeftSupportedHolders(__state);
            return __exception;
        }
    }
}
