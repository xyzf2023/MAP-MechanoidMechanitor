using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 独立视觉资源：瞄准复用原版遮罩，发射端使用程序化蓝白聚能动画。
    /// 不修改原版缓存材质，不创建有伤害/碰撞的 Projectile，也不向磁盘写贴图。
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class AnnihilationCannonVisuals
    {
        private static readonly Material Aim = MonochromeMask("Things/Mote/HellsphereCannon_Aim");
        private static readonly Material Target = MonochromeMask("Things/Mote/MoteHellfireCannon_Target");
        private static readonly Material ContractionRing = CreateRingMaterial();
        private static readonly Material EnergyGlow = CreateEnergyGlowMaterial();
        private static readonly MaterialPropertyBlock Properties = new MaterialPropertyBlock();

        private static Material CreateRingMaterial()
        {
            // 独立的柔边圆环遮罩，只初始化一次；不修改原版共享材质。
            const int resolution = 128;
            Texture2D texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            Color[] pixels = new Color[resolution * resolution];
            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float radius = new Vector2((x + 0.5f) / resolution * 2f - 1f,
                        (y + 0.5f) / resolution * 2f - 1f).magnitude;
                    float opacity = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Abs(radius - 0.92f) / 0.045f);
                    pixels[y * resolution + x] = new Color(1f, 1f, 1f, opacity);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return new Material(ShaderDatabase.Transparent) { mainTexture = texture };
        }

        private static Material CreateEnergyGlowMaterial()
        {
            const int resolution = 96;
            Texture2D texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            Color[] pixels = new Color[resolution * resolution];
            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float radius = new Vector2((x + 0.5f) / resolution * 2f - 1f,
                        (y + 0.5f) / resolution * 2f - 1f).magnitude;
                    float alpha = Mathf.Pow(Mathf.Clamp01(1f - radius), 2.4f);
                    pixels[y * resolution + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return new Material(ShaderDatabase.MoteGlow) { mainTexture = texture };
        }

        private static Material MonochromeMask(string path)
        {
            Texture2D source = ContentFinder<Texture2D>.Get(path);
            RenderTexture? previous = RenderTexture.active;
            RenderTexture? temporary = null;
            Texture2D? mask = null;
            try
            {
                // 游戏贴图可能不可读，先读回独立副本再转换；只在启动时执行一次。
                temporary = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(source, temporary);
                RenderTexture.active = temporary;
                mask = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false)
                {
                    wrapMode = TextureWrapMode.Clamp
                };
                mask.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                Color[] pixels = mask.GetPixels();
                for (int i = 0; i < pixels.Length; i++)
                    // 提高遮罩密度，保留透明边缘，让白金色在明亮地面上更清晰。
                    pixels[i] = new Color(1f, 1f, 1f,
                        Mathf.Clamp01(pixels[i].a * Mathf.Sqrt(pixels[i].grayscale) * 1.35f));
                mask.SetPixels(pixels);
                mask.Apply(false, true);
                return new Material(ShaderDatabase.Transparent) { mainTexture = mask };
            }
            catch (Exception ex)
            {
                if (mask != null) UnityEngine.Object.Destroy(mask);
                Log.Warning("[MAP] 湮灭炮灰度蓄力贴图初始化失败：" + ex.Message);
                return new Material(ShaderDatabase.Transparent) { mainTexture = BaseContent.WhiteTex };
            }
            finally
            {
                RenderTexture.active = previous;
                if (temporary != null) RenderTexture.ReleaseTemporary(temporary);
            }
        }

        private static void Draw(Material material, Vector3 center, Vector3 size,
            float angle, Color color, float age = 0f)
        {
            Properties.Clear();
            Properties.SetColor(ShaderPropertyIDs.Color, color);
            Properties.SetFloat(ShaderPropertyIDs.AgeSecs, age);
            Properties.SetFloat(ShaderPropertyIDs.AgeSecsPausable, age);
            Properties.SetFloat(ShaderPropertyIDs.RandomPerObject, 0f);
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(center,
                Quaternion.AngleAxis(angle, Vector3.up), size), material, 0, null, 0, Properties);
        }

        internal static void DrawWarmupPart(Vector3 emitterPosition, IntVec3 target, float progress,
            float chargeSeconds, int part, float alpha)
        {
            if (alpha <= 0f || Find.UIRoot?.HideMotes == true) return;
            float time = Find.TickManager.TicksGame / 60f;
            float pulse = 0.9f + 0.1f * Mathf.Sin(time * 24f);
            progress = Mathf.Clamp01(progress);
            Vector3 destination = target.ToVector3Shifted();
            Vector3 direction = (destination - emitterPosition).Yto0().normalized;
            // 调用方已提供呼吸灯中心；这里只设置特效图层，不再沿目标方向前移。
            Vector3 muzzle = emitterPosition.WithY(AltitudeLayer.MoteOverhead.AltitudeFor());
            destination.y = muzzle.y;
            float angle = direction.AngleFlat();
            float length = (destination - muzzle).magnitude;
            Vector3 middle = (muzzle + destination) * 0.5f;
            Color blue = new Color(0.10f, 0.48f, 1f, alpha);
            Color cyan = new Color(0.32f, 0.88f, 1f, alpha);
            Color white = new Color(0.92f, 0.98f, 1f, alpha);
            if (part == 0)
            {
                // 瞄准束压低存在感，把视觉重心留给发射端的聚能动画。
                Draw(Aim, middle, new Vector3(0.18f + progress * 0.22f, 1f, length),
                    angle, new Color(blue.r, blue.g, blue.b, alpha * (0.16f + progress * 0.22f)));
                middle.y += 0.01f;
                Draw(Aim, middle, new Vector3(0.07f + progress * 0.08f, 1f, length), angle,
                    new Color(cyan.r, cyan.g, cyan.b, alpha * (0.25f + progress * 0.35f)));
            }
            else if (part == 1)
            {
                DrawEmitterCharge(muzzle, progress, chargeSeconds, alpha, pulse, blue, cyan, white);
            }
            else
            {
                // 目标端仅保留冷色定位环，避免和发射端争夺视觉焦点。
                Color targetColor = Color.Lerp(blue, cyan, 0.55f);
                targetColor.a = pulse * alpha * 0.62f;
                Draw(Target, destination, new Vector3(6.5f, 1f, 6.5f), 0f, targetColor);
                destination.y += 0.01f;
                // 对随进度线性增加的频率积分，避免直接乘当前速度造成相位跳变。
                // 时间来自 Job 的已保存蓄力 tick，暂停冻结，读档恢复同一收缩阶段。
                float phase = chargeSeconds * (0.6f + 1.2f * progress);
                for (int i = 0; i < 3; i++)
                {
                    float contraction = Mathf.Repeat(phase + i / 3f, 1f);
                    float diameter = Mathf.Lerp(8f, 0.4f, contraction);
                    Color ringColor = Color.Lerp(blue, cyan, contraction);
                    ringColor.a = alpha * 0.5f * Mathf.Sqrt(Mathf.Sin(contraction * Mathf.PI));
                    Draw(ContractionRing, destination, new Vector3(diameter, 1f, diameter), 0f, ringColor);
                }
            }
        }

        private static void DrawEmitterCharge(Vector3 muzzle, float progress, float chargeSeconds,
            float alpha, float pulse, Color blue, Color cyan, Color white)
        {
            // 与装甲相同的加速曲线积分：进度来自 Job，旋转相位不依赖镜头或帧率。
            float integratedAcceleration = chargeSeconds * Mathf.Pow(Mathf.Clamp01(progress), 1.5f) / 2.5f;
            float innerAngle = integratedAcceleration * 60f * SunSkillAnimation.CannonMaxSpeed;
            progress = Mathf.SmoothStep(0f, 1f, progress);
            muzzle.y += 0.025f;

            // 三道圆环不断从外圈坍缩到炮口，越接近发射旋转越快。
            float phase = chargeSeconds * 0.65f + integratedAcceleration * 1.15f;
            for (int i = 0; i < 3; i++)
            {
                float contraction = Mathf.Repeat(phase + i / 3f, 1f);
                float diameter = Mathf.Lerp(SunSkillAnimation.CannonOuterRadius * 2f, 0.28f, contraction);
                Color ring = Color.Lerp(blue, cyan, contraction);
                ring.a = alpha * (0.22f + progress * 0.58f)
                    * Mathf.Sin(contraction * Mathf.PI);
                Draw(ContractionRing, muzzle, new Vector3(diameter, 1f, diameter),
                    innerAngle * (i % 2 == 0 ? 1f : SunSkillAnimation.OuterSpeedFactor), ring);
            }

            // 离散能量粒子沿螺旋轨迹从装甲外缘向炮口核心汇聚。
            const int particles = 14;
            for (int i = 0; i < particles; i++)
            {
                bool inner = i % 2 == 0;
                float travel = Mathf.Repeat(chargeSeconds * 0.72f + integratedAcceleration * 0.92f
                    + i / (float)particles, 1f);
                float radius = Mathf.Lerp(inner ? SunSkillAnimation.CannonInnerRadius
                    : SunSkillAnimation.CannonOuterRadius, 0.06f, travel);
                float theta = i * 137.508f
                    - innerAngle * (inner ? 1f : SunSkillAnimation.OuterSpeedFactor)
                    - travel * 150f * (inner ? 1f : -1f);
                float radians = theta * Mathf.Deg2Rad;
                Vector3 point = muzzle + new Vector3(Mathf.Cos(radians), i * 0.0003f,
                    Mathf.Sin(radians)) * radius;
                float opacity = Mathf.Sin(travel * Mathf.PI) * alpha
                    * (0.24f + progress * 0.76f);
                float size = Mathf.Lerp(0.18f, 0.055f, travel) * (0.8f + pulse * 0.2f);
                Color particle = Color.Lerp(blue, white, travel);
                particle.a = opacity;
                Draw(EnergyGlow, point, new Vector3(size * 1.8f, 1f, size),
                    theta + 90f, particle);
            }

            // 核心随进度增亮，但在发射前保持收束，不再向外膨胀成硬质光球。
            float coreSize = Mathf.Lerp(0.22f, 0.58f, progress) * pulse;
            Color core = Color.Lerp(cyan, white, Mathf.Pow(progress, 2f));
            core.a = alpha * (0.55f + progress * 0.45f);
            Draw(EnergyGlow, muzzle, new Vector3(coreSize, 1f, coreSize), 0f, core);
            muzzle.y += 0.003f;
            Draw(EnergyGlow, muzzle, new Vector3(coreSize * 0.42f, 1f, coreSize * 0.42f),
                0f, new Color(1f, 1f, 1f, alpha * Mathf.Lerp(0.35f, 1f, progress)));
        }

    }
}
