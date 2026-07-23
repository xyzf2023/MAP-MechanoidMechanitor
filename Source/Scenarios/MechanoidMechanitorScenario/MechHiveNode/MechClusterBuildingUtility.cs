using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 机械集群建筑相关的公共工具，供“肃清指令—集群部署”与“机械巢节点”共用。
    /// 集中维护：机械集群建筑候选筛选、地图状态建筑候选筛选、自动迫击炮排除、
    /// 建筑威胁判定、地图状态建筑放置、高低角护盾识别。
    /// 通过原版建筑标签（MechClusterMember / MechClusterProblemCauser）读取，
    /// 兼容其他 MOD 通过标签或 Def 扩展的建筑列表，避免维护易失同步的硬编码名单。
    /// </summary>
    public static class MechClusterBuildingUtility
    {
        public const string MemberTag = "MechClusterMember";

        public const string ProblemCauserTag = "MechClusterProblemCauser";

        // 原版机械集群自动迫击炮；从可选/生成的状态建筑中排除。
        public const string ExcludedAutoMortarDefName = "Turret_AutoMortar";

        /// <summary>
        /// 返回当前威胁点数下可用的地图状态建筑（状态制造者）候选列表。
        /// 与集群部署使用同一判定，保证两处一致。
        /// </summary>
        public static List<ThingDef> GetConditionCausers(int threatPoints)
        {
            List<ThingDef> result = new List<ThingDef>();
            List<ThingDef> defs = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                ThingDef def = defs[i];
                if (IsConditionCauser(def, threatPoints))
                {
                    result.Add(def);
                }
            }

            result.Sort((a, b) =>
                string.Compare(a.LabelCap, b.LabelCap, StringComparison.CurrentCulture));
            return result;
        }

        public static bool IsConditionCauser(ThingDef? def, int threatPoints)
        {
            if (def?.building?.buildingTags == null
                || def.category != ThingCategory.Building
                || def.thingClass == null
                || !typeof(Building).IsAssignableFrom(def.thingClass)
                || def.building.minMechClusterPoints > threatPoints
                || IsExcludedConditionCauser(def))
            {
                return false;
            }

            List<string> tags = def.building.buildingTags;
            return tags.Contains(MemberTag) && tags.Contains(ProblemCauserTag);
        }

        public static bool IsExcludedConditionCauser(ThingDef def)
        {
            return def.defName == ExcludedAutoMortarDefName;
        }

        /// <summary>
        /// 清理草图中随机生成的状态建筑（问题制造者）。排除规则仅针对可选/额外添加流程。
        /// </summary>
        public static void RemoveGeneratedProblemCausers(Sketch sketch)
        {
            if (sketch == null)
            {
                return;
            }

            List<SketchThing> things = sketch.Things;
            for (int i = things.Count - 1; i >= 0; i--)
            {
                SketchThing thing = things[i];
                if (thing.def?.building?.buildingTags != null
                    && thing.def.building.buildingTags.Contains(MemberTag)
                    && thing.def.building.buildingTags.Contains(ProblemCauserTag))
                {
                    sketch.Remove(thing);
                }
            }
        }

        /// <summary>
        /// 向机械集群草图中放置一个指定的地图状态建筑，避免与建筑/守军重叠。
        /// </summary>
        public static bool TryAddConditionCauser(
            MechClusterSketch cluster,
            ThingDef conditionCauser)
        {
            if (cluster?.buildingsSketch == null || conditionCauser == null)
            {
                return false;
            }

            Sketch sketch = cluster.buildingsSketch;
            CellRect searchRect = sketch.OccupiedRect.ExpandedBy(6);
            List<IntVec3> cells = searchRect.Cells.ToList();
            IntVec3 center = searchRect.CenterCell;
            cells.Sort((a, b) =>
                DistanceSquared(a, center).CompareTo(DistanceSquared(b, center)));

            for (int i = 0; i < cells.Count; i++)
            {
                IntVec3 cell = cells[i];
                if (sketch.WouldCollide(conditionCauser, cell, Rot4.North)
                    || OverlapsMechSpawn(cluster, conditionCauser, cell))
                {
                    continue;
                }

                ThingDef? stuff = null;
                try
                {
                    stuff = GenStuff.RandomStuffByCommonalityFor(conditionCauser);
                }
                catch (Exception)
                {
                    if (conditionCauser.MadeFromStuff)
                    {
                        continue;
                    }
                }

                if (sketch.AddThing(
                        conditionCauser,
                        cell,
                        Rot4.North,
                        stuff,
                        wipeIfCollides: false))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 建筑是否属于“有实际战斗或地图威胁”的机械集群建筑：
        /// 炮塔、会苏醒生成 Pawn 的建筑、机械族生成器、地图状态建筑（含护盾）。
        /// </summary>
        public static bool IsBuildingThreat(Thing b)
        {
            if (b == null)
            {
                return false;
            }

            CompPawnSpawnOnWakeup? spawnOnWakeup = b.TryGetComp<CompPawnSpawnOnWakeup>();
            if (spawnOnWakeup != null && spawnOnWakeup.CanSpawn)
            {
                return true;
            }

            CompSpawnerPawn? spawnerPawn = b.TryGetComp<CompSpawnerPawn>();
            if (spawnerPawn != null && spawnerPawn.pawnsLeftToSpawn != 0)
            {
                return true;
            }

            if (b.TryGetComp<CompProjectileInterceptor>() != null)
            {
                return true;
            }

            if (b.def?.building != null && b.def.building.IsTurret)
            {
                return true;
            }

            return b.TryGetComp<CompCauseGameCondition>() != null;
        }

        /// <summary>高低角护盾识别（护盾拦截器）。</summary>
        public static bool IsShield(Thing thing)
        {
            return thing != null && thing.TryGetComp<CompProjectileInterceptor>() != null;
        }

        /// <summary>低角护盾（拦截子弹）建筑定义；Royalty 未启用时返回 false。</summary>
        public static bool TryGetLowAngleShieldDef(out ThingDef def)
        {
            def = MechHiveNodeDefOf.ShieldGeneratorBullets!;
            return def != null;
        }

        /// <summary>高角护盾（拦截迫击炮）建筑定义；Royalty 未启用时返回 false。</summary>
        public static bool TryGetHighAngleShieldDef(out ThingDef def)
        {
            def = MechHiveNodeDefOf.ShieldGeneratorMortar!;
            return def != null;
        }

        /// <summary>
        /// 机械族专用炮塔候选：带机械集群成员标签、且为炮塔、排除自动迫击炮。
        /// 通过标签读取，兼容其他 MOD 追加的机械族炮塔；不包含人类据点炮塔。
        /// </summary>
        public static List<ThingDef> GetMechTurretDefs()
        {
            List<ThingDef> result = new List<ThingDef>();
            List<ThingDef> defs = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                ThingDef def = defs[i];
                if (IsMechTurret(def))
                {
                    result.Add(def);
                }
            }

            return result;
        }

        public static bool IsMechTurret(ThingDef? def)
        {
            if (def?.building == null
                || def.category != ThingCategory.Building
                || !def.building.IsTurret
                || def.defName == ExcludedAutoMortarDefName)
            {
                return false;
            }

            List<string>? tags = def.building.buildingTags;
            return tags != null && tags.Contains(MemberTag);
        }

        private static bool OverlapsMechSpawn(
            MechClusterSketch cluster,
            ThingDef def,
            IntVec3 position)
        {
            CellRect occupied = GenAdj.OccupiedRect(position, Rot4.North, def.Size);
            for (int i = 0; i < cluster.pawns.Count; i++)
            {
                if (occupied.Contains(cluster.pawns[i].position))
                {
                    return true;
                }
            }

            return false;
        }

        private static int DistanceSquared(IntVec3 a, IntVec3 b)
        {
            int dx = a.x - b.x;
            int dz = a.z - b.z;
            return dx * dx + dz * dz;
        }
    }
}
