using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>建筑转换的灯效与 BOSS 苏醒装甲；进度由原有转换系统保存。</summary>
    public sealed class CompProperties_SunBuildingLight : CompProperties
    {
        public bool ancient;
        public CompProperties_SunBuildingLight() => compClass = typeof(CompSunBuildingLight);
    }

    public sealed class CompSunBuildingLight : ThingComp
    {
        private bool Ancient => ((CompProperties_SunBuildingLight)props).ancient;

        public override System.Collections.Generic.IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra()) yield return gizmo;
            if (!((CompProperties_SunBuildingLight)props).ancient || !DebugSettings.ShowDevGizmos || !(parent is Building core)
                || core.Destroyed || !core.Spawned) yield break;

            MapComponent_SunBossArena arena = core.Map.GetComponent<MapComponent_SunBossArena>();
            Command_Action command = new Command_Action
            {
                defaultLabel = "DEV：激活",
                defaultDesc = "发送反应堆异常信件，并启动与殖民者靠近时相同的苏醒及设施激活流程。",
                action = () =>
                {
                    if (DebugSettings.ShowDevGizmos) arena.TryStartDevActivation(core);
                }
            };
            yield return command;
        }

        public override bool DontDrawParent() => Ancient && SunArmorPresentation.ReplacesBuilding(parent);

        public override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            base.DrawAt(drawLoc, flip);
            if (Ancient) SunArmorPresentation.DrawBuilding(parent, drawLoc);
        }

        public override void PostDraw()
        {
            base.PostDraw();
            // BOSS 灯效随分层装甲统一绘制，避免同一帧重复叠加。
            if (!Ancient) SunLightPresentation.DrawBuilding(parent, false);
        }
    }

    /// <summary>太阳两种外观共用的核心灯罩与辉光；曲线只读取游戏刻、健康和转换进度。</summary>
    [StaticConstructorOnStartup]
    internal static class SunLightPresentation
    {
        private const float ReferenceDrawSize = 3f;
        private const float LowHealthThreshold = 0.35f;
        private const int BreathingPeriodTicks = 240;
        private const int FlickerCycleTicks = 180;
        private static readonly Vector2 LightSize = new Vector2(171f / 1863f, 166f / 1862f) * ReferenceDrawSize;
        private static readonly Color DormantColor = new Color(0.32f, 0.34f, 0.36f);
        private static readonly Color SunColor = new Color(0.16f, 0.62f, 1f);
        private static readonly Color BossHealthyColor = new Color(1f, 0.44f, 0.055f);
        private static readonly Color BossCriticalColor = new Color(1f, 0.035f, 0.012f);
        // 灯罩使用透明覆盖材质，才能盖住整机图的白色灯面并真正变色、变暗；辉光独立叠加。
        private static readonly Material Light = MaterialPool.MatFrom(
            "Mech/Sun/Animation/SunCoreLight", ShaderDatabase.Transparent);
        private static readonly Material AncientLight = MaterialPool.MatFrom(
            "Mech/SunAncient/Animation/SunAncientCoreLight", ShaderDatabase.Transparent);
        private static readonly Material Halo = MaterialPool.MatFrom(
            "Mech/Sun/Animation/SunCoreLight", ShaderDatabase.MoteGlow);
        private static readonly Material AncientHalo = MaterialPool.MatFrom(
            "Mech/SunAncient/Animation/SunAncientCoreLight", ShaderDatabase.MoteGlow);
        private static readonly MaterialPropertyBlock Properties = new();

        // 机体灯效和 BOSS 仪表共用完整度配色，避免两处对同一结构值显示不同状态。
        internal static Color BossTint(float health) =>
            Color.Lerp(BossHealthyColor, BossCriticalColor, SunSkillAnimation.Smooth(1f - health));

        internal static float HealthFraction(Pawn pawn)
        {
            CompSunBossState? boss = pawn.GetComp<CompSunBossState>();
            return boss != null
                ? Mathf.Clamp01(boss.Structure / Mathf.Max(1f, boss.MaxStructure))
                : MechPartDurabilityUtility.GetStructuralIntegrity(pawn);
        }

        internal static void DrawPawn(Pawn pawn, Vector3 center, Vector2 drawScale,
            float angle, float visibility, SunArmorPose pose, float health)
        {
            bool active = pawn.Awake() && !pawn.IsSelfShutdown() && !pawn.IsDeactivated();
            float power = active ? 1f : 0f;
            if (GameComponent_MechBuildingConversionQueue.IsConversionQueued(pawn))
                power = 0f;
            else if (pawn.jobs?.curDriver is JobDriver_MechConvertToBuilding conversion)
                power *= 1f - SunSkillAnimation.Smooth(conversion.ConversionProgress);

            Draw(center, drawScale, angle, visibility, pawn.def.defName == "MAP_Mech_SunBOSS",
                pawn.GetComp<CompSunBossState>()?.LightSeed ?? pawn.thingIDNumber,
                health, power, pose.Glow, pose.Flash);
        }

        internal static void DrawBuilding(Thing building, bool ancient, Vector3? drawLoc = null)
        {
            if (!building.Spawned || building.Destroyed || building.Map != Find.CurrentMap
                || building.Position.Fogged(building.Map) || Find.UIRoot?.HideMotes == true) return;

            float progress = ancient
                ? building.Map.GetComponent<MapComponent_SunBossArena>().ActivationProgressFor(building)
                : building.TryGetComp<CompMechBuildingForm>()?.RestoreProgress ?? 0f;
            if (progress <= 0f) return;

            // 玩家建筑与其保存的源 Pawn 共用相位，转换交接时不会重新开始一次呼吸或受损闪烁。
            int seed = building.TryGetComp<CompMechFormCarrier>()?.SourcePawn?.thingIDNumber
                ?? building.thingIDNumber;
            float health = building.def.useHitPoints
                ? Mathf.Clamp01((float)building.HitPoints / Mathf.Max(1, building.MaxHitPoints)) : 1f;
            float power = ancient ? StartupPower(progress, seed) : SunSkillAnimation.Smooth(progress);
            SunArmorPose pose = ancient ? SunSkillAnimation.Awakening(progress) : default;
            Vector2 drawScale = (building.def.graphicData?.drawSize ?? Vector2.one * ReferenceDrawSize)
                / ReferenceDrawSize;
            Draw(SunDrawUtility.BreathingLightPosition(drawLoc ?? building.DrawPos), drawScale, 0f,
                SunSkillAnimation.Smooth(Mathf.Clamp01(progress * 10f)), ancient, seed, health, power,
                pose.Glow, pose.Flash);
        }

        private static float StartupPower(float progress, int seed)
        {
            // 外推期间由短促闪烁过渡到稳定通电，之后随完整展开继续增强辉光。
            float ticks = SunSkillAnimation.AwakeningElapsed(progress);
            float unstable = 1f - SunSkillAnimation.Smooth(Mathf.InverseLerp(
                SunSkillAnimation.AwakeningPushStartTick, SunSkillAnimation.AwakeningPushEndTick, ticks));
            float sample = Noise(seed, Mathf.FloorToInt(ticks) / 3, 17);
            float flicker = sample < 0.38f ? 0.04f + sample * 0.4f : 0.65f + sample * 0.35f;
            return SunSkillAnimation.Smooth(ticks / SunSkillAnimation.AwakeningPushEndTick)
                * Mathf.Lerp(1f, flicker, unstable);
        }

        private static float Breathing(int now, int seed)
        {
            float phase = ((now % BreathingPeriodTicks) + seed % BreathingPeriodTicks)
                * (2f * Mathf.PI / BreathingPeriodTicks);
            float ripple = ((now % 73) + seed % 73) * (2f * Mathf.PI / 73f);
            return 0.94f + Mathf.Sin(phase) * 0.035f + Mathf.Sin(ripple) * 0.005f;
        }

        private static float DamageFlicker(int now, int seed, float health)
        {
            if (health >= LowHealthThreshold) return 1f;
            float severity = Mathf.Clamp01(1f - health / LowHealthThreshold);
            int shifted = now + (seed & 0x7FFF);
            int cycle = shifted / FlickerCycleTicks;
            int local = shifted % FlickerCycleTicks;
            // 每个窗口只有一次概率性的短闪烁簇，其余时间保持正常；越残损越频繁、越暗。
            if (Noise(seed, cycle, 31) >= Mathf.Lerp(0.28f, 0.8f, severity)) return 1f;
            int start = 22 + Mathf.FloorToInt(Noise(seed, cycle, 53) * 110f);
            int duration = 14 + Mathf.FloorToInt(severity * 16f);
            if (local < start || local >= start + duration) return 1f;
            float sample = Noise(seed, cycle * 61 + (local - start) / 3, 97);
            return sample > 0.7f ? 1f
                : 1f - Mathf.Lerp(0.3f, 0.92f, severity) * Mathf.Lerp(0.65f, 1f, sample / 0.7f);
        }

        private static float Noise(int seed, int index, int salt)
        {
            // 不调用游戏 Rand；暂停、离屏、读档或渲染次数均不改变战斗随机序列。
            unchecked
            {
                uint value = (uint)seed * 747796405u + (uint)index * 2891336453u + (uint)salt;
                value = (value ^ (value >> 16)) * 2246822519u;
                value = (value ^ (value >> 13)) * 3266489917u;
                return ((value ^ (value >> 16)) & 0x00FFFFFFu) / 16777215f;
            }
        }

        private static void Draw(Vector3 center, Vector2 drawScale, float angle, float visibility,
            bool ancient, int seed, float health, float power, float glow, float flash)
        {
            if (visibility <= 0.001f) return;
            int now = Find.TickManager.TicksGame;
            glow = Mathf.Clamp01(glow);
            flash = Mathf.Clamp01(flash);
            // 技能只提高灯效亮度，受损闪烁和转换断电最终作用于灯罩及全部辉光。
            float intensity = Mathf.Clamp01(power) * Breathing(now, seed) * DamageFlicker(now, seed, health);
            Color tint = ancient
                ? BossTint(health)
                : SunColor;
            Color charged = Color.Lerp(tint, Color.white, glow * 0.65f);
            charged = Color.Lerp(charged, Color.white, flash);
            Color lamp = Color.Lerp(DormantColor, charged, intensity);
            lamp.a = visibility;
            Vector2 size = Vector2.Scale(LightSize, drawScale);
            DrawLayer(ancient ? AncientLight : Light, center, size, angle, lamp);
            if (intensity <= 0.001f) return;

            Color halo = Color.Lerp(tint, Color.white, Mathf.Clamp01(glow + flash) * 0.55f);
            halo.a = Mathf.Clamp01(0.22f + 0.22f * glow + 0.4f * flash) * intensity * visibility;
            center.y += 0.002f;
            DrawLayer(ancient ? AncientHalo : Halo, center,
                size * (1.4f + glow * 0.35f + flash * 0.8f), angle, halo);
        }

        private static void DrawLayer(Material material, Vector3 center, Vector2 size, float angle, Color color)
        {
            Properties.Clear();
            Properties.SetColor(ShaderPropertyIDs.Color, color);
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(center,
                Quaternion.AngleAxis(angle, Vector3.up), new Vector3(size.x, 1f, size.y)),
                material, 0, null, 0, Properties);
        }
    }
}
