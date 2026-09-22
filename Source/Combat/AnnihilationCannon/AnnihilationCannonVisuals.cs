using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 独立视觉资源：蓄力与瞄准复用业火炮纹理的灰度遮罩。
    /// 不修改原版缓存材质，不创建有伤害/碰撞的 Projectile，也不向磁盘写贴图。
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class AnnihilationCannonVisuals
    {
        private static readonly Material Charge = MonochromeMask("Things/Mote/HellsphereCannon_ChargeGlow");
        private static readonly Material Aim = MonochromeMask("Things/Mote/HellsphereCannon_Aim");
        private static readonly Material Target = MonochromeMask("Things/Mote/MoteHellfireCannon_Target");
        private static readonly Material ContractionRing = CreateRingMaterial();
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

        internal static void DrawWarmupPart(Vector3 casterPosition, IntVec3 target, float progress,
            float chargeSeconds, int part, float alpha)
        {
            if (alpha <= 0f || Find.UIRoot?.HideMotes == true) return;
            float time = Find.TickManager.TicksGame / 60f;
            float pulse = 0.9f + 0.1f * Mathf.Sin(time * 24f);
            progress = Mathf.Clamp01(progress);
            Vector3 destination = target.ToVector3Shifted();
            Vector3 direction = (destination - casterPosition).Yto0().normalized;
            Vector3 muzzle = (casterPosition + direction * 1.07f).WithY(AltitudeLayer.MoteOverhead.AltitudeFor());
            destination.y = muzzle.y;
            float angle = direction.AngleFlat();
            float length = (destination - muzzle).magnitude;
            Vector3 middle = (muzzle + destination) * 0.5f;
            Color gold = new Color(0.78f, 0.57f, 0.22f, alpha);
            Color white = new Color(0.94f, 0.9f, 0.76f, alpha);
            if (part == 0)
            {
                Draw(Aim, middle, new Vector3(0.8f + progress, 1f, length), angle, gold);
                middle.y += 0.01f;
                Draw(Aim, middle, new Vector3(0.25f + progress * 0.4f, 1f, length), angle,
                    new Color(white.r, white.g, white.b, (0.65f + progress * 0.35f) * alpha));
            }
            else if (part == 1)
            {
                float size = (1.4f + progress * 2f) * pulse;
                Draw(Charge, muzzle, new Vector3(size * 1.35f, 1f, size * 1.35f), time * 30f, gold);
                muzzle.y += 0.02f;
                Draw(Charge, muzzle, new Vector3(size, 1f, size), -time * 45f, white);
            }
            else
            {
                // 准星只绘制一次，直接混合白金色，避免不同尺寸的同一贴图产生双层重影。
                Color targetColor = Color.Lerp(gold, white, 0.65f);
                targetColor.a = pulse * alpha;
                Draw(Target, destination, new Vector3(8f, 1f, 8f), 0f, targetColor);
                destination.y += 0.01f;
                // 对随进度线性增加的频率积分，避免直接乘当前速度造成相位跳变。
                // 时间来自 Job 的已保存蓄力 tick，暂停冻结，读档恢复同一收缩阶段。
                float phase = chargeSeconds * (0.6f + 1.2f * progress);
                for (int i = 0; i < 3; i++)
                {
                    float contraction = Mathf.Repeat(phase + i / 3f, 1f);
                    float diameter = Mathf.Lerp(8f, 0.4f, contraction);
                    Color ringColor = Color.Lerp(gold, white, contraction);
                    ringColor.a = alpha * Mathf.Sqrt(Mathf.Sin(contraction * Mathf.PI));
                    Draw(ContractionRing, destination, new Vector3(diameter, 1f, diameter), 0f, ringColor);
                }
            }
        }
    }
}
