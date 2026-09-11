using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 通用机械飞行的起飞巡航曲线。只限制 TakingOff 阶段的水平速度；
    /// 稳定悬浮和紧急迫降继续使用配置中的完整飞行速度。
    /// </summary>
    internal static class MechanicalFlightCruiseUtility
    {
        internal static float TravelRamp(
            Pawn? pawn,
            MechanicalFlightAuthorizationRecord? record)
        {
            MechanicalFlightProfileDef? profile = record?.Profile;
            if (pawn?.flight == null || profile == null
                || record!.Phase != MechanicalFlightPhase.TakingOff)
            {
                return 1f;
            }

            float start = Mathf.Clamp01(profile.takeoffTravelRampStartFactor);
            float full = Mathf.Clamp01(profile.takeoffTravelRampFullSpeedFactor);
            if (full <= start)
            {
                full = Mathf.Min(1f, start + 0.001f);
            }

            float progress = Mathf.InverseLerp(
                start, full, pawn.flight.PositionOffsetFactor);
            return Mathf.SmoothStep(0f, 1f, progress);
        }

        internal static float RampAdjustedFlightSpeed(
            MechanicalFlightProfileDef profile,
            Pawn pawn)
        {
            float baseSpeed = Mathf.Max(0.01f, profile.flightCellsPerSecond);
            if (!GameComponent_MechanicalFlightRegistry.TryGetRecord(
                    pawn, out MechanicalFlightAuthorizationRecord? record)
                || record?.Profile != profile)
            {
                return baseSpeed;
            }

            // TickDirectPath 后续会再次以 0.01 做最小值保护。
            // 起飞最初阶段保持这一极小值，避免除零，同时视觉上等同原地抬升。
            return baseSpeed * TravelRamp(pawn, record);
        }
    }

    /// <summary>
    /// 将连续直线飞行实际使用的 flightCellsPerSecond 替换为起飞曲线后的速度。
    /// 仅替换 TickDirectPath 内唯一一次配置速度读取，不接管或复制现有寻路逻辑。
    /// </summary>
    [HarmonyPatch(typeof(MechanicalFlightStraightPathPatch), "TickDirectPath")]
    internal static class MechanicalFlightTakeoffAccelerationPatch
    {
        private const string LogPrefix =
            "[MAP-机械族机械师] MechanicalFlightTakeoffAccelerationPatch：";

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new(instructions);
            FieldInfo? speedField = AccessTools.Field(
                typeof(MechanicalFlightProfileDef),
                nameof(MechanicalFlightProfileDef.flightCellsPerSecond));
            MethodInfo? helper = AccessTools.Method(
                typeof(MechanicalFlightCruiseUtility),
                nameof(MechanicalFlightCruiseUtility.RampAdjustedFlightSpeed));

            if (speedField == null || helper == null)
            {
                Log.Error($"{LogPrefix}未找到速度字段或辅助方法，起飞加速曲线未应用。");
                return codes;
            }

            List<int> matches = new();
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Ldfld
                    && Equals(codes[i].operand, speedField))
                {
                    matches.Add(i);
                }
            }

            if (matches.Count != 1)
            {
                Log.Error($"{LogPrefix}TickDirectPath 中 flightCellsPerSecond 读取预期 1 处，"
                    + $"实际找到 {matches.Count} 处，起飞加速曲线未应用。");
                return codes;
            }

            int index = matches[0];
            CodeInstruction original = codes[index];
            CodeInstruction loadPawn = new(OpCodes.Ldarg_1);
            loadPawn.labels.AddRange(original.labels);
            original.labels.Clear();
            loadPawn.blocks.AddRange(original.blocks);
            original.blocks.Clear();
            codes.Insert(index, loadPawn);
            original.opcode = OpCodes.Call;
            original.operand = helper;
            return codes;
        }
    }

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

        internal static float ExhaustStretch(
            Pawn pawn,
            MechanicalFlightAuthorizationRecord record)
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
                ? MechanicalFlightCruiseUtility.TravelRamp(pawn, record)
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
            float stretch = ExhaustStretch(pawn, record);
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
    /// 保留现有推进焰绘制入口，但以“脉动 × 巡航拉伸”的缓存图形替换固定长度版本。
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
                pawn, record, bodyDrawLoc, tiltAngle);
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
