using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 湮灭炮命中时的纯程序动画。所有轮廓都由运行时网格组成，不依赖贴图或序列帧。
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class AnnihilationHitVisuals
    {
        private static readonly Material Transparent = MakeMaterial(ShaderDatabase.Transparent);
        private static readonly Material Glow = MakeMaterial(ShaderDatabase.MoteGlow);
        private static readonly Material FlowGlow = MakeFlowMaterial();
        private static readonly Material SoftRingGlow = MakeSoftRingMaterial();
        private static readonly MaterialPropertyBlock Properties = new MaterialPropertyBlock();
        private static readonly Mesh Disc = MakeDisc(48);
        private static readonly Mesh HorizonDisc = MakeWobblyDisc(64, 0.4f);
        private static readonly Mesh Ring = MakeRing(64, 0.79f);
        private static readonly Mesh ThinRing = MakeRing(64, 0.91f);
        private static readonly Mesh HorizonRingA = MakeWobblyRing(64, 0.82f, 0.4f);
        private static readonly Mesh HorizonRingB = MakeWobblyRing(64, 0.9f, 2.1f);
        private static readonly Mesh LongArc = MakeArc(-154f, 34f, 36, 0.77f);
        private static readonly Mesh ShortArc = MakeArc(18f, 142f, 28, 0.74f);
        private static readonly Mesh FlowArc = MakeArc(-13f, 13f, 8, 0.84f);
        private static readonly Mesh FlowNeedle = MakeArc(-7f, 7f, 6, 0.68f);
        private static readonly Mesh Shard = MakeShard();

        private static Material MakeMaterial(Shader shader) =>
            new Material(shader) { mainTexture = BaseContent.WhiteTex };

        private static Material MakeFlowMaterial()
        {
            // U 为带宽、V 为弧长；两端和内外缘同时渐隐，让程序网格看起来像气流而非硬片。
            const int width = 16;
            const int length = 64;
            var texture = new Texture2D(width, length, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "MAP_AnnihilationHit_FlowGradient"
            };
            var pixels = new Color[width * length];
            for (int y = 0; y < length; y++)
            {
                float along = (y + 0.5f) / length;
                float endFade = Mathf.Pow(Mathf.Sin(along * Mathf.PI), 0.62f);
                for (int x = 0; x < width; x++)
                {
                    float across = (x + 0.5f) / width;
                    float edgeFade = Mathf.Pow(Mathf.Sin(across * Mathf.PI), 0.72f);
                    pixels[y * width + x] = new Color(1f, 1f, 1f, endFade * edgeFade);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return new Material(ShaderDatabase.MoteGlow) { mainTexture = texture };
        }

        private static Material MakeSoftRingMaterial()
        {
            // 完整光环只沿带宽柔化，不在环的首尾制造接缝。
            const int width = 32;
            const int height = 2;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "MAP_AnnihilationHit_RingGradient"
            };
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float across = (x + 0.5f) / width;
                float opacity = Mathf.Pow(Mathf.Sin(across * Mathf.PI), 0.68f);
                pixels[y * width + x] = new Color(1f, 1f, 1f, opacity);
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return new Material(ShaderDatabase.MoteGlow) { mainTexture = texture };
        }

        private static Mesh MakeDisc(int segments)
        {
            var vertices = new Vector3[segments + 1];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[segments * 3];
            vertices[0] = Vector3.zero;
            uv[0] = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                vertices[i + 1] = new Vector3(Mathf.Cos(angle) * 0.5f, 0f, Mathf.Sin(angle) * 0.5f);
                uv[i + 1] = new Vector2(Mathf.Cos(angle) * 0.5f + 0.5f, Mathf.Sin(angle) * 0.5f + 0.5f);
                int next = (i + 1) % segments;
                triangles[i * 3] = 0;
                // 地图相机从 +Y 方向观察；保持顺序朝上，避免透明 Shader 背面剔除。
                triangles[i * 3 + 1] = next + 1;
                triangles[i * 3 + 2] = i + 1;
            }
            return FinishMesh(vertices, uv, triangles, "MAP_AnnihilationHit_Disc");
        }

        private static float WobblyRadius(float angle, float phase) => 0.5f * (1f
            + Mathf.Sin(angle * 5f + phase) * 0.036f
            + Mathf.Sin(angle * 11f - phase * 0.7f) * 0.018f
            + Mathf.Sin(angle * 17f + phase * 1.3f) * 0.009f);

        private static Mesh MakeWobblyDisc(int segments, float phase)
        {
            var vertices = new Vector3[segments + 1];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[segments * 3];
            vertices[0] = Vector3.zero;
            uv[0] = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                float radius = WobblyRadius(angle, phase);
                vertices[i + 1] = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                uv[i + 1] = new Vector2(Mathf.Cos(angle) * radius + 0.5f,
                    Mathf.Sin(angle) * radius + 0.5f);
                int next = (i + 1) % segments;
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = next + 1;
                triangles[i * 3 + 2] = i + 1;
            }
            return FinishMesh(vertices, uv, triangles, "MAP_AnnihilationHit_WobblyDisc");
        }

        private static Mesh MakeWobblyRing(int segments, float innerRadius, float phase)
        {
            var vertices = new Vector3[(segments + 1) * 2];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[segments * 6];
            for (int i = 0; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                float radius = WobblyRadius(angle, phase);
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices[i * 2] = direction * radius;
                vertices[i * 2 + 1] = direction * (radius * innerRadius);
                uv[i * 2] = new Vector2(1f, (float)i / segments);
                uv[i * 2 + 1] = new Vector2(0f, (float)i / segments);
                if (i == segments) continue;
                int v = i * 2;
                int t = i * 6;
                triangles[t] = v;
                triangles[t + 1] = v + 1;
                triangles[t + 2] = v + 2;
                triangles[t + 3] = v + 1;
                triangles[t + 4] = v + 3;
                triangles[t + 5] = v + 2;
            }
            return FinishMesh(vertices, uv, triangles, "MAP_AnnihilationHit_WobblyRing");
        }

        private static Mesh MakeRing(int segments, float innerRadius)
        {
            var vertices = new Vector3[(segments + 1) * 2];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[segments * 6];
            for (int i = 0; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices[i * 2] = direction * 0.5f;
                vertices[i * 2 + 1] = direction * (0.5f * innerRadius);
                uv[i * 2] = new Vector2(1f, (float)i / segments);
                uv[i * 2 + 1] = new Vector2(0f, (float)i / segments);
                if (i == segments) continue;
                int v = i * 2;
                int t = i * 6;
                triangles[t] = v;
                triangles[t + 1] = v + 1;
                triangles[t + 2] = v + 2;
                triangles[t + 3] = v + 1;
                triangles[t + 4] = v + 3;
                triangles[t + 5] = v + 2;
            }
            return FinishMesh(vertices, uv, triangles, "MAP_AnnihilationHit_Ring");
        }

        private static Mesh MakeArc(float startDegrees, float endDegrees, int segments, float innerRadius)
        {
            var vertices = new Vector3[(segments + 1) * 2];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[segments * 6];
            for (int i = 0; i <= segments; i++)
            {
                float t01 = (float)i / segments;
                float angle = Mathf.Lerp(startDegrees, endDegrees, t01) * Mathf.Deg2Rad;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices[i * 2] = direction * 0.5f;
                vertices[i * 2 + 1] = direction * (0.5f * innerRadius);
                uv[i * 2] = new Vector2(1f, t01);
                uv[i * 2 + 1] = new Vector2(0f, t01);
                if (i == segments) continue;
                int v = i * 2;
                int tri = i * 6;
                triangles[tri] = v;
                triangles[tri + 1] = v + 1;
                triangles[tri + 2] = v + 2;
                triangles[tri + 3] = v + 1;
                triangles[tri + 4] = v + 3;
                triangles[tri + 5] = v + 2;
            }
            return FinishMesh(vertices, uv, triangles, "MAP_AnnihilationHit_Arc");
        }

        private static Mesh MakeShard()
        {
            return FinishMesh(new[]
            {
                new Vector3(-0.5f, 0f, -0.12f), new Vector3(0.5f, 0f, 0f),
                new Vector3(-0.5f, 0f, 0.12f)
            }, new[] { Vector2.zero, Vector2.right, Vector2.up }, new[] { 0, 2, 1 }, "MAP_AnnihilationHit_Shard");
        }

        private static Mesh FinishMesh(Vector3[] vertices, Vector2[] uv, int[] triangles, string name)
        {
            var mesh = new Mesh { name = name };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }

        private static void DrawMesh(Mesh mesh, Material material, Vector3 center, Vector3 size,
            float angle, Color color, float altitudeOffset)
        {
            center.y = AltitudeLayer.MoteOverhead.AltitudeFor() + altitudeOffset;
            Properties.Clear();
            Properties.SetColor(ShaderPropertyIDs.Color, color);
            Graphics.DrawMesh(mesh, Matrix4x4.TRS(center, Quaternion.AngleAxis(angle, Vector3.up), size),
                material, 0, null, 0, Properties);
        }

        private static float Smooth(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }

        private static float Hash(int value)
        {
            float result = Mathf.Sin(value * 91.733f) * 43758.547f;
            return result - Mathf.Floor(result);
        }

        internal static void Draw(Vector3 origin, int seed, AnnihilationSettings settings, float ageTicks)
        {
            if (Find.UIRoot?.HideMotes == true || ageTicks >= settings.VisualDurationTicks) return;
            float t = settings.VisualProgress(ageTicks);
            float energy = settings.damage;
            float scaleMultiplier = settings.effectRadius / 3.2f;
            float strength = Mathf.Clamp01(Mathf.Log10(1f + Mathf.Max(0f, energy)) / 2.8f);
            float open = Smooth(t / 0.13f);
            float fade = 1f - Smooth((t - 0.62f) / 0.38f);
            float punch = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.24f));
            float alpha = open * fade;
            float collapse = Smooth((t - 0.72f) / 0.28f);
            float collapseFlash = Mathf.Sin(Mathf.PI * Mathf.Clamp01((t - 0.76f) / 0.24f));
            // 展开阶段保持明显的不稳定性：尺寸呼吸、椭圆形变和中心抖动使用不同频率，
            // 避免整个效果像一张贴图匀速放大。稳定后迅速收敛，只留下很轻的流动。
            float instability = open * (1f - Smooth((t - 0.05f) / 0.42f));
            float sizeTremor = 1f + instability * (Mathf.Sin(ageTicks * 1.73f + seed) * 0.075f
                + Mathf.Sin(ageTicks * 0.61f + seed * 0.37f) * 0.045f);
            float uncollapsedSize = (1.45f + strength * 1.65f) * open * (1f + punch * 0.16f)
                * sizeTremor * Mathf.Max(0.1f, scaleMultiplier);
            float baseSize = uncollapsedSize * Mathf.Lerp(1f, 0.075f, collapse);
            float phase = ageTicks * 3.7f + seed * 17f
                + collapse * collapse * 150f;
            float offsetAngle = Hash(seed + 3) * Mathf.PI * 2f;
            origin += new Vector3(Mathf.Cos(offsetAngle), 0f, Mathf.Sin(offsetAngle)) * 0.13f;
            float shake = (0.035f + strength * 0.055f) * instability;
            origin += new Vector3(
                Mathf.Sin(ageTicks * 2.41f + seed * 0.71f) + Mathf.Sin(ageTicks * 0.93f + seed) * 0.45f,
                0f,
                Mathf.Cos(ageTicks * 2.17f + seed * 0.53f) + Mathf.Sin(ageTicks * 1.19f + seed) * 0.4f) * shake;
            float axisWobble = instability * Mathf.Sin(ageTicks * 1.31f + seed * 0.23f) * 0.11f;
            float angularJolt = instability * (Mathf.Sin(ageTicks * 1.87f + seed) * 4.5f
                + Mathf.Sin(ageTicks * 0.47f + seed * 0.4f) * 2.5f);

            Color warm = new Color(0.82f, 0.57f, 0.39f, alpha * 0.78f);
            Color pale = new Color(1f, 0.93f, 0.78f, alpha * 0.95f);
            Color white = new Color(1f, 1f, 0.96f, alpha);

            // 开场的中心白闪与两层冲击波，快速结束以免遮住本体。
            float flash = 1f - Smooth(t / 0.22f);
            if (flash > 0f)
            {
                float flashSize = baseSize * (0.24f + t * 3.2f);
                DrawMesh(Disc, Glow, origin, new Vector3(flashSize, 1f, flashSize), phase,
                    new Color(1f, 0.94f, 0.74f, flash * (0.5f + strength * 0.45f)), 0.025f);
            }
            for (int i = 0; i < 2; i++)
            {
                float wave = Mathf.Clamp01(t * 2.3f - i * 0.16f);
                float waveAlpha = Mathf.Sin(wave * Mathf.PI) * (0.55f - i * 0.12f) * (0.65f + strength * 0.35f);
                if (waveAlpha > 0.002f)
                {
                    float diameter = baseSize * Mathf.Lerp(0.65f, 2.15f + i * 0.42f, wave);
                    Color waveColor = i == 0 ? pale : warm;
                    waveColor.a = waveAlpha;
                    DrawMesh(i == 0 ? Ring : ThinRing, SoftRingGlow, origin,
                        new Vector3(diameter, 1f, diameter * 0.82f), phase * (i == 0 ? 0.22f : -0.15f),
                        waveColor, 0.01f + i * 0.001f);
                }
            }

            // 上下两股不同转速的吸积流围绕事件视界回卷。
            float flowSize = baseSize * (1f + Mathf.Sin(ageTicks * 0.12f) * 0.025f);
            Color softWarm = warm;
            softWarm.a *= 0.72f;
            DrawMesh(LongArc, FlowGlow, origin, new Vector3(flowSize * 1.65f, 1f, flowSize * 0.58f),
                phase * 0.31f + angularJolt, softWarm, 0.032f);
            Color backHighlight = pale;
            backHighlight.a *= 0.72f;
            DrawMesh(ShortArc, FlowGlow, origin,
                new Vector3(flowSize * (1.52f + axisWobble), 1f, flowSize * (0.52f - axisWobble * 0.35f)),
                -phase * 0.24f + 182f - angularJolt * 0.7f, backHighlight, 0.034f);

            // 多组短高光沿同一轨道以不同速度滑行；亮度也沿时间错峰呼吸。
            // 这些是独立小弧，不会再出现整条光环像硬质圆盘同步转动的感觉。
            for (int i = 0; i < 9; i++)
            {
                float lane = i % 3;
                float travel = phase * (0.72f + lane * 0.16f) + i * 137.5f;
                float pulse = 0.42f + 0.58f * Mathf.Sin((ageTicks * 0.075f + i * 0.39f) * Mathf.PI);
                pulse = Mathf.Clamp01(pulse);
                Color streamColor = i % 3 == 0 ? white : i % 2 == 0 ? pale : warm;
                streamColor.a *= 0.28f + pulse * 0.64f;
                float laneScale = 1f + (lane - 1f) * 0.075f;
                DrawMesh(i % 3 == 0 ? FlowNeedle : FlowArc, FlowGlow, origin,
                    new Vector3(flowSize * 1.62f * laneScale, 1f,
                        flowSize * 0.56f * (2f - laneScale)),
                    travel + angularJolt * (i % 2 == 0 ? 1f : -0.6f), streamColor,
                    0.035f + i * 0.0002f);
            }

            float horizon = baseSize * 0.58f;
            float edgeActivity = 0.24f + instability * 0.76f;
            float steppedNoise = Mathf.Round((Mathf.Sin(ageTicks * 2.07f + seed * 0.19f)
                + Mathf.Sin(ageTicks * 0.83f + seed) * 0.55f) * 2f) * 0.5f;
            float edgePulse = 1f + steppedNoise * (0.014f + edgeActivity * 0.026f);
            Vector3 edgeOrigin = origin + new Vector3(
                Mathf.Sin(ageTicks * 2.63f + seed) * 0.022f,
                0f,
                Mathf.Cos(ageTicks * 2.29f + seed * 0.61f) * 0.022f) * edgeActivity;
            DrawMesh(HorizonDisc, Transparent, edgeOrigin,
                new Vector3(horizon * (1f + axisWobble) * edgePulse, 1f,
                    horizon * (0.91f - axisWobble) / edgePulse),
                phase * 0.08f + angularJolt + steppedNoise * 1.8f,
                new Color(0.005f, 0.008f, 0.014f, alpha * 0.92f), 0.038f);
            Color edge = warm;
            edge.a = alpha * (0.46f + edgeActivity * 0.22f);
            DrawMesh(HorizonRingA, SoftRingGlow, edgeOrigin,
                new Vector3(horizon * (1.12f + axisWobble) * edgePulse, 1f,
                    horizon * (1.02f - axisWobble) / edgePulse),
                phase * 0.16f + angularJolt + steppedNoise * 2.4f, edge, 0.04f);
            Color edgeSpark = pale;
            edgeSpark.a = alpha * (0.25f + Mathf.Abs(steppedNoise) * 0.16f);
            DrawMesh(HorizonRingB, FlowGlow, edgeOrigin,
                new Vector3(horizon * (1.08f - axisWobble * 0.5f) / edgePulse, 1f,
                    horizon * (0.98f + axisWobble * 0.4f) * edgePulse),
                -phase * 0.21f - angularJolt * 0.55f, edgeSpark, 0.041f);

            DrawMesh(LongArc, FlowGlow, origin, new Vector3(flowSize * 1.72f, 1f, flowSize * 0.62f),
                phase * 0.31f + 180f + angularJolt, pale, 0.043f);
            Color filament = white;
            filament.a *= 0.7f;
            DrawMesh(ShortArc, FlowGlow, origin,
                new Vector3(flowSize * (1.6f - axisWobble), 1f, flowSize * (0.48f + axisWobble * 0.3f)),
                -phase * 0.43f - angularJolt * 0.8f, filament, 0.045f);

            // 结尾不直接淡没：外圈向事件视界急速收拢，最后压成一个短促亮点。
            if (collapseFlash > 0.001f)
            {
                float pinch = uncollapsedSize * Mathf.Lerp(1.18f, 0.09f, collapse);
                Color pinchWarm = warm;
                pinchWarm.a = collapseFlash * 0.7f;
                DrawMesh(Ring, SoftRingGlow, origin,
                    new Vector3(pinch * 1.25f, 1f, pinch * 0.82f),
                    phase * 0.55f, pinchWarm, 0.047f);
                Color pinchWhite = white;
                pinchWhite.a = collapseFlash * Mathf.Lerp(0.35f, 0.95f, collapse);
                float core = uncollapsedSize * Mathf.Lerp(0.34f, 0.035f, collapse);
                DrawMesh(Disc, Glow, origin, new Vector3(core, 1f, core), -phase,
                    pinchWhite, 0.049f);
            }

            // 可重复的外冲碎光；使用命中种子而非全局随机，暂停和读档画面不会跳变。
            float debrisLife = 1f - Smooth((t - 0.08f) / 0.72f);
            for (int i = 0; i < 10; i++)
            {
                float angle = (Hash(seed + i * 13) * 360f + phase * (0.035f + i * 0.002f));
                float radians = angle * Mathf.Deg2Rad;
                float distance = baseSize * (0.36f + t * (0.8f + Hash(seed + i * 7) * 0.85f));
                Vector3 position = origin + new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians)) * distance;
                float length = baseSize * (0.12f + Hash(seed + i * 19) * 0.22f);
                Color shardColor = i % 3 == 0 ? white : warm;
                shardColor.a *= debrisLife * (0.38f + Hash(seed + i) * 0.48f);
                DrawMesh(Shard, Glow, position, new Vector3(length, 1f, 0.035f + strength * 0.025f),
                    90f - angle, shardColor, 0.048f + i * 0.0001f);
            }
        }
    }
}
