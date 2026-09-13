using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 合体快速飞行的落点规划。只负责两件事：
    /// 判断源机械族当前是否允许进入合体快速转移，以及在目标人类周围
    /// 搜索收益最高的合法降落点。真正的起飞、升空与重定位由 Job 流程执行。
    /// </summary>
    internal static class MechFusionRelocationUtility
    {
        /// <summary>
        /// 源机械族是否满足合体快速转移的全部前置条件：
        /// 统一飞行授权存在、当前不处于异常状态、当前所在位置能够合法起飞。
        /// 起飞规则完全复用 MechanicalFlightUtility.CanBeginTakeoff。
        /// </summary>
        internal static bool CanSourceUseFusionFlight(Pawn? source)
        {
            if (source == null
                || source.Dead
                || source.Downed
                || !source.Spawned
                || source.Map == null
                || source.InMentalState)
            {
                return false;
            }

            if (!GameComponent_MechanicalFlightRegistry.TryGetRecord(
                    source,
                    out MechanicalFlightAuthorizationRecord? record)
                || record?.Profile == null
                || record.IsRuntimeActive
                || record.IsEmergencySequence)
            {
                return false;
            }

            return MechanicalFlightUtility.CanBeginTakeoff(
                source,
                record,
                requireDrafted: false,
                out _);
        }

        /// <summary>
        /// 以 wearer 为中心搜索半径 8 的候选降落格：
        /// 先通过统一基础降落判断，再从落点实际寻路走到 wearer 周围相邻格。
        /// 排序优先级为 B 最短、与 wearer 直线距离最近、固定格子次序。
        /// 所有 PawnPath 都由 MechFusionApproachUtility 申请并释放。
        /// </summary>
        internal static bool TryFindBestLandingCell(
            Pawn source,
            Pawn wearer,
            out IntVec3 bestCell,
            out int bestPathSteps)
        {
            bestCell = IntVec3.Invalid;
            bestPathSteps = 0;
            Map? map = source.Map;
            if (map == null || wearer.Map != map)
            {
                return false;
            }

            int radius = MechFusionApproachTuning.LandingSearchRadius;
            IntVec3 center = wearer.Position;
            bool found = false;
            int bestSteps = int.MaxValue;
            int bestStraightSquared = int.MaxValue;

            for (int dz = -radius; dz <= radius; dz++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    IntVec3 cell = new IntVec3(
                        center.x + dx,
                        0,
                        center.z + dz);
                    if (!cell.InBounds(map) || cell == source.Position)
                    {
                        continue;
                    }

                    if (!MechanicalFlightUtility.IsBaseLandingCellValid(
                            cell,
                            source,
                            map))
                    {
                        continue;
                    }

                    // 切比雪夫距离是 8 向步行步数的下界：
                    // 超过当前最优 B 的候选不可能改善结果，直接跳过。
                    int lowerBound = Mathf.Max(
                        Mathf.Abs(dx),
                        Mathf.Abs(dz));
                    if (found && lowerBound > bestSteps)
                    {
                        continue;
                    }

                    if (!MechFusionApproachUtility.TryComputePathSteps(
                            source,
                            cell,
                            new LocalTargetInfo(wearer),
                            PathEndMode.Touch,
                            out int steps))
                    {
                        continue;
                    }

                    int straightSquared = dx * dx + dz * dz;
                    if (!found
                        || steps < bestSteps
                        || (steps == bestSteps
                            && straightSquared < bestStraightSquared))
                    {
                        found = true;
                        bestCell = cell;
                        bestSteps = steps;
                        bestStraightSquared = straightSquared;
                    }
                }
            }

            if (!found)
            {
                return false;
            }

            bestPathSteps = bestSteps;
            return true;
        }

        /// <summary>
        /// 把源机械族安全迁移到规划落点。只使用原版地图内重定位链：
        /// Thing.Position 负责 ThingGrid、CoverGrid、Region、可达性与地图网格，
        /// Pawn.Notify_Teleported 负责绘制插值、pather 路径与 Job 通知。
        /// endCurrentJob:false 保证合体接近 Job 在迁移后继续执行。
        /// </summary>
        internal static bool ApplyRelocation(
            Pawn? source,
            IntVec3 landingCell,
            out string? failureReason)
        {
            failureReason = null;
            if (source?.Map == null
                || source.Destroyed
                || source.Discarded
                || source.Dead
                || !source.Spawned)
            {
                failureReason =
                    "MAP_MechanoidMechanitor.Fusion.Failure.SourceUnavailable"
                        .Translate();
                return false;
            }

            if (!landingCell.InBounds(source.Map)
                || !MechanicalFlightUtility.IsBaseLandingCellValid(
                    landingCell,
                    source,
                    source.Map))
            {
                failureReason =
                    "MAP_MechanoidMechanitor.Fusion.Approach.LandingInvalid"
                        .Translate();
                return false;
            }

            source.pather?.StopDead();
            MechanicalFlightStraightPathPatch.ClearMotion(source);
            source.Position = landingCell;
            source.Notify_Teleported(
                endCurrentJob: false,
                resetTweenedPos: true);
            return true;
        }
    }
}
