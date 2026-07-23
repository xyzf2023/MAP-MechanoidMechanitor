using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.SketchGen;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 机械集群建筑相关的公共工具，供“肃清指令—集群部署”与“机械巢节点”共用。
    /// 集中维护：机械集群建筑候选筛选、地图状态建筑候选筛选、自动迫击炮排除、
    /// 供电设施排除、建筑初始状态设置、高低角护盾识别、完整节点集群草图生成与修正。
    /// 通过原版建筑标签（MechClusterMember / MechClusterProblemCauser）读取，
    /// 兼容其他 MOD 通过标签或 Def 扩展的建筑列表，避免维护易失同步的硬编码名单。
    /// </summary>
    public static class MechClusterBuildingUtility
    {
        public const string MemberTag = "MechClusterMember";

        public const string ProblemCauserTag = "MechClusterProblemCauser";

        public const string CombatThreatTag = "MechClusterCombatThreat";

        // 原版机械集群自动迫击炮；从可选/生成的状态建筑中排除。
        public const string ExcludedAutoMortarDefName = "Turret_AutoMortar";

        /// <summary>完整节点建筑草图预算（与守军预算独立，不互相扣减）。</summary>
        public const float CompletedNodeBuildingPoints = 10000f;

        private static readonly FloatRange FallbackSizeRandomFactorRange = new FloatRange(0.8f, 2f);

        private static readonly SimpleCurve FallbackPointsToSizeCurve = new SimpleCurve
        {
            new CurvePoint(400f, 7f),
            new CurvePoint(1000f, 10f),
            new CurvePoint(2000f, 20f),
            new CurvePoint(5000f, 25f)
        };

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
                || IsExcludedFromNodeCluster(def))
            {
                return false;
            }

            List<string> tags = def.building.buildingTags;
            return tags.Contains(MemberTag) && tags.Contains(ProblemCauserTag);
        }

        public static bool IsExcludedConditionCauser(ThingDef def)
        {
            return def != null && def.defName == ExcludedAutoMortarDefName;
        }

        /// <summary>
        /// 电缆、电池、发电机等供电设施：节点集群草图中永远排除。
        /// </summary>
        public static bool IsExcludedPowerFacility(ThingDef? def)
        {
            if (def == null)
            {
                return false;
            }

            if (def == ThingDefOf.PowerConduit
                || def.defName == "PowerConduit"
                || def.defName == "HiddenConduit")
            {
                return true;
            }

            if (def.GetCompProperties<CompProperties_Battery>() != null)
            {
                return true;
            }

            CompProperties_Power? power = def.GetCompProperties<CompProperties_Power>();
            return power != null;
        }

        /// <summary>节点集群草图中应排除的建筑（自动迫击炮、供电设施）。</summary>
        public static bool IsExcludedFromNodeCluster(ThingDef? def)
        {
            if (def == null)
            {
                return true;
            }

            return IsExcludedConditionCauser(def) || IsExcludedPowerFacility(def);
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

        /// <summary>从草图中移除自动迫击炮与供电设施。</summary>
        public static void RemoveExcludedNodeClusterBuildings(Sketch sketch)
        {
            if (sketch == null)
            {
                return;
            }

            List<SketchThing> things = sketch.Things;
            for (int i = things.Count - 1; i >= 0; i--)
            {
                if (IsExcludedFromNodeCluster(things[i].def))
                {
                    sketch.Remove(things[i]);
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
            return TryAddBuildingToSketch(cluster, conditionCauser);
        }

        /// <summary>向草图安全追加一座建筑。</summary>
        public static bool TryAddBuildingToSketch(MechClusterSketch? cluster, ThingDef? def)
        {
            return TryAddBuildingToSketch(cluster, def, expandBy: 6);
        }

        /// <summary>向草图安全追加一座建筑；expandBy 控制搜索外扩格数。</summary>
        public static bool TryAddBuildingToSketch(
            MechClusterSketch? cluster,
            ThingDef? def,
            int expandBy)
        {
            if (cluster?.buildingsSketch == null || def == null || IsExcludedFromNodeCluster(def))
            {
                return false;
            }

            Sketch sketch = cluster.buildingsSketch;
            CellRect searchRect = sketch.OccupiedRect.ExpandedBy(Mathf.Max(0, expandBy));
            if (searchRect.Area <= 0)
            {
                searchRect = new CellRect(0, 0, 12, 12);
            }

            List<IntVec3> cells = searchRect.Cells.ToList();
            IntVec3 center = searchRect.CenterCell;
            cells.Sort((a, b) =>
                DistanceSquared(a, center).CompareTo(DistanceSquared(b, center)));

            for (int i = 0; i < cells.Count; i++)
            {
                IntVec3 cell = cells[i];
                if (sketch.WouldCollide(def, cell, Rot4.North)
                    || OverlapsMechSpawn(cluster, def, cell))
                {
                    continue;
                }

                ThingDef? stuff = null;
                try
                {
                    stuff = GenStuff.RandomStuffByCommonalityFor(def);
                }
                catch (Exception)
                {
                    if (def.MadeFromStuff)
                    {
                        continue;
                    }
                }

                if (sketch.AddThing(
                        def,
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

        /// <summary>Royalty 启用时草图是否已具备低角护盾、高角护盾与状态建筑。</summary>
        public static bool SketchHasRequiredRoyaltyBuildings(
            MechClusterSketch? sketch,
            float buildingPoints)
        {
            if (!ModsConfig.RoyaltyActive || sketch?.buildingsSketch == null)
            {
                return !ModsConfig.RoyaltyActive;
            }

            return EvaluateSketchRoyaltyRequirements(
                sketch,
                buildingPoints,
                out _,
                out _,
                out _);
        }

        private static bool EvaluateSketchRoyaltyRequirements(
            MechClusterSketch sketch,
            float buildingPoints,
            out bool hasLow,
            out bool hasHigh,
            out bool hasCauser)
        {
            hasLow = false;
            hasHigh = false;
            hasCauser = false;
            List<SketchThing> things = sketch.buildingsSketch.Things;
            int points = Mathf.RoundToInt(buildingPoints);
            for (int i = 0; i < things.Count; i++)
            {
                ThingDef? def = things[i].def;
                if (IsLowAngleShieldDef(def))
                {
                    hasLow = true;
                }

                if (IsHighAngleShieldDef(def))
                {
                    hasHigh = true;
                }

                if (def != null && IsConditionCauser(def, points))
                {
                    hasCauser = true;
                }
            }

            return hasLow && hasHigh && hasCauser;
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
            def = null!;
            if (!ModsConfig.RoyaltyActive)
            {
                return false;
            }

            def = MechHiveNodeDefOf.ShieldGeneratorBullets!;
            return def != null;
        }

        /// <summary>高角护盾（拦截迫击炮）建筑定义；Royalty 未启用时返回 false。</summary>
        public static bool TryGetHighAngleShieldDef(out ThingDef def)
        {
            def = null!;
            if (!ModsConfig.RoyaltyActive)
            {
                return false;
            }

            def = MechHiveNodeDefOf.ShieldGeneratorMortar!;
            return def != null;
        }

        public static bool IsLowAngleShieldDef(ThingDef? def)
        {
            return TryGetLowAngleShieldDef(out ThingDef low) && def == low;
        }

        public static bool IsHighAngleShieldDef(ThingDef? def)
        {
            return TryGetHighAngleShieldDef(out ThingDef high) && def == high;
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
                || IsExcludedFromNodeCluster(def))
            {
                return false;
            }

            List<string>? tags = def.building.buildingTags;
            return tags != null && tags.Contains(MemberTag);
        }

        /// <summary>
        /// 机械集群战斗威胁建筑候选（炮塔与威胁结构），兼容其他 MOD 扩展；排除迫击炮与供电设施。
        /// </summary>
        public static List<ThingDef> GetMechCombatThreatDefs(float totalPoints)
        {
            List<ThingDef> result = new List<ThingDef>();
            List<ThingDef> defs = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                ThingDef def = defs[i];
                if (def?.building?.buildingTags == null
                    || def.category != ThingCategory.Building
                    || IsExcludedFromNodeCluster(def)
                    || (float)def.building.minMechClusterPoints > totalPoints)
                {
                    continue;
                }

                List<string> tags = def.building.buildingTags;
                if (tags.Contains(MemberTag) && tags.Contains(CombatThreatTag))
                {
                    result.Add(def);
                }
            }

            return result;
        }

        /// <summary>
        /// 为完整机械巢节点生成机械集群式建筑草图（不含守军 Pawn）。
        /// Royalty 启用时使用原版 MechClusterGenerator，并确保高低角护盾与一种状态建筑；
        /// Royalty 关闭时使用战斗威胁建筑草图，绝不访问 Royalty Def。
        /// </summary>
        public static bool TryGenerateCompletedNodeBuildingSketch(
            Map map,
            float buildingPoints,
            out MechClusterSketch sketch)
        {
            sketch = null!;
            if (map == null)
            {
                return false;
            }

            buildingPoints = Mathf.Clamp(buildingPoints, 400f, MechClusterGenerator.MaxPoints);

            try
            {
                if (ModsConfig.RoyaltyActive)
                {
                    // 直接生成建筑草图，不走 GenerateClusterSketch 的机械族点数分流，
                    // 保证建筑预算与守军预算完全分离。
                    Sketch buildingsSketch = RimWorld.SketchGen.SketchGen.Generate(
                        SketchResolverDefOf.MechCluster,
                        new SketchResolveParams
                        {
                            points = buildingPoints,
                            totalPoints = buildingPoints,
                            mechClusterDormant = true,
                            sketch = new Sketch(),
                            mechClusterForMap = map,
                            forceNoConditionCauser = true
                        });
                    sketch = new MechClusterSketch(
                        buildingsSketch ?? new Sketch(),
                        new List<MechClusterSketch.Mech>(),
                        startDormant: true);
                }
                else
                {
                    sketch = GenerateCombatThreatClusterSketch(buildingPoints, map);
                }

                if (sketch?.buildingsSketch == null || !HasAnySketchContent(sketch.buildingsSketch))
                {
                    sketch = GenerateCombatThreatClusterSketch(buildingPoints, map);
                }

                if (sketch.pawns == null)
                {
                    sketch.pawns = new List<MechClusterSketch.Mech>();
                }
                else
                {
                    // 守军另由 Combat 模板生成，建筑草图不携带集群机械族。
                    sketch.pawns.Clear();
                }

                RemoveExcludedNodeClusterBuildings(sketch.buildingsSketch);
                RemoveGeneratedProblemCausers(sketch.buildingsSketch);

                RemoveExcludedNodeClusterBuildings(sketch.buildingsSketch);

                if (ModsConfig.RoyaltyActive)
                {
                    if (!EnsureRoyaltyRequiredBuildings(sketch, buildingPoints))
                    {
                        Log.Error(
                            "[MAP] 完整机械巢节点草图在补充后仍缺少必需的低角护盾、高角护盾或地图状态建筑，终止该节点布局生成。");
                        return false;
                    }
                }

                return HasAnySketchContent(sketch.buildingsSketch);
            }
            catch (Exception ex)
            {
                Log.Warning("[MAP] 完整机械巢节点集群草图生成失败，尝试回退战斗威胁草图: " + ex);
                try
                {
                    sketch = GenerateCombatThreatClusterSketch(buildingPoints, map);
                    if (sketch.pawns == null)
                    {
                        sketch.pawns = new List<MechClusterSketch.Mech>();
                    }
                    else
                    {
                        sketch.pawns.Clear();
                    }

                    RemoveExcludedNodeClusterBuildings(sketch.buildingsSketch);
                    if (ModsConfig.RoyaltyActive
                        && !EnsureRoyaltyRequiredBuildings(sketch, buildingPoints))
                    {
                        Log.Error(
                            "[MAP] 完整机械巢节点回退草图仍缺少必需的 Royalty 建筑，终止该节点布局生成。");
                        return false;
                    }

                    return HasAnySketchContent(sketch.buildingsSketch);
                }
                catch (Exception ex2)
                {
                    Log.Error("[MAP] 完整机械巢节点战斗威胁草图回退也失败: " + ex2);
                    return false;
                }
            }
        }

        /// <summary>状态建筑落地后立即生效（1 tick 初始化）。</summary>
        public static void ApplyImmediateInitiation(Thing thing)
        {
            CompInitiatable? initiatable = thing?.TryGetComp<CompInitiatable>();
            if (initiatable != null)
            {
                initiatable.initiationDelayTicksOverride = 1;
            }
        }

        /// <summary>建筑进入休眠状态。</summary>
        public static void ApplyDormantSleep(Thing thing)
        {
            thing?.TryGetComp<CompCanBeDormant>()?.ToSleep();
        }

        private const int MaxSketchSupplementRounds = 4;

        /// <summary>
        /// 补充并复查草图必需建筑。任一类补充失败或复查未齐则返回 false。
        /// </summary>
        private static bool EnsureRoyaltyRequiredBuildings(
            MechClusterSketch sketch,
            float buildingPoints)
        {
            if (!ModsConfig.RoyaltyActive || sketch?.buildingsSketch == null)
            {
                return !ModsConfig.RoyaltyActive;
            }

            for (int round = 0; round < MaxSketchSupplementRounds; round++)
            {
                EvaluateSketchRoyaltyRequirements(
                    sketch,
                    buildingPoints,
                    out bool hasLow,
                    out bool hasHigh,
                    out bool hasCauser);

                if (hasLow && hasHigh && hasCauser)
                {
                    return true;
                }

                int expandBy = 6 + round * 4;
                if (!hasLow)
                {
                    if (!TryGetLowAngleShieldDef(out ThingDef low)
                        || !TryAddBuildingToSketch(sketch, low, expandBy))
                    {
                        return false;
                    }
                }

                if (!hasHigh)
                {
                    if (!TryGetHighAngleShieldDef(out ThingDef high)
                        || !TryAddBuildingToSketch(sketch, high, expandBy))
                    {
                        return false;
                    }
                }

                if (!hasCauser)
                {
                    List<ThingDef> causers = GetConditionCausers(Mathf.RoundToInt(buildingPoints));
                    if (causers.Count == 0
                        || !TryAddBuildingToSketch(sketch, causers.RandomElement(), expandBy))
                    {
                        return false;
                    }
                }
            }

            return SketchHasRequiredRoyaltyBuildings(sketch, buildingPoints);
        }

        private static MechClusterSketch GenerateCombatThreatClusterSketch(float points, Map map)
        {
            int sizeValue = Mathf.Clamp(
                GenMath.RoundRandom(FallbackPointsToSizeCurve.Evaluate(points)
                    * FallbackSizeRandomFactorRange.RandomInRange),
                10,
                28);
            if (map != null)
            {
                CellRect largest = LargestAreaFinder.FindLargestRect(
                    map,
                    x => !x.Impassable(map)
                        && x.GetAffordances(map).Contains(TerrainAffordanceDefOf.Heavy),
                    sizeValue);
                sizeValue = Mathf.Min(sizeValue, Mathf.Min(largest.Width, largest.Height));
                sizeValue = Mathf.Max(sizeValue, 8);
            }

            IntVec2 size = new IntVec2(sizeValue, sizeValue);
            Sketch sketch = new Sketch();

            // 外围短墙，形成集群式结构骨架。
            if (Rand.Chance(0.65f))
            {
                AddSimpleWallFrame(sketch, size);
            }

            List<ThingDef> combatDefs = GetMechCombatThreatDefs(points);
            if (combatDefs.Count == 0)
            {
                combatDefs = GetMechTurretDefs();
            }

            if (combatDefs.Count > 0)
            {
                float pointsLeft = points;
                ThingDef cheapest = combatDefs.MinBy(d => Mathf.Max(1f, d.building.combatPower));
                pointsLeft = Mathf.Max(pointsLeft, cheapest.building.combatPower);
                int safety = 0;
                while (pointsLeft > 0f && safety++ < 80)
                {
                    List<ThingDef> affordable = combatDefs
                        .Where(d => d.building.combatPower <= pointsLeft)
                        .ToList();
                    if (affordable.Count == 0
                        || !affordable.TryRandomElement(out ThingDef pick)
                        || pick == null)
                    {
                        break;
                    }

                    if (TryPlaceInSketch(sketch, size, pick))
                    {
                        pointsLeft -= Mathf.Max(1f, pick.building.combatPower);
                    }
                    else
                    {
                        break;
                    }
                }
            }

            // 额外路障，增强集群防御感。
            int barricades = Rand.RangeInclusive(6, 14);
            for (int i = 0; i < barricades; i++)
            {
                TryPlaceInSketch(sketch, size, ThingDefOf.Barricade, ThingDefOf.Steel);
            }

            return new MechClusterSketch(
                sketch,
                new List<MechClusterSketch.Mech>(),
                startDormant: true);
        }

        private static void AddSimpleWallFrame(Sketch sketch, IntVec2 size)
        {
            ThingDef wall = ThingDefOf.Wall;
            ThingDef steel = ThingDefOf.Steel;
            for (int x = 0; x < size.x; x++)
            {
                if (x % 4 == 3)
                {
                    continue;
                }

                sketch.AddThing(wall, new IntVec3(x, 0, 0), Rot4.North, steel, 1, null, null, wipeIfCollides: false);
                sketch.AddThing(
                    wall,
                    new IntVec3(x, 0, size.z - 1),
                    Rot4.North,
                    steel,
                    1,
                    null,
                    null,
                    wipeIfCollides: false);
            }

            for (int z = 1; z < size.z - 1; z++)
            {
                if (z % 4 == 3)
                {
                    continue;
                }

                sketch.AddThing(wall, new IntVec3(0, 0, z), Rot4.North, steel, 1, null, null, wipeIfCollides: false);
                sketch.AddThing(
                    wall,
                    new IntVec3(size.x - 1, 0, z),
                    Rot4.North,
                    steel,
                    1,
                    null,
                    null,
                    wipeIfCollides: false);
            }
        }

        private static bool TryPlaceInSketch(
            Sketch sketch,
            IntVec2 size,
            ThingDef def,
            ThingDef? stuffOverride = null)
        {
            for (int attempt = 0; attempt < 80; attempt++)
            {
                IntVec3 cell = new IntVec3(Rand.Range(0, size.x), 0, Rand.Range(0, size.z));
                if (sketch.WouldCollide(def, cell, Rot4.North))
                {
                    continue;
                }

                ThingDef? stuff = stuffOverride;
                if (stuff == null)
                {
                    try
                    {
                        stuff = GenStuff.RandomStuffByCommonalityFor(def);
                    }
                    catch
                    {
                        if (def.MadeFromStuff)
                        {
                            continue;
                        }
                    }
                }

                if (sketch.AddThing(def, cell, Rot4.North, stuff, wipeIfCollides: false))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasAnySketchContent(Sketch sketch)
        {
            if (sketch == null)
            {
                return false;
            }

            return sketch.Things.Count > 0
                || sketch.Terrain.Count > 0
                || CountSketchEntities(sketch) > 0;
        }

        private static int CountSketchEntities(Sketch sketch)
        {
            try
            {
                return sketch.Entities?.Count ?? 0;
            }
            catch
            {
                return 0;
            }
        }

        private static bool OverlapsMechSpawn(
            MechClusterSketch cluster,
            ThingDef def,
            IntVec3 position)
        {
            if (cluster.pawns == null)
            {
                return false;
            }

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
