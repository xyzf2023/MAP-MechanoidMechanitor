using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>可保存的动作起点；技能位移与身体贴图交接分别计算。</summary>
    internal struct SunArmorPose
    {
        internal float Openness, InnerAngle, OuterAngle;
        internal float InnerRadius, OuterRadius, PanelTilt, Glow, Flash;

        internal static SunArmorPose Rest(bool open) => new SunArmorPose
        {
            Openness = open ? 1f : 0f,
            InnerRadius = 1f,
            OuterRadius = 1f
        };

        internal static SunArmorPose Lerp(SunArmorPose from, SunArmorPose to, float t)
        {
            t = Mathf.Clamp01(t);
            return new SunArmorPose
            {
                Openness = Mathf.Lerp(from.Openness, to.Openness, t),
                InnerAngle = Mathf.LerpAngle(from.InnerAngle, to.InnerAngle, t),
                OuterAngle = Mathf.LerpAngle(from.OuterAngle, to.OuterAngle, t),
                InnerRadius = Mathf.Lerp(from.InnerRadius, to.InnerRadius, t),
                OuterRadius = Mathf.Lerp(from.OuterRadius, to.OuterRadius, t),
                PanelTilt = Mathf.Lerp(from.PanelTilt, to.PanelTilt, t),
                Glow = Mathf.Lerp(from.Glow, to.Glow, t),
                Flash = Mathf.Lerp(from.Flash, to.Flash, t)
            };
        }

        internal void ExposeData(string prefix)
        {
            Scribe_Values.Look(ref Openness, prefix + "Open");
            Scribe_Values.Look(ref InnerAngle, prefix + "InnerAngle");
            Scribe_Values.Look(ref OuterAngle, prefix + "OuterAngle");
            Scribe_Values.Look(ref InnerRadius, prefix + "InnerRadius", 1f);
            Scribe_Values.Look(ref OuterRadius, prefix + "OuterRadius", 1f);
            Scribe_Values.Look(ref PanelTilt, prefix + "PanelTilt");
            Scribe_Values.Look(ref Glow, prefix + "Glow");
            Scribe_Values.Look(ref Flash, prefix + "Flash");
        }
    }

    /// <summary>纯动作曲线：输入技能保存的进度，离屏、变速和读档均不依赖渲染累计。</summary>
    internal static class SunSkillAnimation
    {
        internal const float CannonMaxSpeed = 8.5f; // 度 / 游戏刻。
        internal const float OuterSpeedFactor = -0.8f;
        private const float LaserRotationSpeed = 0.12f; // 度 / 游戏刻；正常速度下每秒 7.2°。
        // 基准半径 1.36；两圈实际半径为 1.15 / 2.0，给反转甲片留出径向间隙。
        internal const float CannonInnerRadius = 1.15f;
        internal const float CannonOuterRadius = 2f;

        internal static float Smooth(float progress) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress));
        internal static float Progress(int ticks, int duration) => duration > 0
            ? Mathf.Clamp01((float)ticks / duration) : 1f;

        internal static SunArmorPose Rest(Pawn pawn)
        {
            bool open = GameComponent_MechanicalFlightRegistry.TryGetRecord(pawn, out var record)
                && record != null && (record.IsCruising
                    || record.Phase == MechanicalFlightPhase.EmergencyApproach
                    || record.Phase == MechanicalFlightPhase.FusionAscent);
            return SunArmorPose.Rest(open);
        }

        internal static float CannonSpeed(float progress) => CannonMaxSpeed * Mathf.Pow(Mathf.Clamp01(progress), 1.5f);

        internal static SunArmorPose CannonCharge(int ticks, int duration)
        {
            float p = Progress(ticks, duration);
            // 对速度曲线积分，不能用“当前速度 × 已经过时间”计算角度。
            float angle = CannonMaxSpeed * Mathf.Max(1, duration) * Mathf.Pow(p, 2.5f) / 2.5f;
            return new SunArmorPose
            {
                Openness = 1f,
                InnerAngle = angle,
                OuterAngle = angle * OuterSpeedFactor,
                InnerRadius = CannonInnerRadius / 1.36f,
                OuterRadius = CannonOuterRadius / 1.36f,
                Glow = 0.15f + 0.85f * p * p
            };
        }

        internal static SunArmorPose CannonOpening(SunArmorPose start, int ticks, int duration) =>
            SunArmorPose.Lerp(start, CannonCharge(0, 1), Smooth(Progress(ticks, duration)));

        internal static SunArmorPose ReturnToRest(SunArmorPose start, float innerSpeed,
            float outerSpeed, int ticks, int brakeTicks, int alignTicks, int closeTicks, SunArmorPose rest)
        {
            float t = Mathf.Clamp(ticks, 0, brakeTicks);
            float travel = brakeTicks > 0 ? t - t * t / (2f * brakeTicks) : 0f;
            SunArmorPose pose = start;
            pose.InnerAngle += innerSpeed * travel;
            pose.OuterAngle += outerSpeed * travel;
            pose.Glow *= 1f - Smooth(Progress(ticks, Mathf.Max(1, brakeTicks + alignTicks)));
            pose.Flash = 0f;
            if (ticks >= brakeTicks)
            {
                float align = Smooth(Progress(ticks - brakeTicks, alignTicks));
                // 每块装甲回到原来的插槽，不能按三角阵列的 120° 对称性换位。
                pose.InnerAngle = Mathf.LerpAngle(pose.InnerAngle, 0f, align);
                pose.OuterAngle = Mathf.LerpAngle(pose.OuterAngle, 0f, align);
                pose.PanelTilt *= 1f - align;
                if (ticks >= brakeTicks + alignTicks)
                {
                    pose.InnerAngle = pose.OuterAngle = 0f;
                    pose = SunArmorPose.Lerp(pose, rest,
                        Smooth(Progress(ticks - brakeTicks - alignTicks, closeTicks)));
                }
            }
            return pose;
        }

        internal static SunArmorPose CannonRecovery(int ticks, int chargeDuration,
            int brakeTicks, int alignTicks, int closeTicks, SunArmorPose rest)
        {
            SunArmorPose pose = ReturnToRest(CannonCharge(chargeDuration, chargeDuration),
                CannonMaxSpeed, CannonMaxSpeed * OuterSpeedFactor, ticks,
                brakeTicks, alignTicks, closeTicks, rest);
            float flash = 1f - Mathf.Clamp01(ticks / 8f);
            pose.Flash = flash * flash;
            float kick = ticks < 8 ? Mathf.Sin(Mathf.PI * ticks / 8f) * 0.07f : 0f;
            pose.InnerRadius *= 1f + kick;
            pose.OuterRadius *= 1f + kick;
            return pose;
        }

        internal static SunArmorPose PulseCharge(SunArmorPose start, float progress)
        {
            float p = Mathf.Clamp01(progress);
            SunArmorPose pose = SunArmorPose.Rest(true);
            // 先解锁外壳，再逐渐向核心压紧；此处只改变额外半径，不重新切回整机图。
            float compression = Smooth(Mathf.InverseLerp(0.2f, 1f, p));
            pose.InnerRadius = pose.OuterRadius = Mathf.Lerp(1.08f, 0.78f, compression);
            // 蓄力只收紧阵列，不叠加相邻甲板的交错倾斜。
            pose.PanelTilt = 0f;
            pose.Glow = 0.2f + 0.8f * p * p;
            return SunArmorPose.Lerp(start, pose, Smooth(p / 0.2f));
        }

        internal static SunArmorPose PulseRecovery(int ticks, int duration, SunArmorPose rest)
        {
            float p = Progress(ticks, duration);
            SunArmorPose pose = SunArmorPose.Rest(true);
            float burst = Smooth(p / 0.18f);
            pose.InnerRadius = pose.OuterRadius = Mathf.Lerp(0.78f, 1.2f, burst);
            // 释放时沿径向外扩，保持与蓄力末端一致的甲板朝向。
            pose.PanelTilt = 0f;
            pose.Glow = 1f - p;
            pose.Flash = Mathf.Sin(Mathf.PI * Mathf.Clamp01(p / 0.3f));
            return SunArmorPose.Lerp(pose, rest, Smooth(Mathf.InverseLerp(0.2f, 1f, p)));
        }

        internal static SunArmorPose Laser(SunArmorPose start, float progress, int warmupTicks,
            bool firing, int firingTicks)
        {
            SunArmorPose pose = SunArmorPose.Rest(true);
            float p = Mathf.Clamp01(progress);
            // 六块甲板保持等半径、60° 间隔的展开姿态，不叠加相邻甲板的交错倾斜。
            float glowPulse = firing ? Mathf.Sin(firingTicks * 0.15f) * 0.064f : 0f;
            pose.InnerRadius = pose.OuterRadius = 0.94f;
            pose.PanelTilt = 0f;
            pose.Glow = firing ? 0.82f + glowPulse : 0.15f + 0.65f * p;
            pose = SunArmorPose.Lerp(start, pose, Smooth(p / 0.25f));
            // 展开插值后统一叠加旋转，蓄力与发射同速，开火时保留蓄力累计角度。
            // 使用已有的保存进度重建，避免按渲染帧累计导致暂停、离屏或读档后错位。
            // 蓄力时长由实际技能或预览传入，保持与该机体的组件配置一致。
            float rotationTicks = p * Mathf.Max(1, warmupTicks)
                + (firing ? Mathf.Max(0, firingTicks) : 0);
            float angle = rotationTicks * LaserRotationSpeed;
            pose.InnerAngle += angle;
            pose.OuterAngle += angle;
            return pose;
        }
    }
}
