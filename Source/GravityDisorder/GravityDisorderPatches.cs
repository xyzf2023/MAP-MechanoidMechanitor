using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(Pawn_Thinker), nameof(Pawn_Thinker.ConstantThinkTree), MethodType.Getter)]
    internal static class GravityDisorderThinkTreePatch
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Pawn_Thinker __instance, ref ThinkTreeDef __result)
        {
            if (GravityDisorderUtility.IsAffected(__instance.pawn))
                __result = GravityDisorderDefOf.MAP_GravityDisorderThinkTree;
        }
    }

    [HarmonyPatch]
    internal static class GravityDisorderControlRecoveryPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Pawn), "Tick");
            yield return AccessTools.Method(typeof(Pawn), "TickInterval");
        }

        // 读档或容器中新增的 Hediff 可能未能当场启动任务。
        // 在下一次 Pawn 更新前补齐，随后放行全部原版更新，不冻结健康/需求计时。
        public static void Prefix(Pawn __instance) =>
            GravityDisorderUtility.EnsureControl(GravityDisorderUtility.GetEffect(__instance));
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryTakeOrderedJob))]
    internal static class GravityDisorderOrderedJobPatch
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(Pawn ___pawn, ref bool __result)
        {
            if (!GravityDisorderUtility.IsAffected(___pawn))
                return true;
            // 在原版申请预留和入队前拒绝，不把命令延后到解除时执行。
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    internal static class GravityDisorderDirectJobPatch
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(Pawn ___pawn, Job newJob)
        {
            if (newJob == null || GravityDisorderUtility.AllowsJob(___pawn, newJob))
                return true;
            // 直接启动任务的调用者可能仍持有该 Job，不在这里擅自归还对象池。
            // 正常 AI 已由常驻树拦截，此处仅封住需求、事件等旁路。
            if (!ReferenceEquals(___pawn.CurJob, newJob))
                ___pawn.ClearReservationsForJob(newJob);
            return false;
        }
    }

    [HarmonyPatch]
    internal static class GravityDisorderPathPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.StartPath));
            yield return AccessTools.Method(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.PatherTick));
        }

        [HarmonyPriority(Priority.First)]
        public static bool Prefix(Pawn_PathFollower __instance, Pawn ___pawn)
        {
            if (!GravityDisorderUtility.BlocksMovement(___pawn))
                return true;
            __instance.StopDead();
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.CanTakeOrder), MethodType.Getter)]
    internal static class GravityDisorderCanTakeOrderPatch
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Pawn __instance, ref bool __result)
        {
            if (GravityDisorderUtility.IsAffected(__instance))
                __result = false;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    internal static class GravityDisorderGizmosPatch
    {
        [HarmonyPriority(Priority.Last)]
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
        {
            foreach (Gizmo gizmo in __result)
            {
                if (GravityDisorderUtility.IsAffected(__instance) && gizmo is Command command)
                    command.Disable(GravityDisorderUtility.BlockedReason);
                yield return gizmo;
            }
        }
    }

    [HarmonyPatch(typeof(Ability), nameof(Ability.CanCast), MethodType.Getter)]
    internal static class GravityDisorderAbilityAvailabilityPatch
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Ability __instance, ref AcceptanceReport __result)
        {
            if (GravityDisorderUtility.IsAffected(__instance.pawn))
                __result = GravityDisorderUtility.BlockedReason;
        }
    }

    [HarmonyPatch]
    internal static class GravityDisorderAbilityActivationPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Ability), nameof(Ability.Activate),
                new[] { typeof(LocalTargetInfo), typeof(LocalTargetInfo) });
            yield return AccessTools.Method(typeof(Ability), nameof(Ability.Activate),
                new[] { typeof(GlobalTargetInfo) });
        }

        public static bool Prefix(Ability __instance, ref bool __result)
        {
            if (!GravityDisorderUtility.IsAffected(__instance.pawn))
                return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.TryStartCastOn), new[]
    {
        typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(bool),
        typeof(bool), typeof(bool), typeof(bool)
    })]
    internal static class GravityDisorderVerbStartPatch
    {
        public static bool Prefix(Verb __instance, ref bool __result)
        {
            if (!GravityDisorderUtility.IsAffected(__instance.CasterPawn))
                return true;
            GravityDisorderUtility.CancelBurst(__instance);
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.VerbTick))]
    internal static class GravityDisorderVerbTickPatch
    {
        public static void Prefix(Verb __instance)
        {
            if (GravityDisorderUtility.IsAffected(__instance.CasterPawn))
                GravityDisorderUtility.CancelBurst(__instance);
        }
    }

    [HarmonyPatch(typeof(Verb), "TryCastNextBurstShot")]
    internal static class GravityDisorderBurstShotPatch
    {
        public static bool Prefix(Verb __instance)
        {
            if (!GravityDisorderUtility.IsAffected(__instance.CasterPawn))
                return true;
            GravityDisorderUtility.CancelBurst(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_MeleeVerbs), nameof(Pawn_MeleeVerbs.TryMeleeAttack))]
    internal static class GravityDisorderMeleePatch
    {
        public static bool Prefix(Pawn_MeleeVerbs __instance, ref bool __result)
        {
            if (!GravityDisorderUtility.IsAffected(__instance.Pawn))
                return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(CompTurretGun), "CanShoot", MethodType.Getter)]
    internal static class GravityDisorderTurretPatch
    {
        public static void Postfix(CompTurretGun __instance, ref bool __result)
        {
            if (!GravityDisorderUtility.IsAffected(__instance.parent as Pawn))
                return;
            __result = false;
            if (__instance.gun != null)
                GravityDisorderUtility.CancelBurst(__instance.AttackVerb);
        }
    }

    [HarmonyPatch(typeof(PawnUtility), nameof(PawnUtility.IsInteractionBlocked))]
    internal static class GravityDisorderSocialPatch
    {
        public static void Postfix(Pawn pawn, ref bool __result)
        {
            // 原版存在精神状态时会提前返回，可能跳过 Hediff 的 blocksSocialInteraction。
            if (GravityDisorderUtility.IsAffected(pawn))
                __result = true;
        }
    }
}
