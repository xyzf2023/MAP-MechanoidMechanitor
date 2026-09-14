using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    [StaticConstructorOnStartup]
    public static class MechanicalFlightUtility
    {
        internal enum VanillaFlightState
        {
            Unknown = -1,
            Grounded = 0,
            Flying = 1,
            TakingOff = 2,
            Landing = 3
        }

        private static readonly Texture2D FlightIcon =
            ContentFinder<Texture2D>.Get("UI/Commands/MechanicalFlight", false)
            ?? TexCommand.Install;

        private static readonly FieldInfo? VanillaFlightStateField =
            AccessTools.Field(typeof(Pawn_FlightTracker), "flightState");

        internal static VanillaFlightState GetVanillaFlightState(Pawn? pawn)
        {
            Pawn_FlightTracker? tracker = pawn?.flight;
            if (tracker == null || VanillaFlightStateField == null)
            {
                return VanillaFlightState.Unknown;
            }

            object? value = VanillaFlightStateField.GetValue(tracker);
            if (value == null)
            {
                return VanillaFlightState.Unknown;
            }
            return (VanillaFlightState)Convert.ToInt32(value);
        }

        public static bool IsAirborne(Pawn? pawn)
        {
            return GameComponent_MechanicalFlightRegistry.TryGetRecord(pawn, out var record)
                && record != null && record.IsRuntimeActive;
        }

        public static bool IsActivelyFlying(Pawn? pawn)
        {
            return GameComponent_MechanicalFlightRegistry.TryGetRecord(pawn, out var record)
                && record?.ConsumesFlightEnergy == true;
        }

        public static bool UsesAerialMovement(Pawn? pawn)
        {
            return GameComponent_MechanicalFlightRegistry.TryGetRecord(pawn, out var record)
                && record?.UsesAerialMovement == true;
        }

        public static bool HasHoverVisual(Pawn? pawn)
        {
            return TryGetHoverVisualState(pawn, out _, out _);
        }

        /// <summary>
        /// 统一悬浮视觉查询：一次完成 Pawn 非空、已 Spawn、Map 存在、flight 存在、
        /// 授权记录存在、Profile 存在以及当前应显示悬浮视觉的全部检查。
        /// 调用方拿到 record 后不得再次通过 HasHoverVisual(pawn) 重复查询注册表。
        /// </summary>
        internal static bool TryGetHoverVisualState(
            Pawn? pawn,
            out MechanicalFlightAuthorizationRecord? record,
            out MechanicalFlightProfileDef? profile)
        {
            record = null;
            profile = null;
            if (pawn?.Spawned != true || pawn.Map == null || pawn.flight == null)
            {
                return false;
            }

            if (!GameComponent_MechanicalFlightRegistry.TryGetRecord(pawn, out record)
                || record == null || record.Profile == null)
            {
                return false;
            }

            if (!HasHoverVisual(pawn, record))
            {
                record = null;
                return false;
            }

            profile = record.Profile;
            return true;
        }

        internal static bool HasHoverVisual(
            Pawn pawn,
            MechanicalFlightAuthorizationRecord record)
        {
            if (pawn.flight == null)
            {
                return false;
            }

            if (record.Purpose == MechanicalFlightPurpose.FusionRelocation)
            {
                // 合体快速转移使用专用垂直绘制偏移，不依赖原版 PositionOffsetFactor。
                return true;
            }

            return record.UsesAerialMovement
                || ((record.Phase == MechanicalFlightPhase.Landing
                        || record.Phase == MechanicalFlightPhase.EmergencyLanding)
                    && pawn.flight.PositionOffsetFactor > 0f);
        }

        public static bool CanEverFlyWithAuthorization(Pawn? pawn)
        {
            // 机械飞行授权是独立资格来源，不继承原版生物飞行的 Mutant、真空或飞行时长限制。
            return pawn != null
                && GameComponent_MechanicalFlightRegistry.IsAuthorized(pawn);
        }

        public static Command_Action MakeCommand(Pawn pawn)
        {
            bool active = IsActivelyFlying(pawn);
            Command_Action command = new()
            {
                defaultLabel = active
                    ? "MAP_MechanicalFlight_LandLabel".Translate()
                    : "MAP_MechanicalFlight_TakeoffLabel".Translate(),
                defaultDesc = active
                    ? "MAP_MechanicalFlight_LandDesc".Translate()
                    : "MAP_MechanicalFlight_TakeoffDesc".Translate(),
                icon = FlightIcon,
                action = () => ToggleFlight(pawn)
            };

            string? reason = GetDisabledReason(pawn);
            if (!reason.NullOrEmpty())
            {
                command.Disable(reason);
            }
            return command;
        }

        internal static bool CanIssueAerialMove(Pawn? pawn, IntVec3 cell)
        {
            if (pawn?.Spawned != true
                || pawn.Map == null
                || pawn.Faction != Faction.OfPlayer
                || !pawn.Drafted
                || !IsActivelyFlying(pawn)
                || !cell.InBounds(pawn.Map))
            {
                return false;
            }

            // 飞行移动跳过地面可达性，但殖民地机械族仍必须遵守原版命令范围。
            // 统一调用原版入口，使量子通讯器、代理子链、数据处理分配及
            // 无监管者节点等现有范围豁免继续由 CommandRangePatches 处理。
            return !pawn.IsColonyMech
                || MechanitorUtility.InMechanitorCommandRange(pawn, cell);
        }

        public static bool TryStartAerialMove(Pawn? pawn, IntVec3 cell)
        {
            // 执行层再次校验，防止菜单生成后状态变化或其它调用入口绕过范围限制。
            if (!CanIssueAerialMove(pawn, cell))
            {
                return false;
            }

            Job job = JobMaker.MakeJob(JobDefOf.Goto, cell);
            job.locomotionUrgency = LocomotionUrgency.Sprint;
            job.expiryInterval = -1;
            job.flying = true;
            return pawn!.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        public static bool TryBeginTakeoff(Pawn? pawn)
        {
            if (!GameComponent_MechanicalFlightRegistry.TryGetRecord(pawn, out var record)
                || record == null || pawn == null
                || !CanBeginTakeoff(pawn, record, requireDrafted: true, out _))
            {
                return false;
            }

            MechanicalFlightProfileDef profile = record.Profile!;
            if (profile.breakThinRoofOnTakeoff)
            {
                MechanicalFlightRoofUtility.BreakThinRoofArea(pawn, profile);
            }

            pawn.flight!.StartFlying();
            if (!pawn.flight.Flying)
            {
                return false;
            }

            record.Phase = MechanicalFlightPhase.TakingOff;
            record.EmergencyLandingTarget = IntVec3.Invalid;
            record.LowEnergyWarningSent = false;
            record.PendingShutdownAfterLanding = false;
            record.GroundLandingBlockedNoticeSent = false;
            record.GroundJobBlockedNoticeSent = false;
            record.TicksUntilNextEnergyDrain =
                Mathf.Max(1, profile.energyDrainIntervalTicks);
            if (pawn.CurJob != null)
            {
                pawn.CurJob.flying = true;
            }

            MechanicalFlightEnergyUtility.TryConsumeMaximumEnergyFraction(
                pawn, profile.energyDrainFraction);
            if (MechFusionEnergyUtility.TryHandleDepletedFlightEnergy(pawn))
            {
                return false;
            }

            GameComponent_MechanicalFlightRegistry.NotifyRuntimeStateChanged(record);
            MechanicalFlightPresentationUtility.NotifyFlightStarted(pawn, record);
            return true;
        }

        /// <summary>
        /// 合体快速转移专用升空：复用统一起飞资格与一次性起飞能源规则，
        /// 但使用 FusionAscent/FusionDescent 相位与专用垂直绘制偏移，
        /// 不启动原版飞行状态机，也不参与每 60 Tick 的持续飞行耗能。
        /// </summary>
        internal static bool TryBeginFusionTakeoff(Pawn? pawn)
        {
            if (!GameComponent_MechanicalFlightRegistry.TryGetRecord(pawn, out var record)
                || record == null || pawn == null
                || record.Purpose != MechanicalFlightPurpose.Normal
                || record.IsRuntimeActive
                || !CanBeginTakeoff(pawn, record, requireDrafted: false, out _))
            {
                return false;
            }

            MechanicalFlightProfileDef profile = record.Profile!;
            if (profile.breakThinRoofOnTakeoff)
            {
                MechanicalFlightRoofUtility.BreakThinRoofArea(pawn, profile);
            }

            record.Purpose = MechanicalFlightPurpose.FusionRelocation;
            record.Phase = MechanicalFlightPhase.FusionAscent;
            record.EmergencyLandingTarget = IntVec3.Invalid;
            record.LowEnergyWarningSent = false;
            record.PendingShutdownAfterLanding = false;
            record.GroundLandingBlockedNoticeSent = false;
            record.GroundJobBlockedNoticeSent = false;
            record.TicksUntilNextEnergyDrain =
                Mathf.Max(1, profile.energyDrainIntervalTicks);

            MechanicalFlightEnergyUtility.TryConsumeMaximumEnergyFraction(
                pawn, profile.energyDrainFraction);

            GameComponent_MechanicalFlightRegistry.NotifyRuntimeStateChanged(record);
            MechanicalFlightPresentationUtility.NotifyFlightStarted(pawn, record);
            MechanicalFlightVisualSmoothing.BeginFusionAscent(pawn);
            return true;
        }

        /// <summary>
        /// 合体快速转移的降落阶段：真实 Pawn 已经在规划落点，
        /// 只负责切换相位并启动垂直降落表现。
        /// </summary>
        internal static bool TryBeginFusionDescent(Pawn? pawn)
        {
            if (!GameComponent_MechanicalFlightRegistry.TryGetRecord(pawn, out var record)
                || record == null || pawn == null
                || record.Purpose != MechanicalFlightPurpose.FusionRelocation
                || record.Phase != MechanicalFlightPhase.FusionAscent)
            {
                return false;
            }

            if (record.Profile?.breakThinRoofOnLanding == true)
            {
                MechanicalFlightRoofUtility.BreakThinRoofArea(
                    pawn,
                    record.Profile);
            }

            record.Phase = MechanicalFlightPhase.FusionDescent;
            GameComponent_MechanicalFlightRegistry.NotifyRuntimeStateChanged(record);
            MechanicalFlightVisualSmoothing.BeginFusionDescent(pawn);
            return true;
        }

        /// <summary>
        /// 合体快速转移落地完成：清空全部特殊飞行运行态，不留任何半空状态。
        /// </summary>
        internal static void CompleteFusionLanding(Pawn? pawn)
        {
            if (pawn == null
                || !GameComponent_MechanicalFlightRegistry.TryGetRecord(
                    pawn,
                    out MechanicalFlightAuthorizationRecord? record)
                || record == null
                || record.Purpose != MechanicalFlightPurpose.FusionRelocation)
            {
                return;
            }

            pawn.pather?.StopDead();
            MechanicalFlightStraightPathPatch.ClearMotion(pawn);
            if (pawn.CurJob != null)
            {
                pawn.CurJob.flying = false;
            }

            MechanicalFlightProfileDef? profile = record.Profile;
            if (profile != null && !pawn.Dead && !pawn.Downed
                && pawn.Spawned && pawn.Map != null)
            {
                MechanicalFlightCruisePresentation.PlayLandingEffects(
                    pawn,
                    profile);
            }

            FinalizeRuntimeState(pawn, record);
        }

        /// <summary>
        /// 安全取消合体快速转移。真实地图位置在升空阶段保持不变，
        /// 在重定位后一定位于已验证的合法落点，因此直接回到地面态即可。
        /// </summary>
        internal static void CancelFusionRelocation(Pawn? pawn)
        {
            if (pawn == null
                || !GameComponent_MechanicalFlightRegistry.TryGetRecord(
                    pawn,
                    out MechanicalFlightAuthorizationRecord? record)
                || record == null
                || record.Purpose != MechanicalFlightPurpose.FusionRelocation)
            {
                return;
            }

            pawn.pather?.StopDead();
            MechanicalFlightStraightPathPatch.ClearMotion(pawn);
            if (pawn.CurJob != null)
            {
                pawn.CurJob.flying = false;
            }

            FinalizeRuntimeState(pawn, record);
        }

        private static void TickFusionRelocation(
            Pawn pawn,
            MechanicalFlightAuthorizationRecord record)
        {
            if (record.Phase == MechanicalFlightPhase.Grounded)
            {
                CancelFusionRelocation(pawn);
                return;
            }

            if (pawn.Downed)
            {
                // 保持统一倒地安全处理；未进入坠毁时至少回到合法地面态。
                if (!MechanicalFlightEmergencyUtility.TryCrashFromDowned(pawn))
                {
                    CancelFusionRelocation(pawn);
                }
                return;
            }

            MechanicalFlightPresentationUtility.Tick(pawn, record);
        }

        /// <summary>
        /// 合体快速转移的当前垂直绘制因子（0 = 贴地，1 = 最大高度）。
        /// 只在 FusionRelocation 运行期间有非零值。
        /// </summary>
        internal static float GetFusionRelocationVisualFactor(Pawn? pawn)
        {
            if (!GameComponent_MechanicalFlightRegistry.TryGetRecord(
                    pawn,
                    out MechanicalFlightAuthorizationRecord? record)
                || record == null
                || record.Purpose != MechanicalFlightPurpose.FusionRelocation)
            {
                return 0f;
            }

            return MechanicalFlightVisualSmoothing.FusionVisualFactor(
                pawn!,
                record);
        }

        /// <summary>
        /// 合体快速转移当前是否仍在活动（用于 Job 的异常收尾判断）。
        /// </summary>
        internal static bool IsFusionRelocating(Pawn? pawn)
        {
            return GameComponent_MechanicalFlightRegistry.TryGetRecord(
                    pawn,
                    out MechanicalFlightAuthorizationRecord? record)
                && record != null
                && record.Purpose == MechanicalFlightPurpose.FusionRelocation;
        }

        public static bool TryBeginLanding(Pawn? pawn, bool showMessage = false)
        {
            if (!GameComponent_MechanicalFlightRegistry.TryGetRecord(pawn, out var record)
                || record == null || pawn?.Map == null || !record.ConsumesFlightEnergy
                || record.Purpose != MechanicalFlightPurpose.Normal)
            {
                return false;
            }

            MechanicalFlightProfileDef? profile = record.Profile;
            if (profile == null)
            {
                if (showMessage)
                {
                    Messages.Message("MAP_MechanicalFlight_ThickRoofLanding".Translate(), pawn,
                        MessageTypeDefOf.RejectInput, false);
                }
                return false;
            }
            if (!IsBaseLandingCellValid(pawn.Position, pawn, pawn.Map))
            {
                if (showMessage)
                {
                    Messages.Message(
                        MechanicalFlightRoofUtility.HasThickRoof(pawn.Position, pawn.Map)
                            ? "MAP_MechanicalFlight_ThickRoofLanding".Translate()
                            : "MAP_MechanicalFlight_InvalidLanding".Translate(),
                        pawn, MessageTypeDefOf.RejectInput, false);
                }
                return false;
            }

            if (profile.breakThinRoofOnLanding)
            {
                MechanicalFlightRoofUtility.BreakThinRoofArea(pawn, profile);
            }
            bool interruptMovingFlightJob = pawn.CurJob?.flying == true
                && pawn.pather?.MovingNow == true;
            pawn.pather?.StopDead();
            MechanicalFlightStraightPathPatch.ClearMotion(pawn);
            record.Phase = MechanicalFlightPhase.Landing;
            record.TicksUntilNextEnergyDrain = 0;
            record.PendingShutdownAfterLanding = false;
            record.GroundLandingBlockedNoticeSent = false;
            record.GroundJobBlockedNoticeSent = false;
            if (pawn.CurJob != null)
            {
                pawn.CurJob.flying = false;
            }
            pawn.flight?.ForceLand();
            GameComponent_MechanicalFlightRegistry.NotifyRuntimeStateChanged(record);
            if (interruptMovingFlightJob && pawn.CurJob != null)
            {
                // StopDead 不会结束等待 PatherArrival 的 Toil；主动结束原飞行移动任务，
                // 避免降落后任务永远等待一条已经清除的路径。
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
            }
            return true;
        }

        /// <summary>
        /// 普通降落与紧急迫降共享的基础物理落点合法性判断。
        /// 只描述“格子本身能否承载机械体”；紧急迫降专属的预留、监狱格与
        /// 交互格限制继续由 MechanicalFlightEmergencyUtility 追加。
        /// </summary>
        internal static bool IsBaseLandingCellValid(
            IntVec3 cell,
            Pawn pawn,
            Map map)
        {
            if (!cell.InBounds(map)
                || !cell.Standable(map)
                || !cell.WalkableBy(map, pawn)
                || MechanicalFlightRoofUtility.HasThickRoof(cell, map)
                || cell.GetTerrain(map).dangerous
                || cell.ContainsStaticFire(map)
                || cell.GetFirstBuilding(map) != null)
            {
                return false;
            }

            List<Thing> things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i] is Pawn other && !ReferenceEquals(other, pawn))
                {
                    return false;
                }
            }

            return true;
        }

        internal static void Tick(MechanicalFlightAuthorizationRecord record)
        {
            Pawn? pawn = record.Pawn;
            MechanicalFlightProfileDef? profile = record.Profile;
            if (pawn == null || profile == null || pawn.Dead || !pawn.Spawned || pawn.Map == null)
            {
                ClearRuntimeState(record, forceLand: false);
                return;
            }

            if (record.Purpose == MechanicalFlightPurpose.FusionRelocation)
            {
                TickFusionRelocation(pawn, record);
                return;
            }

            if (record.IsEmergencySequence)
            {
                MechanicalFlightEmergencyUtility.Tick(record);
                return;
            }

            if (pawn.Downed)
            {
                MechanicalFlightEmergencyUtility.TryCrashFromDowned(pawn);
                return;
            }

            if (record.Phase == MechanicalFlightPhase.Landing)
            {
                if (pawn.flight?.Flying != true)
                {
                    CompleteNormalLanding(pawn, record);
                }
                return;
            }

            if (pawn.flight?.Flying != true)
            {
                ClearRuntimeState(record, forceLand: false);
                return;
            }

            if (record.Phase == MechanicalFlightPhase.TakingOff
                && pawn.flight.PositionOffsetFactor >= 0.999f)
            {
                record.Phase = MechanicalFlightPhase.Hovering;
            }

            if (!pawn.Drafted)
            {
                pawn.pather?.StopDead();
                if (!TryBeginLanding(pawn))
                {
                    // 当前位置不能安全降落：保留飞行与记录，恢复征召让玩家继续操控。
                    NotifyGroundLandingBlocked(pawn, record);
                    if (pawn.drafter != null && !pawn.Drafted)
                    {
                        pawn.drafter.Drafted = true;
                        return;
                    }
                    // 无征召控制器的机械体（非玩家/异常授权）沿用清理路径，避免无限悬停。
                    // 不强制降落，交由原版飞行状态机自行处理。
                    ClearRuntimeState(record, forceLand: false);
                }
                return;
            }

            MechanicalFlightPresentationUtility.Tick(pawn, record);

            // 没有机械能需求的 Pawn 使用无能源型飞行：不扣除机械能，
            // 也不参与低能量警告、自动降落和耗尽迫降。
            if (!MechanicalFlightEnergyUtility.TryGetEnergyFraction(pawn, out float energy))
            {
                return;
            }

            record.TicksUntilNextEnergyDrain--;
            if (record.TicksUntilNextEnergyDrain <= 0)
            {
                MechanicalFlightEnergyUtility.TryConsumeMaximumEnergyFraction(
                    pawn, profile.energyDrainFraction);
                record.TicksUntilNextEnergyDrain =
                    Mathf.Max(1, profile.energyDrainIntervalTicks);
                if (!MechanicalFlightEnergyUtility.TryGetEnergyFraction(pawn, out energy))
                {
                    return;
                }
            }

            MechanicalFlightEmergencyUtility.TrySendLowEnergyWarning(pawn, record, energy);
            if (energy <= 0f
                && MechFusionEnergyUtility.TryHandleDepletedFlightEnergy(pawn))
            {
                return;
            }

            if (energy <= 0f)
            {
                MechanicalFlightEmergencyUtility.TryBeginEmergencySequence(pawn);
            }
            else if (energy < profile.automaticLandingEnergy)
            {
                TryBeginLanding(pawn);
            }
        }

        /// <summary>
        /// 四条飞行结束路径共用的最小收尾：清空运行态、通知表现层、通知注册表。
        /// ForceLand、StopDead、pendingShutdown、坠毁保护等差异仍由各调用路径自行处理。
        /// </summary>
        internal static void FinalizeRuntimeState(
            Pawn? pawn,
            MechanicalFlightAuthorizationRecord record)
        {
            record.ResetRuntimeState();
            MechanicalFlightPresentationUtility.NotifyFlightEnded(pawn);
            GameComponent_MechanicalFlightRegistry.NotifyRuntimeStateChanged(record);
        }

        internal static void ClearRuntimeState(
            MechanicalFlightAuthorizationRecord record,
            bool forceLand)
        {
            Pawn? pawn = record.Pawn;
            if (forceLand && pawn?.flight?.Flying == true)
            {
                pawn.pather?.StopDead();
                MechanicalFlightStraightPathPatch.ClearMotion(pawn);
                pawn.flight.ForceLand();
            }
            if (pawn?.CurJob != null)
            {
                pawn.CurJob.flying = false;
            }
            if (pawn?.CurJobDef == MAPMechanitor_JobDefOf.MAP_MechanicalFlightEmergencyLanding)
            {
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
            }
            FinalizeRuntimeState(pawn, record);
        }

        internal static void CompleteNormalLanding(
            Pawn pawn,
            MechanicalFlightAuthorizationRecord record)
        {
            bool pendingShutdown = record.PendingShutdownAfterLanding;
            MechanicalFlightStraightPathPatch.ClearMotion(pawn);
            if (pawn.CurJob != null)
            {
                pawn.CurJob.flying = false;
            }
            FinalizeRuntimeState(pawn, record);
            if (pendingShutdown && !pawn.Dead && pawn.Spawned)
            {
                pawn.needs?.energy?.NeedInterval();
            }
        }

        internal static void CleanupUnavailableRecord(
            MechanicalFlightAuthorizationRecord record)
        {
            MechanicalFlightPresentationUtility.NotifyFlightEnded(record.Pawn);
            record.ResetRuntimeState();
        }

        internal static void NotifyGroundLandingBlocked(
            Pawn pawn,
            MechanicalFlightAuthorizationRecord record)
        {
            if (record.GroundLandingBlockedNoticeSent)
            {
                return;
            }
            record.GroundLandingBlockedNoticeSent = true;
            Messages.Message("MAP_MechanicalFlight_InvalidLanding".Translate(), pawn,
                MessageTypeDefOf.RejectInput, false);
        }

        internal static void NotifyGroundJobBlocked(Pawn pawn, bool showMessage)
        {
            if (!showMessage)
            {
                return;
            }
            if (!GameComponent_MechanicalFlightRegistry.TryGetRecord(pawn, out var record)
                || record == null || record.GroundJobBlockedNoticeSent)
            {
                return;
            }
            record.GroundJobBlockedNoticeSent = true;
            Messages.Message("MAP_MechanicalFlight_GroundJobBlocked".Translate(), pawn,
                MessageTypeDefOf.RejectInput, false);
        }

        internal static void ReconcileAfterLoad(
            IReadOnlyList<MechanicalFlightAuthorizationRecord> activeRecords)
        {
            for (int i = activeRecords.Count - 1; i >= 0; i--)
            {
                MechanicalFlightAuthorizationRecord record = activeRecords[i];
                Pawn? pawn = record.Pawn;
                if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Map == null
                    || pawn.flight == null)
                {
                    ClearRuntimeState(record, forceLand: false);
                    continue;
                }

                if (record.Purpose == MechanicalFlightPurpose.FusionRelocation)
                {
                    // 合体快速转移的升降纯视觉进度不写入存档：
                    // 真实地图位置始终是合法地面格，读档后直接清除运行态，
                    // 由合体接近 Job 重新规划，绝不制造半空悬挂或丢 Pawn。
                    if (pawn.CurJob != null)
                    {
                        pawn.CurJob.flying = false;
                    }
                    pawn.pather?.StopDead();
                    MechanicalFlightStraightPathPatch.ClearMotion(pawn);
                    FinalizeRuntimeState(pawn, record);
                    continue;
                }

                if (record.IsEmergencySequence)
                {
                    MechanicalFlightEmergencyUtility.ReconcileAfterLoad(record);
                    continue;
                }

                if (record.ConsumesFlightEnergy)
                {
                    VanillaFlightState state = GetVanillaFlightState(pawn);
                    if (state == VanillaFlightState.Grounded)
                    {
                        ClearRuntimeState(record, forceLand: false);
                        continue;
                    }
                    if (state == VanillaFlightState.Landing)
                    {
                        bool interruptMovingFlightJob = pawn.CurJob?.flying == true
                            && pawn.pather?.MovingNow == true;
                        pawn.pather?.StopDead();
                        MechanicalFlightStraightPathPatch.ClearMotion(pawn);
                        record.Phase = MechanicalFlightPhase.Landing;
                        record.TicksUntilNextEnergyDrain = 0;
                        if (pawn.CurJob != null)
                        {
                            pawn.CurJob.flying = false;
                        }
                        GameComponent_MechanicalFlightRegistry.NotifyRuntimeStateChanged(record);
                        if (interruptMovingFlightJob && pawn.CurJob != null)
                        {
                            pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                        }
                        continue;
                    }
                    if (!pawn.flight.Flying)
                    {
                        pawn.flight.StartFlying();
                        if (!pawn.flight.Flying)
                        {
                            ClearRuntimeState(record, forceLand: false);
                        }
                    }
                    continue;
                }

                if (record.Phase == MechanicalFlightPhase.Landing)
                {
                    VanillaFlightState state = GetVanillaFlightState(pawn);
                    if (state == VanillaFlightState.Landing)
                    {
                        pawn.pather?.StopDead();
                        MechanicalFlightStraightPathPatch.ClearMotion(pawn);
                        continue;
                    }
                    if (state == VanillaFlightState.TakingOff
                        || state == VanillaFlightState.Flying
                        || (state == VanillaFlightState.Unknown && pawn.flight.Flying))
                    {
                        pawn.flight.ForceLand();
                        continue;
                    }
                    CompleteNormalLanding(pawn, record);
                }
            }
        }

        private static void ToggleFlight(Pawn pawn)
        {
            if (IsActivelyFlying(pawn))
            {
                TryBeginLanding(pawn, showMessage: true);
            }
            else
            {
                string? reason = GetDisabledReason(pawn);
                if (reason.NullOrEmpty())
                {
                    TryBeginTakeoff(pawn);
                }
                else
                {
                    Messages.Message(reason, pawn, MessageTypeDefOf.RejectInput, false);
                }
            }
        }

        /// <summary>
        /// 纯起飞可行性判断，无任何副作用。普通玩家起飞与合体快速转移规划
        /// 共用同一套屋顶、能源、冷却与征召规则，禁止在别处复制第二套。
        /// requireDrafted 为 false 时允许未征召机械族进入合体快速转移。
        /// </summary>
        internal static bool CanBeginTakeoff(
            Pawn? pawn,
            bool requireDrafted,
            out string? disabledReason)
        {
            GameComponent_MechanicalFlightRegistry.TryGetRecord(
                pawn,
                out MechanicalFlightAuthorizationRecord? record);
            return CanBeginTakeoff(
                pawn,
                record,
                requireDrafted,
                out disabledReason);
        }

        internal static bool CanBeginTakeoff(
            Pawn? pawn,
            MechanicalFlightAuthorizationRecord? record,
            bool requireDrafted,
            out string? disabledReason)
        {
            disabledReason = GetDisabledReason(pawn, record, requireDrafted);
            return disabledReason.NullOrEmpty();
        }

        private static string? GetDisabledReason(Pawn pawn)
        {
            GameComponent_MechanicalFlightRegistry.TryGetRecord(pawn, out var record);
            return GetDisabledReason(pawn, record, requireDrafted: true);
        }

        private static string? GetDisabledReason(
            Pawn? pawn,
            MechanicalFlightAuthorizationRecord? record,
            bool requireDrafted)
        {
            if (pawn == null || record?.Profile == null || !pawn.Spawned
                || pawn.Map == null || pawn.Dead || pawn.Downed)
            {
                return "MAP_MechanicalFlight_Unavailable".Translate();
            }
            if (record.IsEmergencySequence)
            {
                return "MAP_MechanicalFlight_EmergencyLandingBlocked".Translate();
            }
            if (record.Purpose == MechanicalFlightPurpose.FusionRelocation)
            {
                // 合体快速转移期间不接受普通起飞/降落操控。
                return "MAP_MechanicalFlight_Unavailable".Translate();
            }
            if (requireDrafted && !pawn.Drafted)
            {
                return "MAP_MechanicalFlight_Unavailable".Translate();
            }
            if (record.Phase == MechanicalFlightPhase.Landing)
            {
                return "MAP_MechanicalFlight_Landing".Translate();
            }
            if (record.ConsumesFlightEnergy)
            {
                if (MechanicalFlightRoofUtility.HasThickRoof(pawn.Position, pawn.Map))
                {
                    return "MAP_MechanicalFlight_ThickRoofLanding".Translate();
                }
                return pawn.Position.WalkableBy(pawn.Map, pawn)
                    ? null
                    : "MAP_MechanicalFlight_InvalidLanding".Translate();
            }
            if (MechanicalFlightEnergyUtility.TryGetEnergyFraction(pawn, out float energy)
                && energy < record.Profile.minimumTakeoffEnergy)
            {
                return "MAP_MechanicalFlight_LowEnergy".Translate(
                    record.Profile.minimumTakeoffEnergy.ToStringPercent());
            }
            if (MechanicalFlightRoofUtility.HasThickRoof(pawn.Position, pawn.Map))
            {
                return "MAP_MechanicalFlight_ThickRoofTakeoff".Translate();
            }
            if (pawn.flight == null || !pawn.flight.CanFlyNow)
            {
                return "MAP_MechanicalFlight_Cooling".Translate();
            }
            return null;
        }
    }
}
