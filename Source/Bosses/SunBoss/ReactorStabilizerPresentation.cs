using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class CompProperties_ReactorStabilizerPresentation : CompProperties
    {
        public CompProperties_ReactorStabilizerPresentation() =>
            compClass = typeof(CompReactorStabilizerPresentation);
    }

    /// <summary>只接管建筑外观；升降进度来自竞技场，组件不 Tick、不保存第二份激活状态。</summary>
    public sealed class CompReactorStabilizerPresentation : ThingComp
    {
        public override bool DontDrawParent() =>
            parent.Spawned && parent.def.drawerType == DrawerType.RealtimeOnly;

        public override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            base.DrawAt(drawLoc, flip);
            if (parent.Spawned && parent.def.drawerType == DrawerType.RealtimeOnly)
                ReactorStabilizerPresentation.Draw(parent, drawLoc);
        }
    }

    /// <summary>分层升降、整图交接及沿折线分流的灯效；动态资源在首次实际绘制时创建并复用。</summary>
    internal static class ReactorStabilizerPresentation
    {
        private const string TextureRoot = "Buildings/SunBOSSAncient/";
        private const float CanvasSize = 1254f;
        private const float PatternWidth = 498f;
        private const float PatternHeight = 719f;
        private const float PatternLeft = 376f;
        private const float PatternTop = 338f;
        // 与太阳最后的收稳阶段对齐：在总时长的 8/9 处升到位，随后点亮纹路。
        private const int DeploymentTicks = SunSkillAnimation.AwakeningSettleTick;
        private const int DustStartTick = 5;
        private const int DustIntervalTicks = 15;
        private const float RetractedDistance = 480f;
        private const float BodyClipY = 1040f;
        private const int FlowSliceCount = 32;
        private const int FlowCellSize = 4;
        private const int FlowCycleTicks = 156;
        private const float FlowHalfWidth = 0.16f;

        private static readonly Color NormalColor = new Color(1f, 0.49f, 0.09f);
        private static readonly Color OverloadColor = new Color(1f, 0.14f, 0.025f);
        private static readonly Color PeakColor = new Color(1f, 0.91f, 0.62f);

        // 显式静态构造阻止提前初始化；休眠地图和仅执行扬尘 Tick 都不会创建这些资源。
        private static class Resources
        {
            static Resources() { }

            internal static readonly Material Body = MaterialPool.MatFrom(
                TextureRoot + "ReactorStabilizerAncientBody", ShaderDatabase.Transparent);
            internal static readonly Material Base = MaterialPool.MatFrom(
                TextureRoot + "ReactorStabilizerAncientBase", ShaderDatabase.Transparent);
            internal static readonly Material Complete = MaterialPool.MatFrom(
                TextureRoot + "ReactorStabilizerAncient", ShaderDatabase.Transparent);
            internal static readonly MaterialPropertyBlock Properties = new();
            internal static readonly Mesh BaseMesh = MakeQuad(
                new Rect(217f, 443f, 782f, 811f), new Rect(0f, 0f, 1f, 1f), "Base");
            internal static readonly Mesh[] BodyMeshes = MakeBodyMeshes();

            // 坐标以原纹路画布左上角为原点；折线长度控制流速，分支起点承接输入干线的累计长度。
            internal static readonly PatternPart[] Patterns =
            {
                new PatternPart("UpperTrace", new Rect(124f, 0f, 168f, 218f), 0.53f, 0.38f,
                    new FlowPath(0f, new Vector2(285f, 213f), new Vector2(261f, 213f),
                        new Vector2(129f, 35f), new Vector2(129f, 7f))),
                new PatternPart("StatusLight", new Rect(299f, 65f, 20f, 120f), 0f, 0f),
                new PatternPart("CoreNode", new Rect(162f, 315f, 51f, 56f), 0f, 0f),
                new PatternPart("LowerTrace", new Rect(175f, 398f, 138f, 131f), 0.16f, 0.37f,
                    new FlowPath(0f, new Vector2(306f, 520f), new Vector2(211f, 520f),
                        new Vector2(182f, 487f), new Vector2(182f, 405f))),
                new PatternPart("LeftBranch", new Rect(0f, 632f, 77f, 87f), 0f, 0.28f,
                    new FlowPath(0f, new Vector2(64f, 710f), new Vector2(64f, 667f), new Vector2(42f, 667f)),
                    new FlowPath(65f, new Vector2(42f, 667f), new Vector2(42f, 681f), new Vector2(7f, 681f)),
                    new FlowPath(65f, new Vector2(42f, 667f), new Vector2(42f, 638f), new Vector2(70f, 638f))),
                new PatternPart("RightBranch", new Rect(431f, 645f, 67f, 74f), 0f, 0.28f,
                    new FlowPath(0f, new Vector2(458f, 708f), new Vector2(458f, 650f)),
                    new FlowPath(58f, new Vector2(458f, 650f), new Vector2(437f, 650f)),
                    new FlowPath(58f, new Vector2(458f, 650f), new Vector2(491f, 650f))),
                new PatternPart("BottomTrace", new Rect(163f, 683f, 11f, 36f), 0f, 0.22f,
                    new FlowPath(0f, new Vector2(168f, 711f), new Vector2(168f, 690f)))
            };
        }

        internal static void Draw(Thing building, Vector3 drawLoc)
        {
            if (building.Destroyed || !building.Spawned || building.Map != Find.CurrentMap
                || building.Position.Fogged(building.Map)) return;

            ApplyGraphicTransform(building, ref drawLoc, out Vector3 scale);
            MapComponent_SunBossArena arena = building.Map.GetComponent<MapComponent_SunBossArena>();
            arena.TryGetStabilizerActivation(building, out int elapsed, out bool powered);
            if (elapsed < DeploymentTicks)
            {
                DrawMesh(Resources.BodyMeshes[Mathf.Clamp(elapsed, 0, DeploymentTicks)],
                    Resources.Body, drawLoc, scale, Color.white, 0.004f);
                DrawMesh(Resources.BaseMesh, Resources.Base, drawLoc, scale, Color.white, 0.008f);
            }
            else
            {
                // 到位后只画整图，避免整图与拆分零件叠加导致描边变粗或半透明边缘加深。
                DrawMesh(MeshPool.plane10, Resources.Complete, drawLoc, scale, Color.white, 0.008f);
            }

            // 隐藏特效只影响灯，不隐藏建筑；BOSS 死亡后保留展开姿态并熄灯。
            if (!powered || elapsed <= DeploymentTicks || Find.UIRoot?.HideMotes == true) return;
            float power = SunSkillAnimation.Smooth((elapsed - DeploymentTicks)
                / (float)(MapComponent_SunBossArena.ActivationDurationTicks - DeploymentTicks));
            int remaining = arena.RemainingStabilizers;
            int now = Find.TickManager.TicksGame;
            int seed = building.thingIDNumber & 0x7FFFFFFF;
            float stress = Mathf.Clamp01((6f - remaining) / 5f);
            Color tint = Color.Lerp(NormalColor, OverloadColor, stress * stress);
            // 固定相位短闪，不调用 Rand，不因暂停、读档或重复绘制改变战斗随机序列。
            if (remaining <= 2 && (now % 210 + seed % 210) % 210 < 8) power *= 0.35f;
            float phase = ((elapsed - DeploymentTicks) % FlowCycleTicks + seed % 37)
                % FlowCycleTicks / (float)FlowCycleTicks;

            for (int i = 0; i < Resources.Patterns.Length; i++)
            {
                PatternPart part = Resources.Patterns[i];
                Color surface = tint;
                surface.a = power * 0.5f;
                DrawMesh(part.FullMesh, part.Surface, drawLoc, scale, surface, 0.012f);

                if (i == 1)
                {
                    float breath = Mathf.Sin((now % 240 + seed % 240) * (2f * Mathf.PI / 240f));
                    DrawGlow(part.FullMesh, part.Glow, drawLoc, scale, tint, power * (0.2f + 0.035f * breath));
                }
                else if (i == 2)
                {
                    float pulse = 1f - SunSkillAnimation.Smooth(Mathf.Abs(phase - 0.53f) / 0.12f);
                    DrawGlow(part.FullMesh, part.Glow, drawLoc, scale,
                        Color.Lerp(tint, PeakColor, pulse * 0.65f), power * (0.15f + 0.65f * pulse));
                }
                else
                {
                    DrawGlow(part.FullMesh, part.Glow, drawLoc, scale, tint, power * 0.08f);
                    part.DrawFlow(drawLoc, scale, tint, power, phase);
                }
            }
        }

        // 只从竞技场的地图 Tick 调用，不在 Draw 中喷尘，也不补发离屏期间错过的粒子。
        internal static void TickDeploymentDust(Building building, int elapsedTicks)
        {
            if (elapsedTicks < DustStartTick || elapsedTicks >= DeploymentTicks
                || (elapsedTicks - DustStartTick) % DustIntervalTicks != 0
                || building.Destroyed || !building.Spawned) return;
            Map map = building.Map;
            if (map != Find.CurrentMap || Find.UIRoot?.HideMotes == true
                || building.Position.Fogged(map)) return;

            Vector3 center = building.DrawPos;
            ApplyGraphicTransform(building, ref center, out Vector3 scale);
            float progress = elapsedTicks / (float)DeploymentTicks;
            // SmoothStep 的速度包络：中段摩擦扬尘较明显，起步和到位时减弱。
            float strength = 4f * progress * (1f - progress);
            // Fleck 初始化也使用 Rand；整个创建过程隔离，避免改变战斗随机序列。
            Rand.PushState(Gen.HashCombineInt(building.thingIDNumber, elapsedTicks));
            try
            {
                ThrowDeploymentDust(map, center, scale, 378f, 1010f, 260f, strength);
                ThrowDeploymentDust(map, center, scale, 877f, 1010f, 100f, strength);
                if ((elapsedTicks - DustStartTick) / DustIntervalTicks % 2 == 0)
                    ThrowDeploymentDust(map, center, scale, Rand.Range(440f, 810f), 1040f, 180f,
                        strength * 0.7f);
            }
            finally { Rand.PopState(); }
        }

        private static void ThrowDeploymentDust(Map map, Vector3 center, Vector3 scale,
            float pixelX, float pixelY, float angle, float strength)
        {
            Vector3 position = center + Vector3.Scale(
                PixelPosition(pixelX + Rand.Range(-10f, 10f), pixelY + Rand.Range(-12f, 12f)), scale);
            if (!position.ShouldSpawnMotesAt(map, false) || position.ToIntVec3().Fogged(map)) return;
            float size = Mathf.Lerp(0.45f, 0.85f, strength) * Rand.Range(0.85f, 1.15f)
                * Mathf.Max(scale.x, scale.z) / 6.8f;
            FleckCreationData data = FleckMaker.GetDataStatic(position, map, FleckDefOf.DustPuff, size);
            data.instanceColor = new Color(0.68f, 0.65f, 0.59f, Mathf.Lerp(0.25f, 0.45f, strength));
            data.rotation = Rand.Range(0f, 360f);
            data.rotationRate = Rand.Range(-35f, 35f);
            data.velocityAngle = angle + Rand.Range(-15f, 15f);
            data.velocitySpeed = Mathf.Lerp(0.18f, 0.4f, strength);
            map.flecks.CreateFleck(data);
        }

        private static void ApplyGraphicTransform(Thing building, ref Vector3 center, out Vector3 scale)
        {
            GraphicData? graphicData = building.def.graphicData;
            Vector2 drawSize = graphicData?.drawSize ?? new Vector2(3f, 3f);
            // 自绘不经过 Graphic.DrawWorker；图层和扬尘统一使用占地锚点及配置偏移。
            center += graphicData?.DrawOffsetForRot(building.Rotation) ?? Vector3.zero;
            // Def 使用 BuildingOnTop；再高一个标准微层，避开灯具静态网格顶部的 0.01 高度偏置。
            center.y += Altitudes.AltInc;
            scale = new Vector3(drawSize.x, 1f, drawSize.y);
        }

        private static Mesh[] MakeBodyMeshes()
        {
            var meshes = new Mesh[DeploymentTicks + 1];
            for (int i = 0; i <= DeploymentTicks; i++)
            {
                float drop = RetractedDistance * (1f - SunSkillAnimation.Smooth(i / (float)DeploymentTicks));
                float top = 159f + drop;
                float visibleHeight = Mathf.Min(935f, BodyClipY - top);
                // 同时缩短几何与 UV，保持主体像素比例；裁切边界始终埋在基座前框里面。
                meshes[i] = MakeQuad(new Rect(357f, top, 526f, visibleHeight),
                    new Rect(0f, 1f - visibleHeight / 935f, 1f, visibleHeight / 935f), "Body_" + i);
            }
            return meshes;
        }

        private static Vector3 PixelPosition(float x, float y) =>
            new Vector3(x / CanvasSize - 0.5f, 0f, 0.5f - y / CanvasSize);

        private static Mesh MakeQuad(Rect pixels, Rect uv, string name)
        {
            var builder = new MeshBuilder();
            builder.AddQuad(pixels, uv);
            return builder.Finish(name);
        }

        private static void DrawGlow(Mesh mesh, Material material, Vector3 center, Vector3 scale,
            Color color, float alpha)
        {
            color.a = alpha;
            DrawMesh(mesh, material, center, scale, color, 0.016f);
        }

        private static void DrawMesh(Mesh mesh, Material material, Vector3 center, Vector3 scale,
            Color color, float altitude)
        {
            if (color.a <= 0.001f) return;
            center.y += altitude;
            Resources.Properties.Clear();
            Resources.Properties.SetColor(ShaderPropertyIDs.Color, color);
            Graphics.DrawMesh(mesh, Matrix4x4.TRS(center, Quaternion.identity, scale),
                material, 0, null, 0, Resources.Properties);
        }

        private sealed class FlowPath
        {
            internal readonly Vector2[] Points;
            internal readonly float[] Distances;
            internal readonly float End;

            internal FlowPath(float startDistance, params Vector2[] points)
            {
                Points = points;
                Distances = new float[points.Length];
                Distances[0] = startDistance;
                for (int i = 1; i < points.Length; i++)
                    Distances[i] = Distances[i - 1] + Vector2.Distance(points[i - 1], points[i]);
                End = Distances[points.Length - 1];
            }

            internal void FindDistance(Vector2 point, ref float nearestSquared, ref float along)
            {
                for (int i = 1; i < Points.Length; i++)
                {
                    Vector2 delta = Points[i] - Points[i - 1];
                    float t = Mathf.Clamp01(Vector2.Dot(point - Points[i - 1], delta)
                        / Mathf.Max(0.001f, delta.sqrMagnitude));
                    float squared = (point - (Points[i - 1] + delta * t)).sqrMagnitude;
                    if (squared >= nearestSquared) continue;
                    nearestSquared = squared;
                    along = Mathf.Lerp(Distances[i - 1], Distances[i], t);
                }
            }
        }

        private sealed class PatternPart
        {
            internal readonly Material Surface, Glow;
            internal readonly Mesh FullMesh;
            private readonly Mesh?[] slices;
            private readonly float phaseStart, phaseDuration;

            internal PatternPart(string name, Rect bounds, float phaseStart, float phaseDuration,
                params FlowPath[] paths)
            {
                string path = TextureRoot + "PatternParts/" + name;
                Surface = MaterialPool.MatFrom(path, ShaderDatabase.Transparent);
                Glow = MaterialPool.MatFrom(path, ShaderDatabase.MoteGlow);
                this.phaseStart = phaseStart;
                this.phaseDuration = phaseDuration;
                var full = new MeshBuilder();
                full.AddPatternQuad(bounds);
                FullMesh = full.Finish(name);
                slices = new Mesh?[paths.Length > 0 ? FlowSliceCount : 0];
                if (paths.Length == 0) return;

                float length = 1f;
                foreach (FlowPath route in paths) length = Mathf.Max(length, route.End);
                var builders = new MeshBuilder?[FlowSliceCount];
                // 将遮罩平面分为互不重叠的小格，再按最近路径的累计长度分组。
                // 每格只属于一个进度段；转角和分叉不会因多条路径重画而出现亮斑。
                for (float y = bounds.yMin; y < bounds.yMax; y += FlowCellSize)
                for (float x = bounds.xMin; x < bounds.xMax; x += FlowCellSize)
                {
                    var cell = new Rect(x, y, Mathf.Min(FlowCellSize, bounds.xMax - x),
                        Mathf.Min(FlowCellSize, bounds.yMax - y));
                    float nearest = float.MaxValue;
                    float along = 0f;
                    foreach (FlowPath route in paths)
                        route.FindDistance(cell.center, ref nearest, ref along);
                    int index = Mathf.Clamp(Mathf.FloorToInt(along / length * FlowSliceCount),
                        0, FlowSliceCount - 1);
                    (builders[index] ??= new MeshBuilder()).AddPatternQuad(cell);
                }
                for (int i = 0; i < slices.Length; i++)
                    slices[i] = builders[i]?.Finish(name + "_Flow_" + i);
            }

            internal void DrawFlow(Vector3 center, Vector3 scale, Color tint, float power, float phase)
            {
                if (slices.Length == 0 || phaseDuration <= 0f
                    || phase < phaseStart || phase > phaseStart + phaseDuration) return;
                float head = Mathf.Lerp(-FlowHalfWidth, 1f + FlowHalfWidth,
                    (phase - phaseStart) / phaseDuration);
                for (int i = 0; i < slices.Length; i++)
                {
                    Mesh? mesh = slices[i];
                    if (mesh == null) continue;
                    float distance = Mathf.Abs((i + 0.5f) / FlowSliceCount - head);
                    if (distance >= FlowHalfWidth) continue;
                    float glow = 1f - SunSkillAnimation.Smooth(distance / FlowHalfWidth);
                    DrawGlow(mesh, Glow, center, scale,
                        Color.Lerp(tint, PeakColor, glow * 0.6f), power * glow * 0.75f);
                }
            }
        }

        private sealed class MeshBuilder
        {
            private readonly List<Vector3> vertices = new();
            private readonly List<Vector2> uvs = new();
            private readonly List<int> triangles = new();

            internal void AddPatternQuad(Rect pixels)
            {
                AddQuad(new Rect(PatternLeft + pixels.x, PatternTop + pixels.y, pixels.width, pixels.height),
                    new Rect(pixels.x / PatternWidth, 1f - pixels.yMax / PatternHeight,
                        pixels.width / PatternWidth, pixels.height / PatternHeight));
            }

            internal void AddQuad(Rect pixels, Rect uv)
            {
                int start = vertices.Count;
                vertices.Add(PixelPosition(pixels.xMin, pixels.yMax));
                vertices.Add(PixelPosition(pixels.xMin, pixels.yMin));
                vertices.Add(PixelPosition(pixels.xMax, pixels.yMin));
                vertices.Add(PixelPosition(pixels.xMax, pixels.yMax));
                uvs.Add(new Vector2(uv.xMin, uv.yMin));
                uvs.Add(new Vector2(uv.xMin, uv.yMax));
                uvs.Add(new Vector2(uv.xMax, uv.yMax));
                uvs.Add(new Vector2(uv.xMax, uv.yMin));
                triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
                triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
            }

            internal Mesh Finish(string name)
            {
                var mesh = new Mesh { name = "MAP_ReactorStabilizer_" + name };
                mesh.vertices = vertices.ToArray();
                mesh.uv = uvs.ToArray();
                mesh.triangles = triangles.ToArray();
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                mesh.UploadMeshData(true);
                return mesh;
            }
        }
    }
}
