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
        private static readonly Dictionary<string, Graphic[]> FlameGraphics = new();
        private static readonly Dictionary<string, Graphic[]> CoreGraphics = new();
        private static readonly Dictionary<string, Graphic[]> GlowGraphics = new();
        private static Graphic[]? groundWashGraphics;
        private const int PulseSteps = 8;

        public static Vector3 HoverVisualOffset(
            Pawn pawn,
            MechanicalFlightAuthorizationRecord record)
        {
            MechanicalFlightProfileDef? profile = record.Profile;
            if (profile == null || !MechanicalFlightUtility.HasHoverVisual(pawn))
            {
                return Vector3.zero;
            }

            float period = Mathf.Max(1f, profile.hoverBobPeriodTicks);
            float phase = (Find.TickManager.TicksGame + pawn.thingIDNumber % 100)
                / period * Mathf.PI * 2f;
            float height = profile.hoverExtraVisualHeight
                + Mathf.Sin(phase) * profile.hoverBobAmplitude;
            return new Vector3(0f, 0f, height * pawn.flight.PositionOffsetFactor);
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
            if (MechanicalFlightUtility.HasHoverVisual(pawn) && pawn.pather?.MovingNow == true)
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
            MechanicalFlightProfileDef? profile = record.Profile;
            if (profile == null || !profile.drawThruster
                || !MechanicalFlightUtility.HasHoverVisual(pawn))
            {
                return;
            }

            Graphic[] flames = GetFlameGraphics(profile);
            Graphic[] cores = GetCoreGraphics(profile);
            Graphic[] glows = GetGlowGraphics(profile);
            float pulse = (Mathf.Sin((Find.TickManager.TicksGame + pawn.thingIDNumber)
                * 0.24f) + 1f) * 0.5f;
            int index = Mathf.Clamp(Mathf.RoundToInt(pulse * (PulseSteps - 1)),
                0, PulseSteps - 1);
            Quaternion rotation = Quaternion.AngleAxis(tiltAngle, Vector3.up);
            Vector3 exhaust = bodyDrawLoc
                + rotation * new Vector3(0f, 0f, -0.72f);
            exhaust.y = AltitudeLayer.Projectile.AltitudeFor();

            glows[index].Draw(exhaust + rotation * new Vector3(0f, 0f, 0.25f),
                Rot4.North, pawn, tiltAngle);
            float flameAngle = tiltAngle + profile.thrusterAngleOffset;
            flames[index].Draw(exhaust, Rot4.North, pawn, flameAngle);
            cores[index].Draw(exhaust + rotation * new Vector3(0f, 0f, 0.17f),
                Rot4.North, pawn, flameAngle);
            DrawPersistentGroundWash(pawn, profile);
        }

        internal static void Tick(Pawn pawn, MechanicalFlightAuthorizationRecord record)
        {
            MechanicalFlightProfileDef? profile = record.Profile;
            if (profile == null)
            {
                return;
            }

            if (pawn.Faction == Faction.OfPlayer)
            {
                RevealFlightFogArea(pawn.Position, pawn.Map);
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
            TiltStates.Remove(pawn.thingIDNumber);
            LastGroundWashTick.Remove(pawn.thingIDNumber);
        }

        private static Graphic[] GetFlameGraphics(MechanicalFlightProfileDef profile)
        {
            if (!FlameGraphics.TryGetValue(profile.thrusterFlameTexture, out Graphic[]? graphics))
            {
                graphics = new Graphic[PulseSteps];
                for (int i = 0; i < PulseSteps; i++)
                {
                    float t = i / (float)(PulseSteps - 1);
                    graphics[i] = GraphicDatabase.Get<Graphic_Single>(
                        profile.thrusterFlameTexture, ShaderDatabase.MoteGlow,
                        new Vector2(0.78f, 2.55f),
                        new Color(Mathf.Lerp(0.27f, 0.34f, t),
                            Mathf.Lerp(0.67f, 0.78f, t), 1f,
                            Mathf.Lerp(0.68f, 0.82f, t)));
                }
                FlameGraphics[profile.thrusterFlameTexture] = graphics;
            }
            return graphics;
        }

        private static Graphic[] GetCoreGraphics(MechanicalFlightProfileDef profile)
        {
            if (!CoreGraphics.TryGetValue(profile.thrusterFlameTexture, out Graphic[]? graphics))
            {
                graphics = new Graphic[PulseSteps];
                for (int i = 0; i < PulseSteps; i++)
                {
                    float t = i / (float)(PulseSteps - 1);
                    graphics[i] = GraphicDatabase.Get<Graphic_Single>(
                        profile.thrusterFlameTexture, ShaderDatabase.TransparentPostLight,
                        new Vector2(Mathf.Lerp(0.40f, 0.44f, t), 1.75f),
                        new Color(0.9f, 0.98f, 1f,
                            Mathf.Lerp(0.88f, 0.98f, t)));
                }
                CoreGraphics[profile.thrusterFlameTexture] = graphics;
            }
            return graphics;
        }

        private static Graphic[] GetGlowGraphics(MechanicalFlightProfileDef profile)
        {
            if (!GlowGraphics.TryGetValue(profile.thrusterGlowTexture,
                    out Graphic[]? graphics))
            {
                graphics = new Graphic[PulseSteps];
                for (int i = 0; i < PulseSteps; i++)
                {
                    float t = i / (float)(PulseSteps - 1);
                    graphics[i] = GraphicDatabase.Get<Graphic_Single>(
                        profile.thrusterGlowTexture, ShaderDatabase.MoteGlow,
                        new Vector2(1.15f, 1.15f),
                        new Color(0.7f, 0.9f, 1f, Mathf.Lerp(0.62f, 0.80f, t)));
                }
                GlowGraphics[profile.thrusterGlowTexture] = graphics;
            }
            return graphics;
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
            if (pawn.Position.GetTerrain(map).IsWater
                || !pawn.DrawPos.ShouldSpawnMotesAt(map, false))
            {
                return;
            }
            FleckDef fleck = map.snowGrid.GetDepth(pawn.Position) > 0f
                ? FleckDefOf.AirPuff : FleckDefOf.DustPuffThick;
            float angle = Rand.Range(0f, 360f);
            float radians = angle * Mathf.Deg2Rad;
            Vector3 position = pawn.DrawPos + new Vector3(Mathf.Cos(radians), 0f,
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

            Vector3 body = pawn.DrawPos + HoverVisualOffset(pawn, record);
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

        private static void DrawPersistentGroundWash(Pawn pawn, MechanicalFlightProfileDef profile)
        {
            if (!profile.drawGroundWash || pawn.Position.GetTerrain(pawn.Map).IsWater)
            {
                return;
            }
            groundWashGraphics ??= new[]
            {
                GraphicDatabase.Get<Graphic_Single>("Things/Mote/DustPuff",
                    ShaderDatabase.TransparentPostLight, new Vector2(0.9f, 0.9f),
                    new Color(0.78f, 0.73f, 0.65f, 0.52f)),
                GraphicDatabase.Get<Graphic_Single>("Things/Mote/DustPuff",
                    ShaderDatabase.TransparentPostLight, new Vector2(1.15f, 1.15f),
                    new Color(0.78f, 0.73f, 0.65f, 0.37f)),
                GraphicDatabase.Get<Graphic_Single>("Things/Mote/DustPuff",
                    ShaderDatabase.TransparentPostLight, new Vector2(1.4f, 1.4f),
                    new Color(0.78f, 0.73f, 0.65f, 0.22f))
            };
            int ticks = Find.TickManager.TicksGame + pawn.thingIDNumber;
            float cycle = ticks % 54 / 54f;
            float turn = ticks / 54 * 41f + pawn.thingIDNumber % 360;
            for (int i = 0; i < 3; i++)
            {
                float phase = Mathf.Repeat(cycle + i / 3f, 1f);
                float angle = (turn + i * 120f) * Mathf.Deg2Rad;
                Vector3 pos = pawn.DrawPos + new Vector3(Mathf.Cos(angle), 0f,
                    Mathf.Sin(angle)) * Mathf.Lerp(0.35f, 1.65f, phase);
                pos.y = AltitudeLayer.Filth.AltitudeFor();
                groundWashGraphics[i].Draw(pos, Rot4.North, pawn, 0f);
            }
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

        private static void RevealFlightFogArea(IntVec3 center, Map map)
        {
            const float radius = 1.45f;
            int cellRadius = Mathf.CeilToInt(radius);
            for (int x = -cellRadius; x <= cellRadius; x++)
            {
                for (int z = -cellRadius; z <= cellRadius; z++)
                {
                    if (x * x + z * z > radius * radius)
                    {
                        continue;
                    }
                    IntVec3 cell = center + new IntVec3(x, 0, z);
                    if (cell.InBounds(map) && map.fogGrid.IsFogged(cell))
                    {
                        map.fogGrid.Unfog(cell);
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(PawnRenderer), nameof(PawnRenderer.DynamicDrawPhaseAt))]
    internal static class MechanicalFlightDynamicDrawPatch
    {
        private static readonly AccessTools.FieldRef<PawnRenderer, Pawn> PawnField =
            AccessTools.FieldRefAccess<PawnRenderer, Pawn>("pawn");
        [ThreadStatic] private static HashSet<PawnRenderer>? offsetInProgress;
        private static HashSet<PawnRenderer> OffsetInProgress =>
            offsetInProgress ??= new HashSet<PawnRenderer>();

        internal static bool IsOffsetInProgress(PawnRenderer renderer) =>
            OffsetInProgress.Contains(renderer);

        public static void Prefix(PawnRenderer __instance, DrawPhase phase, ref Vector3 drawLoc)
        {
            Pawn pawn = PawnField(__instance);
            if (!GameComponent_MechanicalFlightRegistry.TryGetRecord(pawn, out var record)
                || record == null || !pawn.Spawned
                || !MechanicalFlightUtility.HasHoverVisual(pawn))
            {
                return;
            }
            drawLoc += MechanicalFlightPresentationUtility.HoverVisualOffset(pawn, record);
            OffsetInProgress.Add(__instance);
            if (phase == DrawPhase.Draw)
            {
                float angle = MechanicalFlightPresentationUtility.FlightTiltAngle(pawn, record);
                MechanicalFlightPresentationUtility.DrawThrusterVisual(
                    pawn, record, drawLoc, angle);
            }
        }

        public static void Postfix(PawnRenderer __instance) => OffsetInProgress.Remove(__instance);
        public static Exception Finalizer(PawnRenderer __instance, Exception __exception)
        {
            OffsetInProgress.Remove(__instance);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(PawnRenderer), "GetDrawParms")]
    internal static class MechanicalFlightDrawAnglePatch
    {
        private static readonly AccessTools.FieldRef<PawnRenderer, Pawn> PawnField =
            AccessTools.FieldRefAccess<PawnRenderer, Pawn>("pawn");
        public static void Prefix(PawnRenderer __instance, ref float angle)
        {
            Pawn pawn = PawnField(__instance);
            if (GameComponent_MechanicalFlightRegistry.TryGetRecord(pawn, out var record)
                && record != null && pawn.Spawned
                && MechanicalFlightUtility.HasHoverVisual(pawn))
            {
                angle += MechanicalFlightPresentationUtility.FlightTiltAngle(pawn, record);
            }
        }
    }

    [HarmonyPatch(typeof(PawnRenderer), "ParallelPreRenderPawnAt")]
    internal static class MechanicalFlightPreRenderPatch
    {
        private static readonly AccessTools.FieldRef<PawnRenderer, Pawn> PawnField =
            AccessTools.FieldRefAccess<PawnRenderer, Pawn>("pawn");
        public static void Prefix(PawnRenderer __instance, ref Vector3 drawLoc)
        {
            if (MechanicalFlightDynamicDrawPatch.IsOffsetInProgress(__instance))
            {
                return;
            }
            Pawn pawn = PawnField(__instance);
            if (GameComponent_MechanicalFlightRegistry.TryGetRecord(pawn, out var record)
                && record != null && pawn.Spawned
                && MechanicalFlightUtility.HasHoverVisual(pawn))
            {
                drawLoc += MechanicalFlightPresentationUtility.HoverVisualOffset(pawn, record);
            }
        }
    }

    [HarmonyPatch(typeof(PawnRenderer), "DrawShadowInternal")]
    internal static class MechanicalFlightShadowPatch
    {
        private static readonly AccessTools.FieldRef<PawnRenderer, Pawn> PawnField =
            AccessTools.FieldRefAccess<PawnRenderer, Pawn>("pawn");
        public static void Prefix(PawnRenderer __instance, ref Vector3 drawLoc)
        {
            Pawn pawn = PawnField(__instance);
            if (GameComponent_MechanicalFlightRegistry.TryGetRecord(pawn, out var record)
                && record != null && pawn.Spawned
                && MechanicalFlightUtility.HasHoverVisual(pawn))
            {
                drawLoc -= MechanicalFlightPresentationUtility.HoverVisualOffset(pawn, record);
            }
        }
    }
}
