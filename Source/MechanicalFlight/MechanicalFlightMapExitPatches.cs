using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    internal static class MechanicalFlightMapExitUtility
    {
        internal static bool IsExitMove(Pawn pawn, IntVec3 cell)
        {
            return !GroupFlightUtility.IsManaged(pawn) && pawn.Map != null && pawn.Map.exitMapGrid.IsExitCell(cell)
                && (!pawn.IsColonyMech
                    || MAPTravelUtility.IsEligibleRegisteredMechanoidMechanitor(pawn));
        }

        internal static void NotifyJobStarted(Pawn pawn, Job job)
        {
            if (!GameComponent_MechanicalFlightRegistry.TryGetRecord(pawn, out var record)
                || record == null || !record.IsRuntimeActive || record.IsEmergencySequence
                || record.IsExternallyPowered)
            {
                return;
            }

            if (job.def == JobDefOf.Goto && job.exitMapOnArrival)
            {
                record.PendingExitMap = pawn.Map;
            }
            else if (job.playerForced)
            {
                // 自动 Wait 等待不得抹掉已完成飞行 Goto 的意图；新玩家命令则替换它。
                record.PendingExitMap = null;
            }
        }

        internal static void ResumeAfterLanding(Pawn pawn, Map? exitMap)
        {
            // 调用方已清空记录并完成落地/停机结算。本次最多重判一次，不回放旧任务。
            if (exitMap == null || !pawn.Spawned || pawn.Map != exitMap
                || pawn.Dead || pawn.Downed || !pawn.Awake() || pawn.InMentalState
                || pawn.jobs == null || MechanicalFlightUtility.IsAirborne(pawn)
                || !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Moving)
                || (MechanicalFlightEnergyUtility.TryGetEnergyFraction(pawn, out float energy)
                    && energy <= 0f)
                || !exitMap.exitMapGrid.IsExitCell(pawn.Position)
                || !CaravanExitMapUtility.CanExitMapAndJoinOrCreateCaravanNow(pawn))
            {
                return;
            }

            // 用当前位置的地面 Goto 走原版撤离前通知、Anomaly 检查及远行队流程。
            Job job = JobMaker.MakeJob(JobDefOf.Goto, pawn.Position);
            job.exitMapOnArrival = true;
            job.failIfCantJoinOrCreateCaravan = true;
            pawn.jobs.TryTakeOrderedJob(job);
        }
    }

    [HarmonyPatch(typeof(FloatMenuOptionProvider_DraftedMove),
        nameof(FloatMenuOptionProvider_DraftedMove.PawnGotoAction))]
    internal static class MechanicalFlightSameCellExitOrderPatch
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Pawn pawn, IntVec3 clickCell, IntVec3 gotoLoc)
        {
            // 原版点击脚下会直接结束 Goto，不创建新任务，需单独更新撤离/取消意图。
            if (pawn?.Position == gotoLoc
                && GameComponent_MechanicalFlightRegistry.TryGetRecord(pawn, out var record)
                && record != null && record.IsRuntimeActive && !record.IsEmergencySequence)
            {
                record.PendingExitMap = MechanicalFlightMapExitUtility.IsExitMove(pawn, clickCell)
                    ? pawn.Map : null;
            }
        }
    }

    [HarmonyPatch(typeof(JobDriver_Goto), "TryExitMap")]
    internal static class MechanicalFlightGotoExitMapPatch
    {
        public static bool Prefix(JobDriver_Goto __instance)
        {
            Pawn pawn = __instance.pawn;
            if (!MechanicalFlightUtility.IsAirborne(pawn))
            {
                return true;
            }

            if (GameComponent_MechanicalFlightRegistry.TryGetRecord(pawn, out var record)
                && record != null && !record.IsExternallyPowered
                && pawn.Spawned && pawn.Map.exitMapGrid.MapUsesExitGrid)
            {
                record.PendingExitMap = pawn.Map;
            }
            // 在机械师遗留机械体提示及 Anomaly 离图检查之前阻止撤离。
            return false;
        }
    }
}
