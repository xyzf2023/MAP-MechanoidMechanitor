using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [StaticConstructorOnStartup]
    public static class MechanicalFlightPresentationUtility
    {
        private sealed class TiltState
        {
            public float Angle;
            public int LastFrame = -1;
            public Vector3 LastDrawPos;
            public bool HasLastDrawPos;
            public int LastHorizontalMotionTick = -999999;
            public float HorizontalDirection;
        }

        private sealed class GlowState
        {
            public CompGlower Glower = null!;
            public Map Map = null!;
            public IntVec3 Position;
            public MechanicalFlightProfileDef Profile = null!;
        }

        private static readonly Dictionary<int, TiltState> TiltStates = new();
        private static readonly Dictionary<Pawn, GlowState> GlowStates = new();
        private static readonly Dictionary<int, int> LastGroundWashTick = new();
        private static readonly Dictionary<int, IntVec3> LastRevealedFogCell = new();
        internal const float VanillaFlightDrawOffset = 0.6f;

        public static Vector3 HoverVisualOffset(
            Pawn pawn,
            MechanicalFlightAuthorizationRecord record)
        {
            MechanicalFlightProfileDef? profile = record.Profile;
            if (profile == null
                || !MechanicalFlightUtility.HasHoverVisual(pawn, record))
            {
                return Vector3.zero;
            }

            // 与 TotalVisualZOffset / 起降过渡共用同一帧缓存的悬浮高度。
            float height = MechanicalFlightVisualSmoothing.ExtraHoverHeight(
                pawn, profile);
            return new Vector3(
                0f, 0f, height * pawn.flight.PositionOffsetFactor);
        }

        public static Vector3 GroundAnchorDrawPos(Pawn pawn)
        {
            Vector3 drawPos = pawn.DrawPos;
            if (!MechanicalFlightUtility.TryGetHoverVisualState(
                    pawn,
                    out MechanicalFlightAuthorizationRecord? record,
                    out _)
                || record == null)
            {
                return drawPos;
            }

            drawPos -= HoverVisualOffset(pawn, record);
            drawPos -= new Vector3(0f, 0f,
                VanillaFlightDrawOffset * pawn.flight.PositionOffsetFactor);
            return drawPos;
        }

        public static float FlightTiltAngle(
            Pawn pawn,
            MechanicalFlightAuthorizationRecord record)
        {
            MechanicalFlightProfileDef? profile = record.Profile;
            if (profile == null || !profile.allowTilt)
            {
                return 0f;
            }

            if (!TiltStates.TryGetValue(pawn.thingIDNumber, out TiltState? state))
            {
                state = new TiltState();
                TiltStates[pawn.thingIDNumber] = state;
            }
            if (state.LastFrame == Time.frameCount)
            {
                return state.Angle;
            }

            state.LastFrame = Time.frameCount;
            float target = 0f;
            Vector3 drawPos = pawn.DrawPos;
            int tick = Find.TickManager.TicksGame;
            if (MechanicalFlightUtility.HasHoverVisual(pawn, record)
                && pawn.pather?.MovingNow == true)
            {
                float horizontal = state.HasLastDrawPos ? drawPos.x - state.LastDrawPos.x : 0f;
                if (Mathf.Abs(horizontal) >= 0.0005f)
                {
                    state.LastHorizontalMotionTick = tick;
                    state.HorizontalDirection = Mathf.Sign(horizontal);
                }
                else
                {
                    horizontal = tick - state.LastHorizontalMotionTick <= 3
                        ? state.HorizontalDirection : 0f;
                }
                if (Mathf.Abs(horizontal) >= 0.0005f)
                {
                    target = Mathf.Sign(horizontal) * profile.maximumTiltAngle;
                }
            }

            state.LastDrawPos = drawPos;
            state.HasLastDrawPos = true;
            state.Angle = Mathf.MoveTowards(state.Angle, target,
                Mathf.Max(profile.minimumTiltStep, Time.deltaTime * profile.tiltSpeed));
            return state.Angle;
        }

        public static void DrawThrusterVisual(
            Pawn pawn,
            MechanicalFlightAuthorizationRecord record,
            Vector3 bodyDrawLoc,
            float tiltAngle)
        {
            // 唯一实际推进焰实现：巡飞表现层负责脉动与移动拉伸缓存。
            MechanicalFlightCruisePresentation.DrawThrusterVisual(
                pawn, record, bodyDrawLoc, tiltAngle);
        }

        internal static void Tick(Pawn pawn, MechanicalFlightAuthorizationRecord record)
        {
            MechanicalFlightProfileDef? profile = record.Profile;
            if (profile == null)
            {
                return;
            }

            // 合体快速转移的隐藏换位帧只允许逻辑 Position 改变，
            // 不得在新 landingCell 提前留下迷雾揭示、地面气流或悬浮光照。
            if (record.Purpose == MechanicalFlightPurpose.FusionRelocation
                && MechanicalFlightVisualSmoothing.IsFusionHidden(pawn))
            {
                CleanupGlow(pawn);
                return;
            }

            if (pawn.Faction == Faction.OfPlayer)
            {
                TryRevealFlightFog(pawn);
            }
            if (profile.drawGroundWash)
            {
                TryThrowGroundWash(pawn);
            }
            TryThrowThrusterSpark(pawn, record, profile);
            if (profile.useHoverGlow && IsNight(pawn.Map))
            {
                EnsureGlow(pawn, profile);
            }
            else
            {
                CleanupGlow(pawn);
            }
        }

        internal static void NotifyFlightStarted(
            Pawn pawn,
            MechanicalFlightAuthorizationRecord record)
        {
            ResetTilt(pawn);
            Tick(pawn, record);
        }

        internal static void NotifyFlightEnded(Pawn? pawn)
        {
            if (pawn == null)
            {
                return;
            }
            CleanupGlow(pawn);
            MechanicalFlightStraightPathPatch.ClearMotion(pawn);
            TiltStates.Remove(pawn.thingIDNumber);
            LastGroundWashTick.Remove(pawn.thingIDNumber);
            LastRevealedFogCell.Remove(pawn.thingIDNumber);
        }

        internal static void ClearAllRuntimeState()
        {
            // 游戏切换时旧 Map 已不再参与绘制；清空引用即可释放 Pawn/Map，
            // 图形资源缓存不含游戏对象，可以跨存档继续复用。
            TiltStates.Clear();
            GlowStates.Clear();
            LastGroundWashTick.Clear();
            LastRevealedFogCell.Clear();
            MechanicalFlightVisualSmoothing.ClearAllRuntimeState();
            MechanicalFlightCruisePresentation.ClearAllRuntimeState();
        }

        private static void ResetTilt(Pawn pawn)
        {
            TiltStates[pawn.thingIDNumber] = new TiltState
            {
                LastDrawPos = pawn.DrawPos,
                HasLastDrawPos = true
            };
        }

        private static void TryThrowGroundWash(Pawn pawn)
        {
            int tick = Find.TickManager.TicksGame;
            if ((tick + pawn.thingIDNumber) % 3 != 0
                || (LastGroundWashTick.TryGetValue(pawn.thingIDNumber, out int last)
                    && last == tick))
            {
                return;
            }
            LastGroundWashTick[pawn.thingIDNumber] = tick;

            Map map = pawn.Map;
            Vector3 groundAnchor = GroundAnchorDrawPos(pawn);
            if (pawn.Position.GetTerrain(map).IsWater
                || !groundAnchor.ShouldSpawnMotesAt(map, false))
            {
                return;
            }
            FleckDef fleck = map.snowGrid.GetDepth(pawn.Position) > 0f
                ? FleckDefOf.AirPuff : FleckDefOf.DustPuffThick;
            float angle = Rand.Range(0f, 360f);
            float radians = angle * Mathf.Deg2Rad;
            Vector3 position = groundAnchor + new Vector3(Mathf.Cos(radians), 0f,
                Mathf.Sin(radians)) * Rand.Range(1f, 1.5f);
            FleckCreationData data = FleckMaker.GetDataStatic(position, map, fleck,
                Rand.Range(1.15f, 1.45f));
            data.velocityAngle = angle;
            data.velocitySpeed = Rand.Range(0.75f, 1f);
            data.solidTimeOverride = 0.18f;
            data.rotationRate = angle;
            map.flecks.CreateFleck(data);
        }

        private static void TryThrowThrusterSpark(Pawn pawn,
            MechanicalFlightAuthorizationRecord record,
            MechanicalFlightProfileDef profile)
        {
            FleckDef? fleck = profile.thrusterSparkFleck;
            int interval = Mathf.Max(1, profile.thrusterSparkIntervalTicks);
            if (fleck == null || (Find.TickManager.TicksGame + pawn.thingIDNumber) % interval != 0
                || !pawn.DrawPos.ShouldSpawnMotesAt(pawn.Map, false))
            {
                return;
            }

            Vector3 body = pawn.DrawPos;
            float tilt = FlightTiltAngle(pawn, record);
            Quaternion rotation = Quaternion.AngleAxis(tilt, Vector3.up);
            Vector3 position = body + rotation * new Vector3(
                Rand.Range(-0.13f, 0.13f), 0f, -0.82f);
            FleckCreationData data = FleckMaker.GetDataStatic(position, pawn.Map, fleck,
                Rand.Range(0.82f, 1.18f));
            data.velocityAngle = 180f + tilt + Rand.Range(-13f, 13f);
            data.velocitySpeed = Rand.Range(1.2f, 2.2f);
            data.rotationRate = Rand.Range(-90f, 90f);
            pawn.Map.flecks.CreateFleck(data);
        }

        private static void EnsureGlow(Pawn pawn, MechanicalFlightProfileDef profile)
        {
            Map map = pawn.Map;
            if (!GlowStates.TryGetValue(pawn, out GlowState? state)
                || state.Profile != profile)
            {
                CleanupGlow(pawn);
                CompGlower glower = new() { parent = pawn };
                glower.Initialize(new CompProperties_Glower
                {
                    glowRadius = profile.hoverGlowRadius,
                    glowColor = profile.hoverGlowColor
                });
                glower.ForceRegister(map);
                GlowStates[pawn] = new GlowState
                {
                    Glower = glower,
                    Map = map,
                    Position = pawn.Position,
                    Profile = profile
                };
                return;
            }
            if (state.Map != map || state.Position != pawn.Position)
            {
                state.Map.glowGrid.DeRegisterGlower(state.Glower);
                state.Map = map;
                state.Position = pawn.Position;
                state.Glower.ForceRegister(map);
            }
        }

        private static void CleanupGlow(Pawn pawn)
        {
            if (GlowStates.TryGetValue(pawn, out GlowState? state))
            {
                state.Map?.glowGrid.DeRegisterGlower(state.Glower);
                GlowStates.Remove(pawn);
            }
        }

        private static bool IsNight(Map map)
        {
            int hour = GenLocalDate.HourOfDay(map);
            return hour >= 19 || hour < 6;
        }

        private static void TryRevealFlightFog(Pawn pawn)
        {
            int key = pawn.thingIDNumber;
            IntVec3 center = pawn.Position;
            if (LastRevealedFogCell.TryGetValue(key, out IntVec3 last)
                && last == center)
            {
                return;
            }

            LastRevealedFogCell[key] = center;
            RevealFlightFogArea(center, pawn.Map);
        }

        private static void RevealFlightFogArea(IntVec3 center, Map map)
        {
            const int cellRadius = 2;
            for (int x = -cellRadius; x <= cellRadius; x++)
            {
                for (int z = -cellRadius; z <= cellRadius; z++)
                {
                    IntVec3 cell = center + new IntVec3(x, 0, z);
                    if (cell.InBounds(map) && map.fogGrid.IsFogged(cell))
                    {
                        map.fogGrid.Unfog(cell);
                    }
                }
            }
        }
    }

    internal static class MechanicalFlightGroundAnchorContext
    {
        [ThreadStatic] private static int groundAnchorDepth;
        [ThreadStatic] private static int legacySelectionDepth;
        [ThreadStatic] private static int shadowCompensationDepth;

        internal static bool Active => groundAnchorDepth > 0;
        internal static bool LegacySelectionActive => legacySelectionDepth > 0;
        internal static bool ShadowCompensationActive => shadowCompensationDepth > 0;

        internal static void Begin() => groundAnchorDepth++;
        internal static void End()
        {
            if (groundAnchorDepth > 0)
            {
                groundAnchorDepth--;
            }
        }

        internal static void BeginLegacySelection() => legacySelectionDepth++;
        internal static void EndLegacySelection()
        {
            if (legacySelectionDepth > 0)
            {
                legacySelectionDepth--;
            }
        }

        internal static void BeginShadowCompensation() => shadowCompensationDepth++;
        internal static void EndShadowCompensation()
        {
            if (shadowCompensationDepth > 0)
            {
                shadowCompensationDepth--;
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_DrawTracker), nameof(Pawn_DrawTracker.DrawPos),
        MethodType.Getter)]
    internal static class MechanicalFlightPawnDrawPosPatch
    {
        private static readonly AccessTools.FieldRef<Pawn_DrawTracker, Pawn> PawnField =
            AccessTools.FieldRefAccess<Pawn_DrawTracker, Pawn>("pawn");

        public static void Postfix(Pawn_DrawTracker __instance, ref Vector3 __result)
        {
            Pawn pawn = PawnField(__instance);
            if (!MechanicalFlightUtility.TryGetHoverVisualState(
                    pawn,
                    out MechanicalFlightAuthorizationRecord? record,
                    out _)
                || record == null)
            {
                return;
            }

            if (MechanicalFlightStraightPathPatch.TryGetExactGroundDrawPos(
                    pawn, out Vector3 exactGroundDrawPos))
            {
                __result.x = exactGroundDrawPos.x;
                __result.z = exactGroundDrawPos.z
                    + MechanicalFlightPresentationUtility.VanillaFlightDrawOffset
                    * pawn.flight.PositionOffsetFactor;
            }

            if (MechanicalFlightGroundAnchorContext.ShadowCompensationActive)
            {
                // DrawShadowInternal 随后还会减去完整 PositionOffsetFactor。
                // 此处补足原版 DrawPos 的剩余 0.4，使最终阴影恰好回到地面锚点。
                __result += new Vector3(0f, 0f,
                    (1f - MechanicalFlightPresentationUtility.VanillaFlightDrawOffset)
                    * pawn.flight.PositionOffsetFactor);
                return;
            }

            if (MechanicalFlightGroundAnchorContext.Active)
            {
                __result -= new Vector3(0f, 0f,
                    MechanicalFlightPresentationUtility.VanillaFlightDrawOffset
                    * pawn.flight.PositionOffsetFactor);
                return;
            }

            // 鼠标选取沿用原版飞行 DrawPos：保留0.6格偏移，
            // 但不加入本系统额外的悬浮显示高度。
            if (MechanicalFlightGroundAnchorContext.LegacySelectionActive)
            {
                return;
            }

            __result += MechanicalFlightPresentationUtility.HoverVisualOffset(
                pawn, record);
        }
    }

    [HarmonyPatch(typeof(SelectionDrawer),
        nameof(SelectionDrawer.DrawSelectionBracketFor),
        new Type[] { typeof(object), typeof(Material) })]
    internal static class MechanicalFlightSelectionAnchorPatch
    {
        public static void Prefix() => MechanicalFlightGroundAnchorContext.Begin();

        public static Exception Finalizer(Exception __exception)
        {
            MechanicalFlightGroundAnchorContext.End();
            return __exception;
        }
    }

    [HarmonyPatch(typeof(GenUI), nameof(GenUI.ThingsUnderMouse),
        new Type[]
        {
            typeof(Vector3), typeof(float), typeof(TargetingParameters),
            typeof(ITargetingSource)
        })]
    internal static class MechanicalFlightMouseAnchorPatch
    {
        private const float ExtendedHorizontalRadius = 0.55f;
        private const float ExtendedVerticalRadius = 1.35f;

        public static void Prefix() =>
            MechanicalFlightGroundAnchorContext.BeginLegacySelection();

        public static void Postfix(
            Vector3 clickPos,
            float pawnWideClickRadius,
            TargetingParameters clickParams,
            ITargetingSource source,
            ref List<Thing> __result)
        {
            // Selector 使用1格宽选取半径；0.8格调用来自浮动菜单等其他交互，
            // 不应因本次左键选中手感调整而扩大目标获取范围。
            Map? map = Find.CurrentMap;
            if (pawnWideClickRadius < 0.999f || map == null)
            {
                return;
            }

            IReadOnlyList<MechanicalFlightAuthorizationRecord> records =
                GameComponent_MechanicalFlightRegistry.GetActiveRecordsForReading();
            for (int i = 0; i < records.Count; i++)
            {
                MechanicalFlightAuthorizationRecord? record = records[i];
                Pawn? pawn = record?.Pawn;
                if (pawn == null || pawn.Dead || pawn.Destroyed || pawn.Discarded
                    || !pawn.Spawned || pawn.Map != map
                    || !MechanicalFlightUtility.HasHoverVisual(pawn, record!)
                    || pawn.IsHiddenFromPlayer()
                    || __result.Contains(pawn)
                    || !clickParams.CanTarget(pawn, source))
                {
                    continue;
                }

                // 当前上下文中的 DrawPos 是修改前的原版飞行选中点：
                // 保留0.6格原版偏移，但排除额外2.5格悬浮显示高度。
                Vector3 selectionCenter = pawn.DrawPos;
                float normalizedX =
                    (clickPos.x - selectionCenter.x) / ExtendedHorizontalRadius;
                float normalizedZ =
                    (clickPos.z - selectionCenter.z) / ExtendedVerticalRadius;
                if (normalizedX * normalizedX + normalizedZ * normalizedZ <= 1f)
                {
                    // 仅追加候选，不替换或强制置顶，保留原版连续点击轮换逻辑。
                    __result.Add(pawn);
                }
            }
        }

        public static Exception Finalizer(Exception __exception)
        {
            MechanicalFlightGroundAnchorContext.EndLegacySelection();
            return __exception;
        }
    }

    [HarmonyPatch(typeof(PawnRenderer), nameof(PawnRenderer.DynamicDrawPhaseAt))]
    internal static class MechanicalFlightDynamicDrawPatch
    {
        private static readonly AccessTools.FieldRef<PawnRenderer, Pawn> PawnField =
            AccessTools.FieldRefAccess<PawnRenderer, Pawn>("pawn");

        public static void Prefix(PawnRenderer __instance, DrawPhase phase,
            Vector3 drawLoc)
        {
            Pawn pawn = PawnField(__instance);
            if (phase != DrawPhase.Draw
                || !MechanicalFlightUtility.TryGetHoverVisualState(
                    pawn,
                    out MechanicalFlightAuthorizationRecord? record,
                    out _)
                || record == null)
            {
                return;
            }

            float angle = MechanicalFlightPresentationUtility.FlightTiltAngle(
                pawn, record);
            MechanicalFlightPresentationUtility.DrawThrusterVisual(
                pawn, record, drawLoc, angle);
        }
    }

    [HarmonyPatch(typeof(PawnRenderer), "GetDrawParms")]
    internal static class MechanicalFlightDrawAnglePatch
    {
        private static readonly AccessTools.FieldRef<PawnRenderer, Pawn> PawnField =
            AccessTools.FieldRefAccess<PawnRenderer, Pawn>("pawn");
        public static void Prefix(
            PawnRenderer __instance,
            PawnRenderFlags flags,
            ref float angle)
        {
            // GetDrawParms 同时服务于地图绘制与 PortraitsCache 离屏头像渲染。
            // UI 头像不应继承地图上的飞行倾角，否则头像首次在飞行中生成缓存时，
            // 会以当时的倾斜姿态固定显示在机械控制组等界面中。
            if (flags.FlagSet(PawnRenderFlags.Portrait))
            {
                return;
            }

            Pawn pawn = PawnField(__instance);
            if (MechanicalFlightUtility.TryGetHoverVisualState(
                    pawn, out var record, out _)
                && record != null)
            {
                angle += MechanicalFlightPresentationUtility.FlightTiltAngle(pawn, record);
            }
        }
    }

    [HarmonyPatch(typeof(PawnRenderer), "DrawShadowInternal")]
    internal static class MechanicalFlightShadowPatch
    {
        private static readonly AccessTools.FieldRef<PawnRenderer, Pawn> PawnField =
            AccessTools.FieldRefAccess<PawnRenderer, Pawn>("pawn");

        // 原版飞行分支会在 pawn.DrawPos 之后再减去完整的 PositionOffsetFactor；
        // 使用独立上下文只补偿该次读取，避免影响选框所需的地面锚点语义。
        public static void Prefix(PawnRenderer __instance, out bool __state)
        {
            Pawn pawn = PawnField(__instance);
            __state = MechanicalFlightUtility.TryGetHoverVisualState(
                pawn, out _, out _);
            if (__state)
            {
                MechanicalFlightGroundAnchorContext.BeginShadowCompensation();
            }
        }

        public static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state)
            {
                MechanicalFlightGroundAnchorContext.EndShadowCompensation();
            }
            return __exception;
        }
    }
}
