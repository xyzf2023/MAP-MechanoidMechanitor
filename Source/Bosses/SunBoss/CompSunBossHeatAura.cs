using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class CompProperties_SunBossHeatAura : CompProperties
    {
        public CompProperties_SunBossHeatAura()
        {
            compClass = typeof(CompSunBossHeatAura);
        }
    }

    /// <summary>太阳 BOSS 专属近身热场；按所在格中心的距离分档，不区分阵营。</summary>
    public sealed class CompSunBossHeatAura : ThingComp
    {
        private const float Radius = 3.5f;
        private const int IntervalTicks = 20;
        private const int HeatGlowIntervalTicks = 60;
        private const float HeatGlowSizeFactor = 0.4f;
        private const float BaseDamage = 10f;
        private const float BaseHeatstrokeSeverity = 0.01f;
        private readonly List<Pawn> targets = new List<Pawn>();

        public override void CompTick()
        {
            if (!(parent is Pawn boss) || !boss.Spawned || boss.Destroyed
                || boss.Dead || boss.Suspended)
                return;

            Map map = boss.Map;
            float activeRadius = boss.GetComp<CompSunBossState>()?.Stage.HeatRadius ?? Radius;
            // 原版光晕持续 7.4 秒，每秒补充一个，叠加成持续高温提示。
            // ThrowHeatGlow 会将 size 再乘以 4～6；这里只取半径的一部分。
            if (boss.IsHashIntervalTick(HeatGlowIntervalTicks) && !boss.Position.Fogged(map))
                FleckMaker.ThrowHeatGlow(boss.Position, map, activeRadius * HeatGlowSizeFactor);

            if (!boss.IsHashIntervalTick(IntervalTicks)) return;

            IntVec3 center = boss.Position;
            targets.Clear();
            // 先取快照，避免伤害致死或离图回调修改地图 Pawn 列表。
            foreach (Pawn target in map.mapPawns.AllPawnsSpawned)
            {
                if (target != boss && !target.Dead && !target.Destroyed
                    && target.Position.DistanceToSquared(center) <= activeRadius * activeRadius)
                    targets.Add(target);
            }

            foreach (Pawn target in targets)
            {
                // 伤害回调也可能移除施加者，不能继续由失效来源结算热场。
                if (!boss.Spawned || boss.Destroyed || boss.Dead || boss.Map != map)
                    break;
                if (!target.Spawned || target.Destroyed || target.Dead
                    || target.Map != map || target.health == null)
                    continue;

                float distance = Mathf.Sqrt(target.Position.DistanceToSquared(center));
                if (distance > activeRadius) continue;

                // (2.5, 3.5] / (1.5, 2.5] / (0.5, 1.5] / [0, 0.5]
                // 边界每向内满一格即提升一档，因此 2.5、1.5、0.5 属于内档。
                // 分档始终以原始 3.5 格计算，扩大的外圈仅为最低档。
                int depth = Mathf.Clamp(Mathf.FloorToInt(Radius - distance), 0, 3);
                var damage = new DamageInfo(DamageDefOf.Burn, BaseDamage * (1 << depth),
                    instigator: boss);
                damage.SetIgnoreArmor(true);
                // 在伤害前记录阵营，避免致死回调改变目标状态而漏发警告。
                bool playerOwned = target.Faction == Faction.OfPlayer;
                DamageWorker.DamageResult result = target.TakeDamage(damage);
                if (playerOwned && result.totalDamageDealt > 0f)
                    CurrentGameComponentCache<GameComponent_SunBossNotifications>.Get()
                        ?.NotifyHeatDamage(target);

                // 原版严重度 1 为 100%；复用已有中暑，避免重复添加健康状态。
                if (!target.Dead && !target.Destroyed && target.Spawned && target.Map == map)
                    HealthUtility.AdjustSeverity(target, HediffDefOf.Heatstroke,
                        BaseHeatstrokeSeverity * Mathf.Min(depth + 1, 3));
            }
            targets.Clear();
        }
    }
}
