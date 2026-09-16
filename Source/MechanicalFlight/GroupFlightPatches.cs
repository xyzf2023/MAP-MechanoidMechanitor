using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    internal static class GroupFlightPatches
    {
        internal static bool AllowsJob(Pawn pawn, Job? job)
        {
            if (GravityDisorderUtility.IsWaitJob(job) && GravityDisorderUtility.IsAffected(pawn))
                return true;
            if (!GroupFlightUtility.IsManaged(pawn) || job == null)
                return true;
            if (job.def == null)
                return false;
            if (MechanicalFlightEmergencyUtility.IsEmergencySequence(pawn))
                return job.def == MAPMechanitor_JobDefOf.MAP_MechanicalFlightEmergencyLanding;
            if (!GroupFlightUtility.BlocksIndependentMovement(pawn))
                return MechanicalFlightJobPatch.IsHoverCompatible(job);
            // 被托举的乘员可以原地战斗，但不能用攻击/工作任务绕过移动锁。
            string name = job.def.defName;
            return name == "Wait" || name == "Wait_Combat" || name == "Wait_MaintainPosture"
                || name == "StandAndStare" || name == "AttackStatic"
                || name == "UseVerbOnThingStatic" || name == "UseVerbOnThingStaticReserve";
        }
    }

    [HarmonyPatch(typeof(Pawn_DraftController), nameof(Pawn_DraftController.Drafted), MethodType.Setter)]
    internal static class GroupFlightUndraftPatch
    {
        public static void Prefix(Pawn_DraftController __instance, out bool __state) =>
            __state = __instance.Drafted;

        public static void Postfix(Pawn_DraftController __instance, bool __state)
        {
            if (__state && !__instance.Drafted)
                GroupFlightUtility.NotifyUndrafted(__instance.pawn);
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.DrawLinesBetweenTargets))]
    internal static class GroupFlightJobLinesPatch
    {
        public static bool Prefix(Pawn ___pawn) =>
            !GroupFlightUtility.BlocksIndependentMovement(___pawn);
    }

    [HarmonyPatch(typeof(FloatMenuOptionProvider_DraftedMove),
        nameof(FloatMenuOptionProvider_DraftedMove.PawnGotoAction))]
    internal static class GroupFlightGotoFeedbackPatch
    {
        public static bool Prefix(Pawn pawn) =>
            !GroupFlightUtility.BlocksIndependentMovement(pawn);
    }

    [HarmonyPatch(typeof(FloatMenuOptionProvider_DraftedMove),
        nameof(FloatMenuOptionProvider_DraftedMove.PawnCanGoto))]
    internal static class GroupFlightGotoEligibilityPatch
    {
        public static bool Prefix(Pawn pawn, ref AcceptanceReport __result)
        {
            if (!GroupFlightUtility.BlocksIndependentMovement(pawn))
                return true;
            __result = "MAP_GroupFlight_PassengerMoveBlocked".Translate();
            return false;
        }
    }

    [HarmonyPatch(typeof(MultiPawnGotoController), nameof(MultiPawnGotoController.AddPawn))]
    internal static class GroupFlightMultiGotoPatch
    {
        // 原版拖拽编队会在下令前绘制目标标记；从加入阶段就排除群体飞行单位。
        // 提供者只走现有空中移动菜单的专用回调。
        public static bool Prefix(Pawn pawn) => !GroupFlightUtility.IsManaged(pawn);
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Notify_Teleported))]
    internal static class GroupFlightTeleportPatch
    {
        public static void Postfix(Pawn __instance)
        {
            var session = GroupFlightUtility.Member(__instance)?.Session;
            if (session?.Provider != null && !session.Closing)
                GroupFlightUtility.SynchronizeFormation(session.Provider);
        }
    }

    [HarmonyPatch(typeof(Pawn_FlightTracker), nameof(Pawn_FlightTracker.ForceLand))]
    internal static class GroupFlightControlledLandingPatch
    {
        private static readonly AccessTools.FieldRef<Pawn_FlightTracker, Pawn> PawnField =
            AccessTools.FieldRefAccess<Pawn_FlightTracker, Pawn>("pawn");

        [HarmonyPriority(Priority.First)]
        public static bool Prefix(Pawn_FlightTracker __instance)
        {
            // 原版 MakeDowned 最后会 ForceLand；受控退出必须先接近安全落点。
            // 正常/应急着陆和坠毁都先切换阶段，再调用 ForceLand，仍正常放行。
            return !GameComponent_MechanicalFlightRegistry.TryGetRecord(
                    PawnField(__instance), out var record)
                || record?.IsExternallyPowered != true
                || (!record.IsCruising && record.Phase != MechanicalFlightPhase.EmergencyApproach);
        }
    }
}
