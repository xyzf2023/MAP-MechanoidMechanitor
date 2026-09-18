using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>拦截后的无伤害外观快照，不保留 Projectile，也不进入命中、爆炸或存档流程。</summary>
    internal sealed class GravityFieldProjectileVisual
    {
        private const int LengthSamples = 32;
        private const float OutsideDistance = 4f;
        private static readonly AccessTools.FieldRef<Projectile, bool> Landed =
            AccessTools.FieldRefAccess<Projectile, bool>("landed");

        private readonly Material material;
        private readonly Mesh mesh;
        private readonly Vector3 entry;
        private readonly Vector3 controlA;
        private readonly Vector3 controlB;
        private readonly Vector3 exit;
        private readonly Vector3 exitDirection;
        private readonly float[] lengths = new float[LengthSamples + 1];
        private readonly float speed;
        private readonly int createdTick;
        private Vector3 position;
        private Vector3 direction;

        private GravityFieldProjectileVisual(Material material, Vector2 drawSize,
            Vector3 entry, Vector3 direction, Vector3 center, float radius,
            float forwardDistance, float speed, int seed)
        {
            this.material = material;
            mesh = MeshPool.GridPlane(drawSize);
            this.entry = entry;
            this.direction = direction;
            this.speed = speed;
            position = entry;
            createdTick = GenTicks.TicksGame;

            Vector3 inward = (center - entry).Yto0();
            float side = Vector3.Cross(direction, inward).y;
            // 正对圆心时用射弹 ID 决定偏转方向，不消耗游戏随机数。
            if (Mathf.Abs(side) < 0.001f)
                side = (seed & 1) == 0 ? 1f : -1f;
            exitDirection = Quaternion.AngleAxis(Mathf.Sign(side) * 40f, Vector3.up) * direction;
            exit = center + exitDirection * radius;
            exit.y = entry.y;

            // 四个控制点均在圆内，曲线不会提前穿出护盾；入射、出射切线保持连续。
            controlA = entry + direction * Mathf.Min(radius * 0.7f, forwardDistance * 0.4f);
            controlB = exit - exitDirection * (radius * 0.65f);
            Vector3 previous = entry;
            for (int i = 1; i <= LengthSamples; i++)
            {
                Vector3 point = Curve((float)i / LengthSamples);
                lengths[i] = lengths[i - 1] + Vector3.Distance(previous, point);
                previous = point;
            }
        }

        internal static GravityFieldProjectileVisual? TryCreate(Projectile projectile,
            Vector3 center, float radius, Vector3 start, Vector3 end)
        {
            if (projectile is Beam || Landed(projectile) || radius <= 0f
                || projectile.def.graphicData == null || projectile.def.projectile == null)
                return null;

            Vector3 direction = (end - start).Yto0();
            if (direction.sqrMagnitude < 0.000001f)
                direction = (projectile.ExactRotation * Vector3.forward).Yto0();
            if (direction.sqrMagnitude < 0.000001f)
                return null;
            direction.Normalize();

            Vector3 entry = start;
            Vector3 offset = (start - center).Yto0();
            float projection = Vector3.Dot(offset, direction);
            float discriminant = projection * projection - (offset.sqrMagnitude - radius * radius);
            if (discriminant < -0.0001f)
                return null;
            float root = Mathf.Sqrt(Mathf.Max(0f, discriminant));
            if (offset.sqrMagnitude > radius * radius)
            {
                float distance = -projection - root;
                if (distance < 0f || distance > (end - start).Yto0().magnitude + 0.001f)
                    return null;
                entry += direction * distance;
            }

            offset = (entry - center).Yto0();
            projection = Vector3.Dot(offset, direction);
            float forwardDistance = -projection + Mathf.Sqrt(Mathf.Max(0f,
                projection * projection + radius * radius - offset.sqrMagnitude));
            // 精确擦边或已经朝外离开的弹丸没有可见的内部弧段，保留正常消除。
            if (forwardDistance < 0.01f)
                return null;

            Material material = projectile.DrawMat;
            if (material == null)
                return null;
            // 沿用原版速度单位（格/tick），限制极端速度以保证短暂、可见的偏转。
            float speed = Mathf.Clamp(projectile.def.projectile.SpeedTilesPerTick, 0.15f, 0.8f);
            return new GravityFieldProjectileVisual(material, projectile.def.graphicData.drawSize,
                entry, direction, center, radius, forwardDistance, speed, projectile.thingIDNumber);
        }

        internal bool Tick(Map map)
        {
            float travelled = (GenTicks.TicksGame - createdTick) * speed;
            float curveLength = lengths[LengthSamples];
            if (travelled >= curveLength)
            {
                position = exit + exitDirection * Mathf.Min(travelled - curveLength, OutsideDistance);
                direction = exitDirection;
            }
            else
            {
                int sample = 1;
                while (sample < LengthSamples && lengths[sample] < travelled)
                    sample++;
                float fraction = Mathf.InverseLerp(lengths[sample - 1], lengths[sample], travelled);
                float t = (sample - 1 + fraction) / LengthSamples;
                position = Curve(t);
                float inverse = 1f - t;
                direction = 3f * inverse * inverse * (controlA - entry)
                    + 6f * inverse * t * (controlB - controlA)
                    + 3f * t * t * (exit - controlB);
            }

            if (!position.InBounds(map))
                return false;
            if (travelled < curveLength + OutsideDistance)
                return true;

            // 只生成落地表现；不调用 Impact、伤害、点火、爆炸或战斗日志。
            if (position.ToIntVec3().GetTerrain(map).takeSplashes)
                FleckMaker.WaterSplash(position, map, 1f, 4f);
            else
                FleckMaker.Static(position, map, FleckDefOf.ShotHit_Dirt);
            return false;
        }

        internal void Draw()
        {
            if (material != null && direction.sqrMagnitude > 0.000001f)
                Graphics.DrawMesh(mesh, position, Quaternion.LookRotation(direction.Yto0()), material, 0);
        }

        private Vector3 Curve(float t)
        {
            float inverse = 1f - t;
            return inverse * inverse * inverse * entry + 3f * inverse * inverse * t * controlA
                + 3f * inverse * t * t * controlB + t * t * t * exit;
        }
    }
}
