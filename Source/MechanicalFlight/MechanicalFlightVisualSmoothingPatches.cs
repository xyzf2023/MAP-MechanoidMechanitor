using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 机械飞行专用的纯表现插值层。
    /// 不修改 Pawn.Position、Pawn_PathFollower、飞行速度、碰撞、到达判定或原版飞行状态机。
    /// </summary>
    internal static class MechanicalFlightVisualSmoothing
    {
        private const float TakeoffDurationTicks = 50f;
        private const float LandingDurationTicks = 25f;
        private const float MaximumGroundSeparationPadding = 0.05f;

        // 合体快速转移沿地图 +Z 方向退场/入场。
        // 4 格缓冲确保 Pawn 贴图完整越过地图北边界；80 格/秒避免大地图上演出过慢。
        private const float FusionMapBoundaryMargin = 4f;
        private const float FusionHiddenDrawDistance = 64f;
        private const float FusionBoundaryFlightCellsPerSecond = 80f;
        private const float FusionPhaseTimeoutPaddingTicks = 120f;

        private sealed class HeightState
        {
            internal float Factor;
            internal float StartFactor;
            internal float Progress;
            internal MechanicalFlightPhase Phase;
            internal int LastFrame = -1;
            internal int ExtraHoverHeightFrame = -1;
            internal float CachedExtraHoverHeight;
            internal MechanicalFlightProfileDef? CachedExtraHoverHeightProfile;
        }

        private sealed class GroundState
        {
            internal Vector3 VisualPosition;
            internal Vector3 Destination;
            internal int LastFrame = -1;
        }

        private sealed class FusionState
        {
            internal float StartFactor;
            internal float TargetFactor;
            internal float Progress;
            internal float Factor;
            internal float DurationTicks;
            internal bool Ascent;
            internal bool Hidden;
            internal int LastFrame = -1;
        }

        private static readonly Dictionary<int, HeightState> HeightStates = new();
        private static readonly Dictionary<int, GroundState> GroundStates = new();
        private static readonly Dictionary<int, FusionState> FusionStates = new();

        internal static void BeginTakeoff(Pawn pawn)
        {
            HeightStates[pawn.thingIDNumber] = new HeightState
            {
                Factor = 0f,
                StartFactor = 0f,
                Progress = 0f,
                Phase = MechanicalFlightPhase.TakingOff,
                LastFrame = RealTime.frameCount
            };
            GroundStates.Remove(pawn.thingIDNumber);
        }

        internal static void BeginLanding(
            Pawn pawn,
            MechanicalFlightAuthorizationRecord record)
        {
            float current = GetHeightFactor(pawn, record);
            HeightStates[pawn.thingIDNumber] = new HeightState
            {
                Factor = current,
                StartFactor = current,
                Progress = 0f,
                Phase = record.Phase,
                LastFrame = RealTime.frameCount
            };
        }

        internal static void BeginDirectMove(Pawn pawn)
        {
            if (GroundStates.ContainsKey(pawn.thingIDNumber))
            {
                return;
            }

            Vector3 root = pawn.Position.ToVector3Shifted();
            GroundStates[pawn.thingIDNumber] = new GroundState
            {
                VisualPosition = root,
                Destination = root,
                LastFrame = RealTime.frameCount
            };
        }

        /// <summary>
        /// 开始合体快速转移的地图北侧退场表现。
        /// 真实地图位置不变，绘制位置沿 +Z 直线移动到 Map.Size.z + 4。
        /// </summary>
        internal static void BeginFusionAscent(Pawn pawn)
        {
            float start = FusionStates.TryGetValue(
                pawn.thingIDNumber,
                out FusionState? existing)
                ? Mathf.Clamp01(existing!.Factor)
                : 0f;
            FusionStates[pawn.thingIDNumber] = new FusionState
            {
                StartFactor = start,
                TargetFactor = 1f,
                Progress = 0f,
                Factor = start,
                DurationTicks = CalculateFusionDurationTicks(pawn, start, 1f),
                Ascent = true,
                Hidden = false,
                LastFrame = RealTime.frameCount
            };
        }

        /// <summary>
        /// 开始合体快速转移的地图北侧入场表现。
        /// 真实 Pawn 已在合法落点；绘制位置从该 x 列的地图北边界外沿 -Z 直线下降。
        /// </summary>
        internal static void BeginFusionDescent(Pawn pawn)
        {
            FusionStates[pawn.thingIDNumber] = new FusionState
            {
                StartFactor = 1f,
                TargetFactor = 0f,
                Progress = 0f,
                Factor = 1f,
                DurationTicks = CalculateFusionDurationTicks(pawn, 1f, 0f),
                Ascent = false,
                Hidden = false,
                LastFrame = RealTime.frameCount
            };
        }

        /// <summary>
        /// 合体快速转移专用垂直绘制因子（0 = 贴地，1 = 地图北边界外）。
        /// </summary>
        internal static float FusionVisualFactor(
            Pawn pawn,
            MechanicalFlightAuthorizationRecord record)
        {
            if (record.Purpose != MechanicalFlightPurpose.FusionRelocation)
            {
                return 0f;
            }

            return GetFusionFactor(pawn, record.Phase);
        }

        /// <summary>
        /// 返回当前合体转移应叠加到真实地面位置上的 Z 偏移。
        /// 正常升降以地图北边界为目标；隐藏换位阶段把绘制点暂放到更远的地图外，
        /// 使 Pawn、选框与阴影补偿上下文都不会留在原地或落点上。
        /// </summary>
        internal static float FusionVisualZOffset(
            Pawn pawn,
            MechanicalFlightAuthorizationRecord record)
        {
            if (record.Purpose != MechanicalFlightPurpose.FusionRelocation
                || pawn.Map == null)
            {
                return 0f;
            }

            float groundZ = pawn.Position.ToVector3Shifted().z;
            if (IsFusionHidden(pawn))
            {
                return pawn.Map.Size.z + FusionHiddenDrawDistance - groundZ;
            }

            return FusionBoundaryOffset(pawn)
                * FusionVisualFactor(pawn, record);
        }

        internal static void SetFusionHidden(Pawn pawn, bool hidden)
        {
            if (FusionStates.TryGetValue(
                    pawn.thingIDNumber,
                    out FusionState? state))
            {
                state.Hidden = hidden;
            }
        }

        internal static bool IsFusionHidden(Pawn? pawn)
        {
            return pawn != null
                && FusionStates.TryGetValue(
                    pawn.thingIDNumber,
                    out FusionState? state)
                && state.Hidden;
        }

        /// <summary>
        /// 当前升降阶段按实际地图距离计算的超时上限。
        /// 演出本身按固定 80 格/秒推进，额外预留 120 Tick 处理低帧率和调度抖动。
        /// </summary>
        internal static int GetFusionPhaseTimeoutTicks(Pawn pawn)
        {
            if (FusionStates.TryGetValue(
                    pawn.thingIDNumber,
                    out FusionState? state))
            {
                return Mathf.Max(
                    1,
                    Mathf.CeilToInt(
                        state.DurationTicks + FusionPhaseTimeoutPaddingTicks));
            }

            return Mathf.CeilToInt(FusionPhaseTimeoutPaddingTicks);
        }

        private static float GetFusionFactor(
            Pawn pawn,
            MechanicalFlightPhase phase)
        {
            if (!FusionStates.TryGetValue(
                    pawn.thingIDNumber,
                    out FusionState? state))
            {
                // 缺少运行态时保持旧的安全语义：升空视为已退场，降落视为已落地。
                return phase == MechanicalFlightPhase.FusionAscent ? 1f : 0f;
            }

            int frame = RealTime.frameCount;
            if (state.LastFrame == frame)
            {
                return state.Factor;
            }

            state.LastFrame = frame;
            float deltaTicks = GameDeltaTicks();
            state.Progress = Mathf.Clamp01(
                state.Progress
                + deltaTicks / Mathf.Max(1f, state.DurationTicks));
            float eased = state.Ascent
                ? state.Progress * state.Progress
                : 1f - (1f - state.Progress) * (1f - state.Progress);
            state.Factor = Mathf.Lerp(
                state.StartFactor,
                state.TargetFactor,
                eased);
            return state.Factor;
        }

        private static float FusionBoundaryOffset(Pawn pawn)
        {
            if (pawn.Map == null)
            {
                return 0f;
            }

            float groundZ = pawn.Position.ToVector3Shifted().z;
            return Mathf.Max(
                0f,
                pawn.Map.Size.z + FusionMapBoundaryMargin - groundZ);
        }

        private static float CalculateFusionDurationTicks(
            Pawn pawn,
            float startFactor,
            float targetFactor)
        {
            float distance = FusionBoundaryOffset(pawn)
                * Mathf.Abs(targetFactor - startFactor);
            return Mathf.Max(
                1f,
                distance / FusionBoundaryFlightCellsPerSecond * 60f);
        }

        internal static void Cleanup(Pawn? pawn)
        {
            if (pawn == null)
            {
                return;
            }

            HeightStates.Remove(pawn.thingIDNumber);
            GroundStates.Remove(pawn.thingIDNumber);
            FusionStates.Remove(pawn.thingIDNumber);
        }

        internal static void ClearAllRuntimeState()
        {
            HeightStates.Clear();
            GroundStates.Clear();
            FusionStates.Clear();
        }

        internal static float GetHeightFactor(
            Pawn pawn,
            MechanicalFlightAuthorizationRecord record)
        {
            float logicalFactor = Mathf.Clamp01(
                pawn.flight?.PositionOffsetFactor ?? 0f);

            if (record.Phase == MechanicalFlightPhase.Crashing)
            {
                return logicalFactor;
            }

            if (record.Purpose == MechanicalFlightPurpose.FusionRelocation)
            {
                // 合体快速转移使用独立的 FusionVisualFactor，不参与
                // 普通起降与悬浮的高度状态机。
                return 0f;
            }

            if (!HeightStates.TryGetValue(
                    pawn.thingIDNumber, out HeightState? state))
            {
                state = CreateInitialHeightState(record.Phase, logicalFactor);
                HeightStates[pawn.thingIDNumber] = state;
            }

            if (state.Phase != record.Phase)
            {
                TransitionHeightState(state, record.Phase, logicalFactor);
            }

            int frame = RealTime.frameCount;
            if (state.LastFrame == frame)
            {
                return state.Factor;
            }

            state.LastFrame = frame;
            float deltaTicks = GameDeltaTicks();

            switch (state.Phase)
            {
                case MechanicalFlightPhase.TakingOff:
                    state.Progress = Mathf.Clamp01(
                        state.Progress + deltaTicks / TakeoffDurationTicks);
                    state.Factor = Mathf.Lerp(
                        state.StartFactor, 1f, Smooth01(state.Progress));
                    break;

                case MechanicalFlightPhase.Hovering:
                case MechanicalFlightPhase.EmergencyApproach:
                    state.Factor = 1f;
                    state.StartFactor = 1f;
                    state.Progress = 1f;
                    break;

                case MechanicalFlightPhase.Landing:
                case MechanicalFlightPhase.EmergencyLanding:
                    state.Progress = Mathf.Clamp01(
                        state.Progress + deltaTicks / LandingDurationTicks);
                    state.Factor = Mathf.Lerp(
                        state.StartFactor, 0f, Smooth01(state.Progress));
                    break;

                case MechanicalFlightPhase.Grounded:
                    state.Factor = 0f;
                    state.StartFactor = 0f;
                    state.Progress = 1f;
                    break;

                case MechanicalFlightPhase.Crashing:
                    state.Factor = logicalFactor;
                    break;
            }

            return Mathf.Clamp01(state.Factor);
        }

        internal static bool TryGetGroundCorrection(
            Pawn pawn,
            MechanicalFlightAuthorizationRecord record,
            out Vector3 correction)
        {
            if (record.Phase == MechanicalFlightPhase.Crashing)
            {
                correction = Vector3.zero;
                return false;
            }

            bool direct = MechanicalFlightStraightPathPatch.TryGetDirectPathDrawData(
                pawn, out Vector3 exactGround, out Vector3 destination);
            int key = pawn.thingIDNumber;

            if (!direct && !GroundStates.TryGetValue(key, out _))
            {
                correction = Vector3.zero;
                return false;
            }

            Vector3 logicalGround = direct
                ? exactGround
                : pawn.Position.ToVector3Shifted();

            if (!GroundStates.TryGetValue(key, out GroundState? state))
            {
                state = new GroundState
                {
                    VisualPosition = logicalGround,
                    Destination = direct ? destination : logicalGround,
                    LastFrame = RealTime.frameCount
                };
                GroundStates[key] = state;
            }

            state.Destination = direct ? destination : logicalGround;
            UpdateGroundState(state, logicalGround, record.Profile, direct);

            correction = state.VisualPosition - logicalGround;
            correction.y = 0f;

            if (!direct
                && (state.VisualPosition - logicalGround).sqrMagnitude <= 0.0001f)
            {
                state.VisualPosition = logicalGround;
                GroundStates.Remove(key);
                correction = Vector3.zero;
                return false;
            }

            return true;
        }

        internal static float ExtraHoverHeight(
            Pawn pawn,
            MechanicalFlightProfileDef profile)
        {
            if (!pawn.Spawned || pawn.Map == null)
            {
                return 0f;
            }

            float shiftedZ = pawn.Position.ToVector3Shifted().z
                + profile.hoverExtraVisualHeight;
            if (shiftedZ < 0f || shiftedZ >= pawn.Map.Size.z)
            {
                return 0f;
            }

            // 同一帧、同一 Pawn、同一 Profile 只计算一次；
            // Profile 变化或状态清理（HeightStates 移除）时缓存自然失效。
            if (HeightStates.TryGetValue(
                    pawn.thingIDNumber, out HeightState? state))
            {
                int frame = RealTime.frameCount;
                if (state.ExtraHoverHeightFrame == frame
                    && ReferenceEquals(
                        state.CachedExtraHoverHeightProfile, profile))
                {
                    return state.CachedExtraHoverHeight;
                }

                float cachedHeight = ComputeExtraHoverHeight(pawn, profile);
                state.ExtraHoverHeightFrame = frame;
                state.CachedExtraHoverHeightProfile = profile;
                state.CachedExtraHoverHeight = cachedHeight;
                return cachedHeight;
            }

            return ComputeExtraHoverHeight(pawn, profile);
        }

        private static float ComputeExtraHoverHeight(
            Pawn pawn,
            MechanicalFlightProfileDef profile)
        {
            float period = Mathf.Max(1f, profile.hoverBobPeriodTicks);
            float phase = (Find.TickManager.TicksGame
                + pawn.thingIDNumber % 100) / period * Mathf.PI * 2f;
            return profile.hoverExtraVisualHeight
                + Mathf.Sin(phase) * profile.hoverBobAmplitude;
        }

        internal static float TotalVisualZOffset(
            Pawn pawn,
            MechanicalFlightProfileDef profile,
            float factor)
        {
            return (MechanicalFlightPresentationUtility.VanillaFlightDrawOffset
                    + ExtraHoverHeight(pawn, profile))
                * Mathf.Clamp01(factor);
        }

        private static HeightState CreateInitialHeightState(
            MechanicalFlightPhase phase,
            float logicalFactor)
        {
            float initial = phase switch
            {
                MechanicalFlightPhase.Grounded => 0f,
                MechanicalFlightPhase.Hovering => 1f,
                MechanicalFlightPhase.EmergencyApproach => 1f,
                _ => logicalFactor
            };

            return new HeightState
            {
                Factor = initial,
                StartFactor = initial,
                Progress = phase == MechanicalFlightPhase.Hovering
                    || phase == MechanicalFlightPhase.EmergencyApproach
                    || phase == MechanicalFlightPhase.Grounded
                    ? 1f
                    : 0f,
                Phase = phase,
                LastFrame = RealTime.frameCount
            };
        }

        private static void TransitionHeightState(
            HeightState state,
            MechanicalFlightPhase phase,
            float logicalFactor)
        {
            switch (phase)
            {
                case MechanicalFlightPhase.TakingOff:
                    state.StartFactor = state.Factor;
                    state.Progress = 0f;
                    break;

                case MechanicalFlightPhase.Hovering:
                case MechanicalFlightPhase.EmergencyApproach:
                    state.Factor = 1f;
                    state.StartFactor = 1f;
                    state.Progress = 1f;
                    break;

                case MechanicalFlightPhase.Landing:
                case MechanicalFlightPhase.EmergencyLanding:
                    state.StartFactor = state.Factor;
                    state.Progress = 0f;
                    break;

                case MechanicalFlightPhase.Grounded:
                    state.Factor = 0f;
                    state.StartFactor = 0f;
                    state.Progress = 1f;
                    break;

                case MechanicalFlightPhase.Crashing:
                    state.Factor = logicalFactor;
                    state.StartFactor = logicalFactor;
                    state.Progress = 0f;
                    break;
            }

            state.Phase = phase;
            state.LastFrame = RealTime.frameCount;
        }

        private static void UpdateGroundState(
            GroundState state,
            Vector3 logicalGround,
            MechanicalFlightProfileDef? profile,
            bool direct)
        {
            int frame = RealTime.frameCount;
            if (state.LastFrame == frame)
            {
                return;
            }

            state.LastFrame = frame;
            if (Find.TickManager.Paused)
            {
                return;
            }

            float tickRate = Mathf.Max(0f, Find.TickManager.TickRateMultiplier);
            float cellsPerSecond = Mathf.Max(
                0.01f, profile?.flightCellsPerSecond ?? 30f);
            float step = cellsPerSecond * RealTime.deltaTime * tickRate;
            state.VisualPosition = Vector3.MoveTowards(
                state.VisualPosition, state.Destination, step);

            if (!direct)
            {
                return;
            }

            float maxSeparation = cellsPerSecond / 60f
                * Mathf.Max(1f, tickRate)
                + MaximumGroundSeparationPadding;
            Vector3 separation = state.VisualPosition - logicalGround;
            separation.y = 0f;
            if (separation.sqrMagnitude > maxSeparation * maxSeparation)
            {
                state.VisualPosition = logicalGround
                    + separation.normalized * maxSeparation;
            }
        }

        private static float GameDeltaTicks()
        {
            if (Find.TickManager.Paused)
            {
                return 0f;
            }

            return RealTime.deltaTime * 60f
                * Mathf.Max(0f, Find.TickManager.TickRateMultiplier);
        }

        private static float Smooth01(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }
    }

    [HarmonyPatch(typeof(MechanicalFlightPresentationUtility),
        nameof(MechanicalFlightPresentationUtility.NotifyFlightStarted))]
    internal static class MechanicalFlightVisualTakeoffPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Pawn pawn)
        {
            MechanicalFlightVisualSmoothing.BeginTakeoff(pawn);
        }
    }

    [HarmonyPatch(typeof(Pawn_FlightTracker), nameof(Pawn_FlightTracker.ForceLand))]
    internal static class MechanicalFlightVisualLandingStartPatch
    {
        private static readonly AccessTools.FieldRef<Pawn_FlightTracker, Pawn> PawnField =
            AccessTools.FieldRefAccess<Pawn_FlightTracker, Pawn>("pawn");

        [HarmonyPrefix]
        public static void Prefix(Pawn_FlightTracker __instance)
        {
            Pawn pawn = PawnField(__instance);
            if (!GameComponent_MechanicalFlightRegistry.TryGetRecord(
                    pawn, out MechanicalFlightAuthorizationRecord? record)
                || record == null
                || (record.Phase != MechanicalFlightPhase.Landing
                    && record.Phase != MechanicalFlightPhase.EmergencyLanding))
            {
                return;
            }

            MechanicalFlightVisualSmoothing.BeginLanding(pawn, record);
        }
    }

    [HarmonyPatch(typeof(MechanicalFlightStraightPathPatch),
        nameof(MechanicalFlightStraightPathPatch.TryStartDirectPath))]
    internal static class MechanicalFlightVisualDirectMoveStartPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Pawn pawn)
        {
            MechanicalFlightVisualSmoothing.BeginDirectMove(pawn);
        }
    }

    [HarmonyPatch(typeof(MechanicalFlightPresentationUtility),
        nameof(MechanicalFlightPresentationUtility.NotifyFlightEnded))]
    internal static class MechanicalFlightVisualEndPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn? pawn)
        {
            MechanicalFlightVisualSmoothing.Cleanup(pawn);
        }
    }

    [HarmonyPatch(typeof(Pawn_DrawTracker), nameof(Pawn_DrawTracker.DrawPos),
        MethodType.Getter)]
    [HarmonyPriority(Priority.Last)]
    internal static class MechanicalFlightVisualDrawPosPatch
    {
        private const float VanillaFlightYOffset = 0.03658537f;
        private static readonly AccessTools.FieldRef<Pawn_DrawTracker, Pawn> PawnField =
            AccessTools.FieldRefAccess<Pawn_DrawTracker, Pawn>("pawn");

        [HarmonyPostfix]
        public static void Postfix(
            Pawn_DrawTracker __instance,
            ref Vector3 __result)
        {
            Pawn pawn = PawnField(__instance);
            if (!MechanicalFlightUtility.TryGetHoverVisualState(
                    pawn,
                    out MechanicalFlightAuthorizationRecord? record,
                    out MechanicalFlightProfileDef? profile)
                || record == null || profile == null
                || record.Phase == MechanicalFlightPhase.Crashing)
            {
                return;
            }

            if (MechanicalFlightVisualSmoothing.TryGetGroundCorrection(
                    pawn, record, out Vector3 groundCorrection))
            {
                __result.x += groundCorrection.x;
                __result.z += groundCorrection.z;
            }

            if (record.Purpose == MechanicalFlightPurpose.FusionRelocation)
            {
                bool hidden = MechanicalFlightVisualSmoothing.IsFusionHidden(pawn);

                // 正常升降时，地面锚点/旧选取/阴影仍保持地面基准；
                // 隐藏换位阶段则故意让这些上下文也走地图外绘制坐标，
                // 避免 Pawn 已“离场”后原地或落点仍残留选框/阴影。
                if (!hidden
                    && (MechanicalFlightGroundAnchorContext.Active
                        || MechanicalFlightGroundAnchorContext.LegacySelectionActive
                        || MechanicalFlightGroundAnchorContext
                            .ShadowCompensationActive))
                {
                    return;
                }

                float fusionFactor =
                    MechanicalFlightVisualSmoothing.FusionVisualFactor(
                        pawn,
                        record);
                float fusionOffset =
                    MechanicalFlightVisualSmoothing.FusionVisualZOffset(
                        pawn,
                        record);
                if (Mathf.Abs(fusionOffset) <= 0.0001f
                    && fusionFactor <= 0.0001f)
                {
                    return;
                }

                __result.z += fusionOffset;
                __result.y += VanillaFlightYOffset * fusionFactor;
                return;
            }

            // 地面锚点和鼠标选取继续沿用各自现有的垂直基准，
            // 只同步新的水平绘制位置。
            if (MechanicalFlightGroundAnchorContext.Active
                || MechanicalFlightGroundAnchorContext.LegacySelectionActive)
            {
                return;
            }

            float logicalFactor = Mathf.Clamp01(
                pawn.flight.PositionOffsetFactor);
            float visualFactor = MechanicalFlightVisualSmoothing.GetHeightFactor(
                pawn, record);
            if (Mathf.Approximately(logicalFactor, visualFactor))
            {
                return;
            }

            float logicalZ = MechanicalFlightVisualSmoothing.TotalVisualZOffset(
                pawn, profile, logicalFactor);
            float visualZ = MechanicalFlightVisualSmoothing.TotalVisualZOffset(
                pawn, profile, visualFactor);
            __result.z += visualZ - logicalZ;
            __result.y += VanillaFlightYOffset
                * (visualFactor - logicalFactor);
        }
    }

    [HarmonyPatch(typeof(MechanicalFlightPresentationUtility),
        nameof(MechanicalFlightPresentationUtility.GroundAnchorDrawPos))]
    [HarmonyPriority(Priority.Last)]
    internal static class MechanicalFlightVisualGroundAnchorPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn pawn, ref Vector3 __result)
        {
            if (MechanicalFlightGroundAnchorContext.Active
                || MechanicalFlightGroundAnchorContext.LegacySelectionActive
                || !MechanicalFlightUtility.TryGetHoverVisualState(
                    pawn,
                    out MechanicalFlightAuthorizationRecord? record,
                    out MechanicalFlightProfileDef? profile)
                || record == null || profile == null
                || record.Phase == MechanicalFlightPhase.Crashing)
            {
                return;
            }

            if (record.Purpose == MechanicalFlightPurpose.FusionRelocation)
            {
                // DrawPos 已经加入合体专用地图北侧升降偏移；这里抵消它，
                // 让地面清洗/烟尘继续使用真实合法地图格作为锚点。
                __result.z -= MechanicalFlightVisualSmoothing
                    .FusionVisualZOffset(pawn, record);
                return;
            }

            float logicalFactor = Mathf.Clamp01(
                pawn.flight.PositionOffsetFactor);
            float visualFactor = MechanicalFlightVisualSmoothing.GetHeightFactor(
                pawn, record);
            float logicalZ = MechanicalFlightVisualSmoothing.TotalVisualZOffset(
                pawn, profile, logicalFactor);
            float visualZ = MechanicalFlightVisualSmoothing.TotalVisualZOffset(
                pawn, profile, visualFactor);

            // 原 GroundAnchorDrawPos 已减去逻辑高度；
            // DrawPos 现在使用视觉高度，因此把两者差值再抵消一次。
            __result.z -= visualZ - logicalZ;
        }
    }
}
