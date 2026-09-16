using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>仅维护本地图已展开力场的运行时索引，读档由 Pawn Comp 重新登记。</summary>
    public sealed class MapComponent_GravityFieldTracker : MapComponent
    {
        private readonly List<CompGravityField> fields = new();

        public MapComponent_GravityFieldTracker(Map map) : base(map) { }

        public void Register(CompGravityField field)
        {
            if (!fields.Contains(field))
                fields.Add(field);
        }

        public void Deregister(CompGravityField field) => fields.Remove(field);

        public bool TryIntercept(Projectile projectile, Vector3 start, Vector3 end)
        {
            if (!projectile.Spawned || projectile.Destroyed || projectile.Map != map)
                return false;
            for (int i = 0; i < fields.Count; i++)
            {
                CompGravityField field = fields[i];
                if (CanIntercept(field, projectile)
                    && SegmentTouchesCircle(field.Center, field.Props.radius, start, end))
                {
                    // Impact(null, true) 会使原版爆炸弹引爆；直接销毁才是彻底无效化。
                    projectile.Destroy(DestroyMode.Vanish);
                    return true;
                }
            }
            return false;
        }

        public void SweepExistingProjectiles(CompGravityField field)
        {
            if (!field.Active || field.parent.Map != map)
                return;
            // 仅展开和移动到新格子时扫描，覆盖已经落地的延时爆炸弹。
            List<Thing> projectiles = map.listerThings.ThingsInGroup(ThingRequestGroup.Projectile);
            for (int i = projectiles.Count - 1; i >= 0; i--)
            {
                if (projectiles[i] is Projectile projectile && projectile.Spawned
                    && CanIntercept(field, projectile)
                    && Contains(field, projectile.ExactPosition))
                    projectile.Destroy(DestroyMode.Vanish);
            }
        }

        public bool SuppressesExplosion(IntVec3 center)
        {
            if (!center.IsValid)
                return false;
            Vector3 point = center.ToVector3Shifted();
            for (int i = 0; i < fields.Count; i++)
            {
                CompGravityField field = fields[i];
                if (field.parent.Map == map && field.Active && Contains(field, point))
                    return true;
            }
            return false;
        }

        private bool CanIntercept(CompGravityField field, Projectile projectile)
        {
            if (field.parent.Map != map || !field.Active)
                return false;
            Thing? launcher = projectile.Launcher;
            if (launcher != null)
            {
                // 发射者已被摧毁时 HostileTo(Thing) 返回 false，仍须保留阵营判定。
                return launcher.HostileTo(field.parent)
                    || (launcher.Destroyed && launcher.Faction != null
                        && field.parent.Faction != null
                        && launcher.Faction.HostileTo(field.parent.Faction));
            }
            // 无来源射弹只有存在明确敌对阵营时才拦截，不把中立/未知来源当成敌人。
            return projectile.Faction != null && field.parent.Faction != null
                && projectile.Faction.HostileTo(field.parent.Faction);
        }

        private static bool Contains(CompGravityField field, Vector3 point)
        {
            Vector3 offset = point - field.Center;
            return offset.x * offset.x + offset.z * offset.z
                <= field.Props.radius * field.Props.radius;
        }

        private static bool SegmentTouchesCircle(Vector3 center, float radius,
            Vector3 start, Vector3 end)
        {
            Vector2 offset = new Vector2(center.x - start.x, center.z - start.z);
            Vector2 segment = new Vector2(end.x - start.x, end.z - start.z);
            float t = segment.sqrMagnitude > Mathf.Epsilon
                ? Mathf.Clamp01(Vector2.Dot(offset, segment) / segment.sqrMagnitude) : 0f;
            return (offset - segment * t).sqrMagnitude <= radius * radius;
        }
    }
}
