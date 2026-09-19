using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>一次已释放的脉冲独立存档，离开施放者后仍以原释放点为圆心传播。</summary>
    public sealed class EnergyPulseWave : Thing
    {
        private Pawn? launcher;
        private Faction? sourceFaction;
        private float radius;
        private float speed;
        private int stunTicks;
        private int ageTicks;
        private float reachedRadius;
        private HashSet<Pawn> hitPawns = new HashSet<Pawn>();

        private int TravelTicks => Mathf.Max(1, Mathf.CeilToInt(radius / speed));

        internal void Initialize(Pawn actor, CompProperties_EnergyPulse settings)
        {
            launcher = actor;
            // 固定释放瞬间的派系；释放者转派系、离图或死亡不改变这一波的归属。
            sourceFaction = actor.Faction;
            radius = settings.radius;
            speed = settings.propagationSpeed;
            stunTicks = settings.stunTicks;
        }

        protected override void Tick()
        {
            if (!(radius > 0f) || !(speed > 0f) || stunTicks < 1)
            {
                Destroy();
                return;
            }
            ageTicks++;
            if (ageTicks <= TravelTicks)
            {
                float front = Mathf.Min(radius, ageTicks * speed);
                // 每 tick 按目标当前格判定扫过的波带；不预锁目标，不在波后持续施加眩晕。
                // 半格容差覆盖 Pawn 所占格，避免逐 tick 离散判定漏掉穿过波面的移动目标。
                foreach (Pawn target in Map.mapPawns.AllPawnsSpawned)
                {
                    if (target == launcher || target.Dead || target.Downed || !target.RaceProps.IsMechanoid
                        || target.stances?.stunner == null || hitPawns.Contains(target)
                        // 按派系身份排除同阵营，不以外交敌对关系筛选，中立/盟友也可命中。
                        // 无派系不是共同阵营；释放者自身由上面的引用比较排除。
                        || (sourceFaction != null && target.Faction == sourceFaction)) continue;
                    float distance = Position.DistanceTo(target.Position);
                    if (distance > radius || distance > front || distance + 0.5f < reachedRadius) continue;
                    // 波前抵达时按原释放点检查遮挡；墙后的目标不结算，原版视觉仍完整播放。
                    if (!GenSight.LineOfSight(Position, target.Position, Map, skipFirstCell: true)) continue;
                    hitPawns.Add(target);
                    // 直接眩晕，不经过 EMP 伤害、抗性折算或适应判定。
                    target.stances.stunner.StunFor(stunTicks, launcher, addBattleLog: true);
                }
                reachedRadius = front;
            }
            if (ageTicks >= TravelTicks) Destroy(DestroyMode.Vanish);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref launcher, "launcher");
            Scribe_References.Look(ref sourceFaction, "sourceFaction");
            Scribe_Values.Look(ref radius, "radius");
            Scribe_Values.Look(ref speed, "speed");
            Scribe_Values.Look(ref stunTicks, "stunTicks");
            Scribe_Values.Look(ref ageTicks, "ageTicks");
            Scribe_Values.Look(ref reachedRadius, "reachedRadius");
            Scribe_Collections.Look(ref hitPawns, "hitPawns", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                hitPawns ??= new HashSet<Pawn>();
                hitPawns.RemoveWhere(pawn => pawn == null);
            }
        }
    }
}
