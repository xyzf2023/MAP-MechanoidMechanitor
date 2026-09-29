using System.Collections.Generic;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>太阳的分层装甲表现；技能动作由已保存的施放阶段驱动。</summary>
    [StaticConstructorOnStartup]
    internal static class SunArmorPresentation
    {
        private sealed class AnimationState
        {
            internal SunArmorPose Pose;
            internal SunArmorPose TransitionFrom;
            internal int TransitionTick = -1;
            internal int BrakeTicks;
            internal int AlignTicks;
            internal float InnerSpeed;
            internal float OuterSpeed;
            internal float FloatBlend;
            internal bool SkillActive;
            internal bool LastReplacingBody;
            internal bool NeedsGraphicsRefresh;
            internal int LastTick = -1;
            internal int PreparedFrame = -1;
            internal int PreparedTick = -1;
            internal int HealthTick = -1;
            internal float HealthFraction = 1f;
        }

        private sealed class SkillPreview
        {
            internal int Kind, Started, Deploy, Charge, Firing, Brake, Align, Close, Recovery;
            internal SunArmorPose StartPose;
            internal int Duration => Deploy + Charge + Firing + Brake + Align + Close + Recovery;
        }

        private readonly struct ArmorLayer
        {
            internal readonly Material Material;
            internal readonly Material AncientMaterial;
            internal readonly Vector2 Size;
            internal readonly Vector2 ClosedOffset;
            internal readonly Vector2 OpenOffset;
            internal readonly Vector2 ClosedScale;
            internal readonly float ClosedAngle;

            internal ArmorLayer(string textureName, Vector2 size, Vector2 closedOffset,
                Vector2 openOffset, Vector2 closedScale, float closedAngle = 0f)
            {
                Material = MaterialPool.MatFrom(
                    $"Mech/Sun/Animation/Sun{textureName}", ShaderDatabase.Transparent);
                AncientMaterial = MaterialPool.MatFrom(
                    $"Mech/SunAncient/Animation/SunAncient{textureName}", ShaderDatabase.Transparent);
                Size = size;
                ClosedOffset = closedOffset;
                OpenOffset = openOffset;
                ClosedScale = closedScale;
                ClosedAngle = closedAngle;
            }
        }

        private const float BodyBlendPortion = 0.35f;
        private const float ReferenceDrawSize = 3f;
        private const float OpenArmorRadius = 1.36f;
        private const float MotionEpsilon = 0.001f;
        private static readonly Vector2 CoreSize = PixelSize(847f, 1162f);
        private static readonly Material Core = MaterialPool.MatFrom(
            "Mech/Sun/Animation/SunInnerBody", ShaderDatabase.Transparent);
        private static readonly Material AncientCore = MaterialPool.MatFrom(
            "Mech/SunAncient/Animation/SunAncientInnerBody", ShaderDatabase.Transparent);
        // 单位网格随统一绘制尺寸缩放；Graphic_Multi 同时处理缺少西向图时的镜像。
        private static readonly Graphic ClosedBody = GraphicDatabase.Get<Graphic_Multi>(
            "Mech/Sun/Sun", ShaderDatabase.Transparent, Vector2.one, Color.white);
        private static readonly Graphic AncientClosedBody = GraphicDatabase.Get<Graphic_Multi>(
            "Mech/SunAncient/SunAncient", ShaderDatabase.Transparent, Vector2.one, Color.white);
        private static readonly Graphic AncientClosedBuilding = GraphicDatabase.Get<Graphic_Single>(
            "Mech/SunAncient/SunAncient_Building", ShaderDatabase.Transparent, Vector2.one, Color.white);
        private static readonly MaterialPropertyBlock Properties = new();
        private static readonly Dictionary<int, AnimationState> States = new();
        private static readonly Dictionary<int, SkillPreview> DebugPreviews = new();
        private static readonly HashSet<int> DebugFlightGranted = new();
        private static readonly HashSet<int> DebugFlightPendingRevoke = new();

        // 六块甲板按上、右上、右下、下、左下、左上排列。
        // 闭合姿态按静态正面图近似标定（完整画布像素坐标，含透明边距）；
        // 基础展开端为等半径、60° 间隔；各技能再叠加分组半径和角度。
        private static readonly ArmorLayer[] Armor =
        {
            new ArmorLayer("ArmorHexTop",
                PixelSize(457f, 203f),
                PixelOffset(931.5f, 525f), HexOffset(0, OpenArmorRadius),
                new Vector2(1.53f, 1.28f)),
            new ArmorLayer("ArmorHexUpperRight",
                PixelSize(338f, 413f),
                PixelOffset(1312f, 736f), HexOffset(1, OpenArmorRadius),
                new Vector2(1.18f, 1.10f), 15f),
            new ArmorLayer("ArmorHexLowerRight",
                PixelSize(337f, 426f),
                PixelOffset(1280f, 1128f), HexOffset(2, OpenArmorRadius),
                new Vector2(1.40f, 1.15f), -3f),
            new ArmorLayer("ArmorHexBottom",
                PixelSize(516f, 213f),
                PixelOffset(931.5f, 1330f), HexOffset(3, OpenArmorRadius),
                new Vector2(1.04f, 0.93f)),
            new ArmorLayer("ArmorHexLowerLeft",
                PixelSize(337f, 427f),
                PixelOffset(583f, 1128f), HexOffset(4, OpenArmorRadius),
                new Vector2(1.40f, 1.15f), 3f),
            new ArmorLayer("ArmorHexUpperLeft",
                PixelSize(339f, 413f),
                PixelOffset(551f, 736f), HexOffset(5, OpenArmorRadius),
                new Vector2(1.18f, 1.10f), -15f)
        };

        private static Vector2 PixelSize(float width, float height)
        {
            // 正面图通过透明边距调整为 1863×1862，使灯罩居中且不重采样。
            // 分层图使用同一像素比例；核心新增的一行透明边距计入 CoreSize。
            return new Vector2(width / 1863f, height / 1862f) * ReferenceDrawSize;
        }

        private static Vector2 HexOffset(int index, float radius)
        {
            float angle = index * Mathf.PI / 3f;
            return radius * new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
        }

        private static Vector2 PixelOffset(float x, float y)
        {
            // 图像 Y 向下，地图 Z 向上；灯罩中心是所有形态共用的原点。
            return PixelSize(x - 931.5f, 931f - y);
        }

        internal static void ClearAllRuntimeState()
        {
            // 仅表现缓存，不写入存档；避免切换存档后复用同一 thingID 的旧角度。
            States.Clear();
            DebugPreviews.Clear();
            DebugFlightGranted.Clear();
            DebugFlightPendingRevoke.Clear();
        }

        internal static bool IsSun(Pawn? pawn) => pawn?.def?.defName is
            "MAP_Mech_Sun" or "MAP_Mech_SunBOSS";

        internal static SunArmorPose CapturePose(Pawn pawn)
        {
            if (!IsSun(pawn)) return SunSkillAnimation.Rest(pawn);
            Prepare(pawn, force: true);
            SunArmorPose pose = States.TryGetValue(pawn.thingIDNumber, out AnimationState? state)
                ? state.Pose : SunSkillAnimation.Rest(pawn);
            // 正式技能或新的预览接管当前姿态后，旧预览不能在技能结束时重新出现。
            DebugPreviews.Remove(pawn.thingIDNumber);
            return pose;
        }

        internal static void NotifySkillEnded(Pawn pawn, SunArmorPose pose,
            float innerSpeed = 0f, float outerSpeed = 0f)
        {
            if (!IsSun(pawn)) return;
            if (!States.TryGetValue(pawn.thingIDNumber, out AnimationState? state))
                States[pawn.thingIDNumber] = state = new AnimationState();
            state.Pose = pose;
            state.SkillActive = false;
            state.PreparedFrame = -1;
            BeginReturn(state, Find.TickManager.TicksGame, innerSpeed, outerSpeed);
        }

        private static void BeginReturn(AnimationState state, int now,
            float innerSpeed = 0f, float outerSpeed = 0f)
        {
            state.TransitionFrom = state.Pose;
            state.TransitionTick = now;
            state.InnerSpeed = innerSpeed;
            state.OuterSpeed = outerSpeed;
            state.BrakeTicks = Mathf.Abs(innerSpeed) + Mathf.Abs(outerSpeed) > MotionEpsilon ? 30 : 0;
            state.AlignTicks = state.BrakeTicks > 0
                || Mathf.Abs(Mathf.DeltaAngle(state.Pose.InnerAngle, 0f)) > MotionEpsilon
                || Mathf.Abs(Mathf.DeltaAngle(state.Pose.OuterAngle, 0f)) > MotionEpsilon
                || Mathf.Abs(state.Pose.PanelTilt) > MotionEpsilon ? 30 : 0;
        }

        internal static void Prepare(Pawn pawn, bool force = false)
        {
            if (!IsSun(pawn)) return;
            SunFlightPresentation.Prepare(pawn);
            if (!States.TryGetValue(pawn.thingIDNumber, out AnimationState? state))
            {
                if (!pawn.Spawned || pawn.Dead) return;
                state = new AnimationState { Pose = SunSkillAnimation.Rest(pawn) };
                States[pawn.thingIDNumber] = state;
            }

            // 只在主线程准备姿态；并行渲染树查询不修改字典。
            int now = Find.TickManager.TicksGame;
            if (!force && state.PreparedFrame == Time.frameCount && state.PreparedTick == now) return;
            state.PreparedFrame = Time.frameCount;
            state.PreparedTick = now;
            int elapsed = state.LastTick < 0 ? 0 : Mathf.Max(0, now - state.LastTick);
            state.LastTick = now;
            if (!pawn.Spawned || pawn.Dead)
            {
                state.Pose = SunArmorPose.Rest(false);
                state.TransitionTick = -1;
                state.FloatBlend = 0f;
                state.SkillActive = false;
                DebugPreviews.Remove(pawn.thingIDNumber);
            }
            else
            {
                // 部位完整度最多每 15 游戏刻重算一次，不按渲染帧反复遍历健康列表。
                if (state.HealthTick < 0 || now < state.HealthTick || now - state.HealthTick >= 15)
                {
                    state.HealthFraction = SunLightPresentation.HealthFraction(pawn);
                    state.HealthTick = now;
                }
                SunArmorPose rest = SunSkillAnimation.Rest(pawn);
                if (DebugPreviews.TryGetValue(pawn.thingIDNumber, out SkillPreview? expired)
                    && now - expired.Started >= expired.Duration)
                {
                    DebugPreviews.Remove(pawn.thingIDNumber);
                    state.Pose = rest;
                    state.SkillActive = false;
                    state.TransitionTick = -1;
                }
                bool active = TryGetLivePose(pawn, out SunArmorPose pose, out float speed)
                    || TryGetPreviewPose(pawn, now, rest, out pose, out speed);
                if (active)
                {
                    state.Pose = pose;
                    state.InnerSpeed = speed;
                    state.OuterSpeed = speed * SunSkillAnimation.OuterSpeedFactor;
                    state.TransitionTick = -1;
                }
                else
                {
                    if (state.SkillActive)
                        BeginReturn(state, now, state.InnerSpeed, state.OuterSpeed);
                    else if (state.TransitionTick < 0 && Mathf.Abs(state.Pose.Openness - rest.Openness) > MotionEpsilon)
                        BeginReturn(state, now);
                    if (state.TransitionTick >= 0)
                    {
                        int ticks = Mathf.Max(0, now - state.TransitionTick);
                        state.Pose = SunSkillAnimation.ReturnToRest(state.TransitionFrom,
                            state.InnerSpeed, state.OuterSpeed, ticks,
                            state.BrakeTicks, state.AlignTicks, 24, rest);
                        if (ticks >= state.BrakeTicks + state.AlignTicks + 24)
                            state.TransitionTick = -1;
                    }
                    else state.Pose = rest;
                }
                state.SkillActive = active;
                state.FloatBlend = Mathf.MoveTowards(state.FloatBlend,
                    !active && state.TransitionTick < 0 && rest.Openness > 0f ? 1f : 0f, elapsed / 12f);
            }

            bool replacingBody = CanPresent(pawn) && state.Pose.Openness > MotionEpsilon;
            if (state.LastReplacingBody != replacingBody)
            {
                state.LastReplacingBody = replacingBody;
                state.NeedsGraphicsRefresh = true;
            }
        }

        internal static bool ConsumeGraphicsRefresh(Pawn pawn)
        {
            if (!States.TryGetValue(pawn.thingIDNumber, out AnimationState? state)
                || !state.NeedsGraphicsRefresh) return false;
            state.NeedsGraphicsRefresh = false;
            return true;
        }

        internal static bool HasSkillAnimation(Pawn pawn)
        {
            bool live = pawn.jobs?.curDriver switch
            {
                JobDriver_AnnihilationCannon cannon => cannon.HasAnimation,
                JobDriver_EnergyPulse pulse => pulse.HasAnimation,
                JobDriver_HighEnergyLaserBeam laser => laser.HasAnimation,
                _ => false
            };
            return live || HoldsAwakeningPose(pawn)
                || (DebugPreviews.TryGetValue(pawn.thingIDNumber, out var preview)
                && Find.TickManager.TicksGame - preview.Started < preview.Duration);
        }

        private static bool HoldsAwakeningPose(Pawn pawn) => pawn.Spawned && !pawn.Dead && !pawn.Downed
            && pawn.GetComp<CompSunBossState>()?.HasAwakeningHandoff == true
            && pawn.Awake() && !pawn.IsSelfShutdown() && !pawn.IsDeactivated();

        private static bool TryGetLivePose(Pawn pawn, out SunArmorPose pose, out float speed)
        {
            speed = 0f;
            switch (pawn.jobs?.curDriver)
            {
                case JobDriver_AnnihilationCannon cannon when cannon.HasAnimation:
                    pose = cannon.AnimationPose;
                    speed = cannon.AnimationSpeed;
                    return true;
                case JobDriver_EnergyPulse pulse when pulse.HasAnimation:
                    pose = pulse.AnimationPose;
                    return true;
                case JobDriver_HighEnergyLaserBeam laser when laser.HasAnimation:
                    pose = laser.AnimationPose;
                    return true;
            }
            // 真实技能优先；生成后等待首个脉冲期间保持建筑末帧，不先闭合再展开。
            // 标记与灯效相位存于 BOSS 组件，读档及首次离屏绘制都能恢复。
            if (HoldsAwakeningPose(pawn))
            {
                pose = SunSkillAnimation.Awakening(1f);
                return true;
            }
            pose = default;
            return false;
        }

        private static bool TryGetPreviewPose(Pawn pawn, int now, SunArmorPose rest,
            out SunArmorPose pose, out float speed)
        {
            pose = default;
            speed = 0f;
            if (!DebugPreviews.TryGetValue(pawn.thingIDNumber, out SkillPreview? preview)) return false;
            int ticks = Mathf.Max(0, now - preview.Started);
            if (preview.Kind == 0)
            {
                if (ticks < preview.Deploy)
                    pose = SunSkillAnimation.CannonOpening(preview.StartPose, ticks, preview.Deploy);
                else if (ticks < preview.Deploy + preview.Charge)
                {
                    int charge = ticks - preview.Deploy;
                    pose = SunSkillAnimation.CannonCharge(charge, preview.Charge);
                    speed = SunSkillAnimation.CannonSpeed(SunSkillAnimation.Progress(charge, preview.Charge));
                }
                else
                {
                    int recovery = ticks - preview.Deploy - preview.Charge;
                    pose = SunSkillAnimation.CannonRecovery(recovery, preview.Charge,
                        preview.Brake, preview.Align, preview.Close, rest);
                    speed = SunSkillAnimation.CannonMaxSpeed
                        * (1f - SunSkillAnimation.Progress(recovery, preview.Brake));
                }
            }
            else if (preview.Kind == 1)
                pose = ticks < preview.Charge
                    ? SunSkillAnimation.PulseCharge(preview.StartPose, SunSkillAnimation.Progress(ticks, preview.Charge))
                    : SunSkillAnimation.PulseRecovery(ticks - preview.Charge, preview.Recovery, rest);
            else if (ticks < preview.Charge + preview.Firing)
                pose = SunSkillAnimation.Laser(preview.StartPose, SunSkillAnimation.Progress(ticks, preview.Charge),
                    preview.Charge, ticks >= preview.Charge, Mathf.Max(0, ticks - preview.Charge));
            else
                pose = SunArmorPose.Lerp(SunSkillAnimation.Laser(preview.StartPose, 1f, preview.Charge, true, preview.Firing),
                    rest, SunSkillAnimation.Smooth(SunSkillAnimation.Progress(
                        ticks - preview.Charge - preview.Firing, preview.Recovery)));
            return true;
        }

        internal static bool ReplacesBody(Pawn? pawn)
        {
            // 尸体等入口可能直接调用 RenderPawnAt，不经过 Prepare；仍须实时检查可见资格。
            return IsSun(pawn) && CanPresent(pawn!)
                && States.TryGetValue(pawn!.thingIDNumber, out var state)
                && state.LastReplacingBody;
        }

        private static bool CanPresent(Pawn pawn) => pawn.Spawned && !pawn.Dead
            && !pawn.IsHiddenFromPlayer() && Find.UIRoot?.HideMotes != true;

        private static bool TryGetBuildingAwakening(Thing building, out float progress)
        {
            progress = 0f;
            if (!building.Spawned || building.Destroyed || building.Map != Find.CurrentMap
                || building.def.drawerType != DrawerType.RealtimeOnly
                || building.Position.Fogged(building.Map) || Find.UIRoot?.HideMotes == true) return false;
            progress = building.Map.GetComponent<MapComponent_SunBossArena>().ActivationProgressFor(building);
            return progress > 0f;
        }

        internal static bool ReplacesBuilding(Thing building) => TryGetBuildingAwakening(building, out _);

        internal static void DrawBuilding(Thing building, Vector3 drawLoc)
        {
            if (!TryGetBuildingAwakening(building, out float progress)) return;
            Vector2 drawScale = (building.def.graphicData?.drawSize ?? Vector2.one * ReferenceDrawSize)
                / ReferenceDrawSize;
            DrawBodyLayers(drawLoc, drawScale, SunSkillAnimation.Awakening(progress), true,
                AncientClosedBuilding, Rot4.South, building.thingIDNumber);
            SunLightPresentation.DrawBuilding(building, true, drawLoc);
        }

        private static Vector2 AnimationDrawScale(Pawn pawn)
        {
            Vector2 drawSize = pawn.ageTracker?.CurKindLifeStage?.bodyGraphicData?.drawSize
                ?? new Vector2(ReferenceDrawSize, ReferenceDrawSize);
            return drawSize / ReferenceDrawSize;
        }

        internal static int ArmorCount => Armor.Length;

        private static float ArmorOpenProgress(SunArmorPose pose) => Mathf.SmoothStep(0f, 1f,
            Mathf.InverseLerp(BodyBlendPortion * 0.5f, 1f, pose.Openness));

        private static Vector2 ArmorOffset(int seed, SunArmorPose pose, int index,
            float open, float floatBlend)
        {
            ArmorLayer layer = Armor[index];
            bool inner = index % 2 == 0;
            float radius = inner ? pose.InnerRadius : pose.OuterRadius;
            Vector2 offset = Vector2.Lerp(layer.ClosedOffset, layer.OpenOffset * radius, open);
            // 漂浮只用于待机，技能发射端与绘制端共用甲板位置计算。
            float floatPhase = (Find.TickManager.TicksGame
                + seed * 11) * 0.025f + index * 1.17f;
            float floatWeight = open * floatBlend;
            offset += layer.OpenOffset.normalized * (Mathf.Sin(floatPhase) * 0.018f * floatWeight);
            offset.y += Mathf.Cos(floatPhase * 0.83f) * 0.012f * floatWeight;
            return RotateClockwise(offset, inner ? pose.InnerAngle : pose.OuterAngle);
        }

        private static Vector2 FlightArmorOffset(int seed, SunArmorPose pose, int index,
            float open, float floatBlend, SunFlightPresentation.Pose flight)
        {
            Vector2 offset = ArmorOffset(seed, pose, index, open, floatBlend * flight.FloatFactor);
            offset = RotateClockwise(offset, flight.LayoutAngle(open));
            return flight.ApplyToOffset(offset, open);
        }

        internal static Vector3 LaserEmissionOffset(Pawn pawn, SunArmorPose pose, int index)
        {
            // 武器按 tick 更新时也准备姿态；与绘制共用漂浮、倾斜、压缩及制动位移。
            Prepare(pawn);
            float floatBlend = States.TryGetValue(pawn.thingIDNumber, out var state) ? state.FloatBlend : 0f;
            Vector2 offset = FlightArmorOffset(pawn.thingIDNumber, pose, index, ArmorOpenProgress(pose),
                floatBlend, SunFlightPresentation.Current(pawn));
            offset = Vector2.Scale(offset, AnimationDrawScale(pawn));
            return new Vector3(offset.x, 0f, offset.y);
        }

        internal static void Draw(Pawn pawn, Vector3 drawLoc, Rot4? rotOverride = null)
        {
            if (!IsSun(pawn) || !CanPresent(pawn)
                || !States.TryGetValue(pawn.thingIDNumber, out AnimationState? state))
            {
                return;
            }

            CleanupDebugFlightAuthorization(pawn);

            SunArmorPose pose = state.Pose;
            float staticAlpha = StaticBodyAlpha(pose);
            SunFlightPresentation.Pose flight = SunFlightPresentation.Current(pawn);
            PawnRenderer renderer = pawn.Drawer.renderer;
            bool standing = pawn.GetPosture() == PawnPosture.Standing;
            // 与原版身体共用躺卧角度和朝向；飞行倾斜只在此基础上叠加一次。
            float postureAngle = standing ? 0f : renderer.BodyAngle(PawnRenderFlags.None);
            float bodyAngle = postureAngle + flight.BodyAngle;
            Vector2 drawScale = AnimationDrawScale(pawn);
            Vector3 lampCenter = SunDrawUtility.BreathingLightPosition(drawLoc);
            Rot4 facing = rotOverride ?? (standing || pawn.Crawling
                ? pawn.Rotation : renderer.LayingFacing());
            // 按机体 Def 选择缓存材质，确保整图、装甲和光效在交接期间配色一致。
            bool ancient = pawn.def.defName == "MAP_Mech_SunBOSS";
            Graphic closedBody = ancient ? AncientClosedBody : ClosedBody;

            // 交接结束后完全使用正面分层贴图；侧/背面通过对应方向的整图渐变接入。
            if (state.LastReplacingBody)
                DrawBodyLayers(drawLoc, drawScale, pose, ancient, closedBody, facing,
                    pawn.thingIDNumber, state.FloatBlend, flight, postureAngle);

            // 机械体和建筑转换共用灯效曲线；侧/背面只在展开露出核心后显示。
            float lightVisibility = facing == Rot4.South ? 1f : 1f - staticAlpha;
            SunLightPresentation.DrawPawn(pawn, lampCenter, drawScale, bodyAngle,
                lightVisibility, pose, state.HealthFraction);

            DrawDebugCharge(pawn, lampCenter);
        }

        private static float StaticBodyAlpha(SunArmorPose pose) => 1f - Mathf.SmoothStep(0f, 1f,
            Mathf.InverseLerp(BodyBlendPortion * 0.5f, BodyBlendPortion, pose.Openness));

        // 建筑与机械体共用装甲位置、材质和整图交接；建筑无需提前生成临时 Pawn。
        private static void DrawBodyLayers(Vector3 drawLoc, Vector2 drawScale, SunArmorPose pose,
            bool ancient, Graphic closedBody, Rot4 facing, int seed, float floatBlend = 0f,
            SunFlightPresentation.Pose flight = default, float postureAngle = 0f)
        {
            float open = ArmorOpenProgress(pose);
            // 先在不透明整图下淡入零件，再淡出整图，避免交接中段出现透明缺口。
            float layerAlpha = Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01(pose.Openness / (BodyBlendPortion * 0.5f)));
            float staticAlpha = StaticBodyAlpha(pose);
            float bodyAngle = postureAngle + flight.BodyAngle;
            Vector3 layoutCenter = drawLoc;
            layoutCenter.y += 0.028f;
            Color layerColor = new Color(1f, 1f, 1f, layerAlpha);
            if (layerAlpha > MotionEpsilon)
            {
                DrawLayer(ancient ? AncientCore : Core, layoutCenter, Vector3.zero, CoreSize,
                    drawScale, bodyAngle, layerColor);

                for (int i = 0; i < Armor.Length; i++)
                {
                    ArmorLayer layer = Armor[i];
                    // 0/2/4：上、右下、左下；1/3/5：右上、下、左上。
                    bool inner = i % 2 == 0;
                    float orbitAngle = inner ? pose.InnerAngle : pose.OuterAngle;
                    Vector2 offset = FlightArmorOffset(seed, pose, i, open, floatBlend, flight);
                    Vector2 size = Vector2.Scale(layer.Size,
                        Vector2.Lerp(layer.ClosedScale, Vector2.one, open));
                    float layerAngle = flight.LayoutAngle(open) + orbitAngle
                        + Mathf.LerpAngle(layer.ClosedAngle, 0f, open)
                        + pose.PanelTilt * (inner ? 1f : -1f) * open;

                    Vector3 layerOffset = new Vector3(offset.x, 0.004f + i * 0.001f, offset.y);
                    // 倒地时整体跟随身体旋转，飞行偏移已在位置计算中应用。
                    DrawLayer(ancient ? layer.AncientMaterial : layer.Material, layoutCenter, layerOffset, size,
                        drawScale, postureAngle + layerAngle, layerColor, layoutAngle: postureAngle);
                }
            }

            if (staticAlpha > MotionEpsilon)
                DrawLayer(closedBody.MatAt(facing), layoutCenter,
                    new Vector3(0f, 0.011f, 0f), Vector2.one * ReferenceDrawSize,
                    drawScale, bodyAngle, new Color(1f, 1f, 1f, staticAlpha),
                    mesh: closedBody.MeshAt(facing));
        }

        private static Vector2 RotateClockwise(Vector2 point, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float sin = Mathf.Sin(radians);
            float cos = Mathf.Cos(radians);
            return new Vector2(point.x * cos + point.y * sin,
                -point.x * sin + point.y * cos);
        }

        private static void DrawDebugCharge(Pawn pawn, Vector3 lampCenter)
        {
            if (TryGetLivePose(pawn, out _, out _)
                || !DebugPreviews.TryGetValue(pawn.thingIDNumber, out SkillPreview? preview)
                || preview.Kind != 0) return;
            int chargeTicks = Find.TickManager.TicksGame - preview.Started - preview.Deploy;
            if (chargeTicks < 0 || chargeTicks >= preview.Charge) return;
            IntVec3 target = pawn.Position + new IntVec3(0, 0, -10);
            AnnihilationCannonVisuals.DrawWarmupPart(lampCenter, target,
                SunSkillAnimation.Progress(chargeTicks, preview.Charge), chargeTicks / 60f, 1, 1f);
        }

        [DebugAction("MAP-机械族机械师", "太阳湮灭炮动作：点击太阳预览（无伤害）",
            false, false, false, false, false, 0, false,
            actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.Playing)]
        private static void DebugPreviewAtMouse() => BeginPreviewAtMouse(0);

        [DebugAction("MAP-机械族机械师", "太阳能量脉冲动作：点击太阳预览（无伤害）",
            false, false, false, false, false, 0, false,
            actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.Playing)]
        private static void DebugPreviewPulseAtMouse() => BeginPreviewAtMouse(1);

        [DebugAction("MAP-机械族机械师", "太阳高能激光动作：点击太阳预览（无伤害）",
            false, false, false, false, false, 0, false,
            actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.Playing)]
        private static void DebugPreviewLaserAtMouse() => BeginPreviewAtMouse(2);

        private static void BeginPreviewAtMouse(int kind)
        {
            Map? map = Find.CurrentMap;
            Pawn? pawn = map == null ? null : UI.MouseCell().GetFirstPawn(map);
            if (!IsSun(pawn))
            {
                Messages.Message("请点击一个太阳机械体。", MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (TryGetLivePose(pawn!, out _, out _))
            {
                Messages.Message("请等待太阳当前技能结束后再预览。", MessageTypeDefOf.RejectInput, false);
                return;
            }
            var preview = new SkillPreview
            {
                Kind = kind,
                Started = Find.TickManager.TicksGame,
                StartPose = CapturePose(pawn!)
            };
            if (kind == 0)
            {
                CompAnnihilationCannon? cannon = pawn!.GetComp<CompAnnihilationCannon>();
                preview.Deploy = Mathf.Max(1, cannon?.Props.deployTicks ?? 60);
                preview.Charge = cannon?.WarmupTicksFor(pawn) ?? 300;
                preview.Brake = Mathf.Max(1, cannon?.Props.brakeTicks ?? 36);
                preview.Align = Mathf.Max(1, cannon?.Props.alignTicks ?? 30);
                preview.Close = Mathf.Max(1, cannon?.Props.closeTicks ?? 24);
            }
            else if (kind == 1)
            {
                CompEnergyPulse? pulse = pawn!.GetComp<CompEnergyPulse>();
                preview.Charge = Mathf.Max(1, pulse?.Props.warmupTicks ?? 180);
                preview.Recovery = Mathf.Max(0, pulse?.Props.recoveryTicks ?? 42);
            }
            else
            {
                CompHighEnergyLaserBeam? laser = pawn!.GetComp<CompHighEnergyLaserBeam>();
                preview.Charge = laser?.WarmupTicks ?? CompHighEnergyLaserBeam.DefaultWarmupTicks;
                preview.Firing = Mathf.Max(1, laser?.Props.durationTicks ?? 300);
                preview.Recovery = Mathf.Max(0, laser?.Props.recoveryTicks ?? 36);
            }
            DebugPreviews[pawn!.thingIDNumber] = preview;
            if (States.TryGetValue(pawn.thingIDNumber, out AnimationState? state))
                state.PreparedFrame = -1;
        }

        [DebugAction("MAP-机械族机械师", "太阳悬浮测试：点击太阳切换起飞/降落",
            false, false, false, false, false, 0, false,
            actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.Playing)]
        private static void DebugToggleFlightAtMouse()
        {
            Map? map = Find.CurrentMap;
            Pawn? pawn = map == null ? null : UI.MouseCell().GetFirstPawn(map);
            if (!IsSun(pawn))
            {
                Messages.Message("请点击一个太阳机械体。", MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (GameComponent_MechanicalFlightRegistry.TryGetRecord(
                    pawn, out MechanicalFlightAuthorizationRecord? activeRecord)
                && activeRecord?.IsRuntimeActive == true)
            {
                if (MechanicalFlightUtility.TryBeginLanding(pawn, showMessage: true))
                {
                    if (DebugFlightGranted.Contains(pawn!.thingIDNumber))
                        DebugFlightPendingRevoke.Add(pawn.thingIDNumber);
                    Messages.Message("太阳开始降落；落地后将移除临时测试授权。",
                        pawn, MessageTypeDefOf.TaskCompletion, false);
                }
                return;
            }

            bool granted = GameComponent_MechanicalFlightRegistry.TryAuthorize(
                pawn, source: MechanicalFlightAuthorizationSource.Debug);
            if (granted) DebugFlightGranted.Add(pawn!.thingIDNumber);

            if (MechanicalFlightUtility.TryBeginDebugTakeoff(pawn))
            {
                if (pawn!.Faction == Faction.OfPlayer && pawn.drafter != null && !pawn.Drafted)
                    pawn.drafter.Drafted = true;
                Messages.Message("太阳已进入悬浮测试；再次使用同一操作可降落。",
                    pawn, MessageTypeDefOf.TaskCompletion, false);
                return;
            }

            if (granted)
            {
                GameComponent_MechanicalFlightRegistry.TryRemoveAuthorizationSource(
                    pawn, MechanicalFlightAuthorizationSource.Debug);
                DebugFlightGranted.Remove(pawn!.thingIDNumber);
            }
            Messages.Message("太阳当前无法起飞，请检查屋顶、失能或飞行冷却状态。",
                pawn, MessageTypeDefOf.RejectInput, false);
        }

        private static void CleanupDebugFlightAuthorization(Pawn pawn)
        {
            int id = pawn.thingIDNumber;
            if (!DebugFlightPendingRevoke.Contains(id)
                || (GameComponent_MechanicalFlightRegistry.TryGetRecord(
                        pawn, out MechanicalFlightAuthorizationRecord? record)
                    && record?.IsRuntimeActive == true))
            {
                return;
            }

            GameComponent_MechanicalFlightRegistry.TryRemoveAuthorizationSource(
                pawn, MechanicalFlightAuthorizationSource.Debug);
            DebugFlightPendingRevoke.Remove(id);
            DebugFlightGranted.Remove(id);
        }

        private static void DrawLayer(Material material, Vector3 origin, Vector3 offset,
            Vector2 size, Vector2 drawScale, float angle, Color color,
            float layoutAngle = 0f, Mesh? mesh = null)
        {
            // 飞行布局已由共享函数计算；此处只额外叠加躺卧姿态，不重复飞行倾斜。
            Vector2 scaledOffset = RotateClockwise(
                new Vector2(offset.x * drawScale.x, offset.z * drawScale.y), layoutAngle);
            Vector3 center = origin + new Vector3(scaledOffset.x, offset.y, scaledOffset.y);
            Vector2 scaledSize = Vector2.Scale(size, drawScale);
            Properties.Clear();
            Properties.SetColor(ShaderPropertyIDs.Color, color);
            Matrix4x4 matrix = Matrix4x4.TRS(center,
                Quaternion.AngleAxis(angle, Vector3.up),
                new Vector3(scaledSize.x, 1f, scaledSize.y));
            Graphics.DrawMesh(mesh ?? MeshPool.plane10, matrix, material, 0, null, 0, Properties);
        }
    }

    /// <summary>呼吸灯与武器共用的绘制锚点；独立于材质初始化和装甲动画状态。</summary>
    internal static class SunDrawUtility
    {
        internal static Vector3 BreathingLightPosition(Vector3 pawnDrawPos)
        {
            // 两种身体贴图的灯罩均已居中，只提高绘制层级以覆盖核心。
            return pawnDrawPos + new Vector3(0f, 0.04f, 0f);
        }
    }

    // 太阳是非人形机械体，实际身体节点不保证使用 PawnRenderNodeWorker_Body。
    // 在所有 RenderNodeWorker 共用入口按 Body 标签过滤，才能可靠移除后垫着的正面整机图。
    [HarmonyPatch(typeof(PawnRenderNodeWorker), nameof(PawnRenderNodeWorker.CanDrawNow))]
    internal static class SunArmorBodyRenderPatch
    {
        [HarmonyPostfix]
        private static void Postfix(PawnRenderNode node, PawnDrawParms parms,
            ref bool __result)
        {
            if (__result && !parms.Portrait
                && !parms.flags.FlagSet(PawnRenderFlags.Portrait)
                && node.Props.tagDef == PawnRenderNodeTagDefOf.Body
                && SunArmorPresentation.ReplacesBody(parms.pawn))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(PawnRenderer), nameof(PawnRenderer.DynamicDrawPhaseAt))]
    internal static class SunArmorDynamicDrawPatch
    {
        private static readonly AccessTools.FieldRef<PawnRenderer, Pawn> PawnField =
            AccessTools.FieldRefAccess<PawnRenderer, Pawn>("pawn");

        [HarmonyPrefix]
        private static void Prefix(PawnRenderer __instance, DrawPhase phase)
        {
            if (phase != DrawPhase.EnsureInitialized && phase != DrawPhase.Draw)
                return;

            Pawn pawn = PawnField(__instance);
            SunArmorPresentation.Prepare(pawn);
            // 在原版预绘制前更新 Body 的隐藏状态，避免交接首尾晚一帧刷新造成重影或缺图。
            if (SunArmorPresentation.ConsumeGraphicsRefresh(pawn))
                __instance.SetAllGraphicsDirty();
        }

        [HarmonyPostfix]
        private static void Postfix(PawnRenderer __instance, DrawPhase phase, Vector3 drawLoc,
            Rot4? rotOverride)
        {
            if (phase == DrawPhase.Draw)
                SunArmorPresentation.Draw(PawnField(__instance), drawLoc, rotOverride);
        }
    }
}
