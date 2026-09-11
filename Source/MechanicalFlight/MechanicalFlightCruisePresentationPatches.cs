using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace MAP_MechanoidMechanitor
{
    internal static class MechanicalFlightCruisePresentation
    {
        private const int PulseSteps = 8;
        private const int StretchSteps = 6;

        private sealed class ExhaustState
        {
            internal float Stretch;
            internal int LastFrame = -1;
        }

        private sealed class ThrusterGraphicSet
        {
            internal readonly Graphic[,] Flames = new Graphic[PulseSteps, StretchSteps];
            internal readonly Graphic[,] Cores = new Graphic[PulseSteps, StretchSteps];
            internal readonly Graphic[] Glows = new Graphic[PulseSteps];
        }

        private static readonly Dictionary<int, ExhaustState> ExhaustStates = new();
        private static readonly Dictionary<MechanicalFlightProfileDef, ThrusterGraphicSet>
            ThrusterGraphics = new();
        private static Graphic[]? groundWashGraphics;

        internal static void Reset(Pawn? pawn)
        {
            if (pawn != null)
            {
                ExhaustStates.Remove(pawn.thingIDNumber);
            }
        }

        internal static float ExhaustStretch(Pawn pawn)
        {
            if (!ExhaustStates.TryGetValue(pawn.thingIDNumber, out ExhaustState? state))
            {
                state = new ExhaustState();
                ExhaustStates[pawn.thingIDNumber] = state;
            }

            if (state.LastFrame == Time.frameCount)
            {
                return state.Stretch;
            }

            state.LastFrame = Time.frameCount;
            float target = MechanicalFlightUtility.HasHoverVisual(pawn)
                && pawn.pather?.MovingNow == true
                ? 1f
                : 0f;
            state.Stretch = Mathf.MoveTowards(
                state.Stretch,
                target,
                Mathf.Max(0.012f, Time.deltaTime * 2.8f));
            return state.Stretch;
        }

        internal static void DrawThrusterVisual(
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

            ThrusterGraphicSet graphics = GetGraphics(profile);
            float pulse = (Mathf.Sin((Find.TickManager.TicksGame + pawn.thingIDNumber)
                * 0.24f) + 1f) * 0.5f;
            int pulseIndex = Mathf.Clamp(
                Mathf.RoundToInt(pulse * (PulseSteps - 1)), 0, PulseSteps - 1);
            float stretch = ExhaustStretch(pawn);
            int stretchIndex = Mathf.Clamp(
                Mathf.RoundToInt(stretch * (StretchSteps - 1)), 0, StretchSteps - 1);

            Quaternion rotation = Quaternion.AngleAxis(tiltAngle, Vector3.up);
            Vector3 exhaust = bodyDrawLoc
                + rotation * new Vector3(0f, 0f, -0.72f);
            exhaust.y = AltitudeLayer.Projectile.AltitudeFor();

            graphics.Glows[pulseIndex].Draw(
                exhaust + rotation * new Vector3(0f, 0f, 0.25f),
                Rot4.North, pawn, tiltAngle);
            float flameAngle = tiltAngle + profile.thrusterAngleOffset;
            graphics.Flames[pulseIndex, stretchIndex].Draw(
                exhaust, Rot4.North, pawn, flameAngle);
            graphics.Cores[pulseIndex, stretchIndex].Draw(
                exhaust + rotation * new Vector3(0f, 0f, 0.17f),
                Rot4.North, pawn, flameAngle);
            DrawPersistentGroundWash(pawn, profile);
        }

        internal static void PlayLandingEffects(
            Pawn pawn,
            MechanicalFlightProfileDef profile)
        {
            Map? map = pawn.Map;
            if (!pawn.Spawned || map == null)
            {
                return;
            }

            Vector3 position = pawn.TrueCenter();
            if (profile.landingGlowFleck != null)
            {
                FleckMaker.Static(position, map, profile.landingGlowFleck, 0.9f);
            }
            if (profile.thrusterSparkFleck != null)
            {
                for (int i = 0; i < 4; i++)
                {
                    FleckMaker.Static(
                        position + Gen.RandomHorizontalVector(0.35f),
                        map,
                        profile.thrusterSparkFleck,
                        0.35f);
                }
            }
            FleckMaker.ThrowDustPuff(position, map, 2.2f);
            DefDatabase<SoundDef>.GetNamedSilentFail("Explosion_Thump")?.PlayOneShot(
                new TargetInfo(pawn.Position, map));
        }

        private static ThrusterGraphicSet GetGraphics(MechanicalFlightProfileDef profile)
        {
            if (ThrusterGraphics.TryGetValue(profile, out ThrusterGraphicSet? graphics))
            {
                return graphics;
            }

            graphics = new ThrusterGraphicSet();
            float cruiseWidth = Mathf.Max(0.05f, profile.thrusterCruiseWidthFactor);
            float cruiseLength = Mathf.Max(0.05f, profile.thrusterCruiseLengthFactor);
            for (int pulseIndex = 0; pulseIndex < PulseSteps; pulseIndex++)
            {
                float pulse = pulseIndex / (float)(PulseSteps - 1);
                graphics.Glows[pulseIndex] = GraphicDatabase.Get<Graphic_Single>(
                    profile.thrusterGlowTexture,
                    ShaderDatabase.MoteGlow,
                    new Vector2(1.15f, 1.15f),
                    new Color(0.7f, 0.9f, 1f, Mathf.Lerp(0.62f, 0.80f, pulse)));

                for (int stretchIndex = 0; stretchIndex < StretchSteps; stretchIndex++)
                {
                    float stretch = stretchIndex / (float)(StretchSteps - 1);
                    float widthFactor = Mathf.Lerp(1f, cruiseWidth, stretch);
                    float lengthFactor = Mathf.Lerp(1f, cruiseLength, stretch);
                    graphics.Flames[pulseIndex, stretchIndex] =
                        GraphicDatabase.Get<Graphic_Single>(
                            profile.thrusterFlameTexture,
                            ShaderDatabase.MoteGlow,
                            new Vector2(0.78f * widthFactor, 2.55f * lengthFactor),
                            new Color(
                                Mathf.Lerp(0.27f, 0.34f, pulse),
                                Mathf.Lerp(0.67f, 0.78f, pulse),
                                1f,
                                Mathf.Lerp(0.68f, 0.82f, pulse)));
                    graphics.Cores[pulseIndex, stretchIndex] =
                        GraphicDatabase.Get<Graphic_Single>(
                            profile.thrusterFlameTexture,
                            ShaderDatabase.TransparentPostLight,
                            new Vector2(
                                Mathf.Lerp(0.40f, 0.44f, pulse) * widthFactor,
                                1.75f * lengthFactor),
                            new Color(
                                0.9f,
                                0.98f,
                                1f,
                                Mathf.Lerp(0.88f, 0.98f, pulse)));
                }
            }

            ThrusterGraphics[profile] = graphics;
            return graphics;
        }

        private static void DrawPersistentGroundWash(
            Pawn pawn,
            MechanicalFlightProfileDef profile)
        {
            if (!profile.drawGroundWash || pawn.Map == null
                || pawn.Position.GetTerrain(pawn.Map).IsWater)
            {
                return;
            }

            groundWashGraphics ??= new[]
            {
                GraphicDatabase.Get<Graphic_Single>(
                    "Things/Mote/DustPuff",
                    ShaderDatabase.TransparentPostLight,
                    new Vector2(0.9f, 0.9f),
                    new Color(0.78f, 0.73f, 0.65f, 0.52f)),
                GraphicDatabase.Get<Graphic_Single>(
                    "Things/Mote/DustPuff",
                    ShaderDatabase.TransparentPostLight,
                    new Vector2(1.15f, 1.15f),
                    new Color(0.78f, 0.73f, 0.65f, 0.37f)),
                GraphicDatabase.Get<Graphic_Single>(
                    "Things/Mote/DustPuff",
                    ShaderDatabase.TransparentPostLight,
                    new Vector2(1.4f, 1.4f),
                    new Color(0.78f, 0.73f, 0.65f, 0.22f))
            };

            Vector3 groundAnchor = MechanicalFlightPresentationUtility.GroundAnchorDrawPos(pawn);
            int ticks = Find.TickManager.TicksGame + pawn.thingIDNumber;
            float cycle = ticks % 54 / 54f;
            float turn = ticks / 54 * 41f + pawn.thingIDNumber % 360;
            for (int i = 0; i < groundWashGraphics.Length; i++)
            {
                float phase = Mathf.Repeat(cycle + i / 3f, 1f);
                float angle = (turn + i * 120f) * Mathf.Deg2Rad;
                Vector3 pos = groundAnchor
                    + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle))
                    * Mathf.Lerp(0.35f, 1.65f, phase);
                pos.y = AltitudeLayer.Filth.AltitudeFor();
                groundWashGraphics[i].Draw(pos, Rot4.North, pawn, 0f);
            }
        }
    }

    /// <summary>
    /// 保留现有推进焰绘制入口，以“脉动 × 移动拉伸”的缓存图形替换固定长度版本。
    /// </summary>
    [HarmonyPatch(typeof(MechanicalFlightPresentationUtility),
        nameof(MechanicalFlightPresentationUtility.DrawThrusterVisual))]
    internal static class MechanicalFlightThrusterStretchPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(
            Pawn pawn,
            MechanicalFlightAuthorizationRecord record,
            Vector3 bodyDrawLoc,
            float tiltAngle)
        {
            MechanicalFlightProfileDef? profile = record?.Profile;
            if (profile == null || !profile.drawThruster
                || !MechanicalFlightUtility.HasHoverVisual(pawn))
            {
                return true;
            }

            MechanicalFlightCruisePresentation.DrawThrusterVisual(
                pawn, record!, bodyDrawLoc, tiltAngle);
            return false;
        }
    }

    [HarmonyPatch(typeof(MechanicalFlightPresentationUtility), "NotifyFlightStarted")]
    internal static class MechanicalFlightCruiseStartResetPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Pawn pawn)
        {
            MechanicalFlightCruisePresentation.Reset(pawn);
        }
    }

    [HarmonyPatch(typeof(MechanicalFlightPresentationUtility), "NotifyFlightEnded")]
    internal static class MechanicalFlightCruiseEndResetPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn? pawn)
        {
            MechanicalFlightCruisePresentation.Reset(pawn);
        }
    }

    /// <summary>
    /// 只在真正完成普通降落或安全紧急迫降时播放一次触地反馈。
    /// 倒地/死亡坠毁同样会把状态清回 Grounded，因此额外排除 Downed/Dead，
    /// 避免坠毁爆炸与正常触地效果叠加。
    /// </summary>
    [HarmonyPatch(typeof(MechanicalFlightUtility), "Tick")]
    internal static class MechanicalFlightLandingEffectsPatch
    {
        [HarmonyPrefix]
        public static void Prefix(
            MechanicalFlightAuthorizationRecord record,
            out MechanicalFlightPhase __state)
        {
            __state = record.Phase;
        }

        [HarmonyPostfix]
        public static void Postfix(
            MechanicalFlightAuthorizationRecord record,
            MechanicalFlightPhase __state)
        {
            if ((__state != MechanicalFlightPhase.Landing
                    && __state != MechanicalFlightPhase.EmergencyLanding)
                || record.Phase != MechanicalFlightPhase.Grounded)
            {
                return;
            }

            Pawn? pawn = record.Pawn;
            MechanicalFlightProfileDef? profile = record.Profile;
            if (pawn == null || profile == null || pawn.Dead || pawn.Downed
                || !pawn.Spawned || pawn.Map == null)
            {
                return;
            }

            MechanicalFlightCruisePresentation.PlayLandingEffects(pawn, profile);
        }
    }
}
