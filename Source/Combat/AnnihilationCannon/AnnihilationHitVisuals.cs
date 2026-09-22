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
        private static readonly Material HorizonHaloGlow = MakeHorizonHaloMaterial();
        private static readonly MaterialPropertyBlock Properties = new MaterialPropertyBlock();
        private static readonly Mesh Disc = MakeDisc(48);
        private static readonly Mesh HorizonDisc = MakeDisc(128);
        private static readonly Mesh Ring = MakeRing(64, 0.79f);
        private static readonly Mesh ThinRing = MakeRing(64, 0.91f);
        // 外径乘以内径比例恰好为 1，使每层光晕内沿始终贴住黑色圆盘。
        private const float HorizonHaloScale = 1.55f;
        private const float HorizonRimScale = 1.065f;
        private static readonly Mesh HorizonHalo = MakeRing(128, 1f / HorizonHaloScale);
        private static readonly Mesh HorizonRim = MakeRing(128, 1f / HorizonRimScale);
        private static readonly Mesh HorizonHighlight = MakeArc(-26f, 26f, 32, 0.95f);
        private static readonly Mesh HorizonFilament = MakeArc(-14f, 14f, 20, 0.984f);
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

        private static Material MakeHorizonHaloMaterial()
        {
            // 环网格 U=0 为内沿：金光贴边最亮，向外连续衰减至透明。
            // 两端取精确端点，避免渐变贴图边缘留下额外的硬圈。
            const int width = 128;
            const int height = 2;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "MAP_AnnihilationHit_HorizonHalo"
            };
            var pixels = new Color[width * height];
            for (int x = 0; x < width; x++)
            {
                float outward = (float)x / (width - 1);
                float opacity = Mathf.Exp(-outward * 4.8f) * (1f - Smooth(outward));
                Color color = Color.Lerp(new Color(1f, 0.89f, 0.57f),
                    new Color(1f, 0.49f, 0.16f), Mathf.Sqrt(outward));
                color.a = opacity;
                for (int y = 0; y < height; y++) pixels[y * width + x] = color;
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

        private static void DrawHorizon(Vector3 origin, float diameter, float alpha, float ageTicks,
            int seed, float distortion)
        {
            if (diameter <= 0.001f || alpha <= 0.001f) return;
            // 黑盘、亮边和外晕共用轻微的椭圆形变，保持贴边；中心不作随机跳动。
            float stretch = 1f + distortion * 0.018f;
            float width = diameter * stretch;
            float depth = diameter * 0.97f / stretch;
            Vector3 discSize = new Vector3(width, 1f, depth);
            DrawMesh(HorizonDisc, Transparent, origin, discSize, 0f,
                new Color(0.001f, 0.001f, 0.002f, alpha), 0.0452f);

            float glowPulse = 0.97f + 0.03f * Mathf.Sin(ageTicks * 0.025f + Hash(seed + 31) * Mathf.PI * 2f);
            DrawMesh(HorizonHalo, HorizonHaloGlow, origin,
                new Vector3(width * HorizonHaloScale, 1f, depth * HorizonHaloScale),
                0f, new Color(1f, 1f, 1f, alpha * glowPulse * 0.8f), 0.0454f);
            DrawMesh(HorizonRim, SoftRingGlow, origin,
                new Vector3(width * HorizonRimScale, 1f, depth * HorizonRimScale),
                0f, new Color(1f, 0.95f, 0.76f, alpha * glowPulse), 0.0456f);

            // 局部亮弧缓慢移动，形成一侧更亮的金白边缘；内沿保持在黑盘外。
            // 弧带自身接近圆形，旋转后也不会像原来的扁平吸积流穿过黑色中心。
            float highlightAngle = Hash(seed + 71) * 360f + ageTicks * 0.35f;
            DrawMesh(HorizonHighlight, FlowGlow, origin,
                new Vector3(diameter * 1.11f, 1f, diameter * 1.11f),
                highlightAngle, new Color(1f, 0.98f, 0.85f, alpha * 0.92f), 0.0458f);
            DrawMesh(HorizonHighlight, FlowGlow, origin,
                new Vector3(diameter * 1.09f, 1f, diameter * 1.09f),
                highlightAngle + 165f, new Color(1f, 0.83f, 0.44f, alpha * 0.5f), 0.0459f);

            // 少量不同轨道的细丝环绕外沿；时间和实体种子使暂停、读档后的相位稳定。
            for (int i = 0; i < 6; i++)
            {
                float orbit = 1.12f + (i % 3) * 0.065f;
                float angle = ageTicks * (0.65f + i * 0.06f) + Hash(seed + i * 23) * 360f;
                float pulse = 0.85f + 0.15f * Mathf.Sin(ageTicks * 0.035f + i * 1.7f);
                DrawMesh(HorizonFilament, FlowGlow, origin,
                    new Vector3(diameter * orbit, 1f, diameter * orbit),
                    angle, new Color(1f, 0.9f, 0.64f, alpha * pulse * 0.6f),
                    0.046f + i * 0.00002f);
            }
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
            float alpha = open * fade;
            float collapse = Smooth((t - 0.72f) / 0.28f);
            float collapseFlash = Mathf.Sin(Mathf.PI * Mathf.Clamp01((t - 0.76f) / 0.24f));
            // 低频正弦叠加提供连续的不规则感；开场和收缩时柔和进出，不每帧随机。
            float motionEnvelope = open * (1f - collapse);
            float motionSeed = Hash(seed + 43) * Mathf.PI * 2f;
            float slowWave = Mathf.Sin(ageTicks * 0.055f + motionSeed);
            float secondaryWave = Mathf.Sin(ageTicks * 0.087f + motionSeed * 0.73f + 1.2f);
            float distortion = (slowWave * 0.7f + secondaryWave * 0.3f) * motionEnvelope;
            // 整体震动：每轴最多 0.05 格，不随特效尺寸放大；全部视觉层共用偏移。
            // 连续波形避免逐帧随机跳变，按游戏 tick 和固定种子保证暂停、读档后的相位稳定。
            float jitterAmplitude = 0.05f * motionEnvelope;
            float jitterX = Mathf.Sin(ageTicks * 0.31f + motionSeed) * 0.7f
                + Mathf.Sin(ageTicks * 0.53f + motionSeed * 0.81f) * 0.3f;
            float jitterZ = Mathf.Sin(ageTicks * 0.37f + motionSeed + 1.7f) * 0.7f
                + Mathf.Sin(ageTicks * 0.59f + motionSeed * 0.67f + 0.6f) * 0.3f;
            origin += new Vector3(jitterX, 0f, jitterZ) * jitterAmplitude;
            // 最多 0.8% 的慢速呼吸，独立于位置震动，不额外加入高频尺寸变化。
            float breath = 1f + slowWave * motionEnvelope * 0.008f;
            float uncollapsedSize = (1.45f + strength * 1.65f) * open
                * Mathf.Max(0.1f, scaleMultiplier);
            float baseSize = uncollapsedSize * Mathf.Lerp(1f, 0.075f, collapse) * breath;
            // 只保留匀速环流，不在坍缩末尾突然加速；种子限制到一圈内避免大角度精度损失。
            float phase = ageTicks * 1.6f + Hash(seed + 17) * 360f;

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
            float flowSize = baseSize;
            float flowStretch = 1f + distortion * 0.03f;
            float flowTurn = secondaryWave * motionEnvelope * 1.2f;
            Color softWarm = warm;
            softWarm.a *= 0.72f;
            DrawMesh(LongArc, FlowGlow, origin,
                new Vector3(flowSize * 1.65f * flowStretch, 1f, flowSize * 0.58f / flowStretch),
                phase * 0.31f + flowTurn, softWarm, 0.032f);
            Color backHighlight = pale;
            backHighlight.a *= 0.72f;
            DrawMesh(ShortArc, FlowGlow, origin,
                new Vector3(flowSize * 1.52f / flowStretch, 1f, flowSize * 0.52f * flowStretch),
                -phase * 0.24f + 182f - flowTurn, backHighlight, 0.034f);

            // 多组短高光沿同一轨道以不同速度滑行；亮度也沿时间错峰呼吸。
            // 这些是独立小弧，不会再出现整条光环像硬质圆盘同步转动的感觉。
            for (int i = 0; i < 9; i++)
            {
                float lane = i % 3;
                float travel = phase * (0.72f + lane * 0.16f) + i * 137.5f;
                float pulse = 0.85f + 0.15f * Mathf.Sin(ageTicks * 0.035f + i * 1.23f);
                Color streamColor = i % 3 == 0 ? white : i % 2 == 0 ? pale : warm;
                streamColor.a *= pulse * 0.72f;
                float laneScale = 1f + (lane - 1f) * 0.075f;
                DrawMesh(i % 3 == 0 ? FlowNeedle : FlowArc, FlowGlow, origin,
                    new Vector3(flowSize * 1.62f * laneScale * flowStretch, 1f,
                        flowSize * 0.56f * (2f - laneScale) / flowStretch),
                    travel + flowTurn * 0.5f, streamColor,
                    0.035f + i * 0.0002f);
            }

            DrawMesh(LongArc, FlowGlow, origin,
                new Vector3(flowSize * 1.72f * flowStretch, 1f, flowSize * 0.62f / flowStretch),
                phase * 0.31f + 180f + flowTurn, pale, 0.043f);
            Color filament = white;
            filament.a *= 0.7f;
            DrawMesh(ShortArc, FlowGlow, origin,
                new Vector3(flowSize * 1.6f / flowStretch, 1f, flowSize * 0.48f * flowStretch),
                -phase * 0.43f - flowTurn, filament, 0.045f);

            // 圆盘与贴边光环盖在吸积流前方，保持参考样式中完整、干净的黑色中心。
            DrawHorizon(origin, baseSize * 0.58f, alpha, ageTicks, seed, distortion);

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
                    90f - angle, shardColor, 0.037f + i * 0.00001f);
            }
        }
    }
}
