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

        /// <summary>
        /// 单张机械集群草图允许的最大建筑点数（等于原版 MechClusterGenerator.MaxPoints）。
        /// 只作为「多蓝图拆分阈值」与「旧存档回退值」使用；
        /// 新完整节点的建筑总预算来自节点创建时保存的完成态点数快照，不再固定为 10000。
        /// </summary>
        public const int MaxNodeClusterSketchPoints = 10000;

        /// <summary>
        /// 纯函数：把完整节点的总建筑预算平均拆分到多张机械集群蓝图。
        /// 规则：总点数至少按 1 处理；单张蓝图不超过 <see cref="MaxNodeClusterSketchPoints"/>；
        /// 余数逐个摊到前几张蓝图，保证结果之和严格等于 totalPoints，且每一项都大于 0。
        /// 例：6000→[6000]；10001→[5001,5000]；25000→[8334,8333,8333]。
        /// </summary>
        public static List<int> SplitBuildingPointsIntoSketchBudgets(int totalPoints)
        {
            List<int> budgets = new List<int>();
            int total = Mathf.Max(1, totalPoints);

            // 先减 1 再除，避免 totalPoints 接近 int.MaxValue 时溢出。
            int clusterCount = 1 + (total - 1) / MaxNodeClusterSketchPoints;
            int basePoints = total / clusterCount;
            int remainder = total % clusterCount;
            for (int i = 0; i < clusterCount; i++)
            {
                budgets.Add(basePoints + (i < remainder ? 1 : 0));
            }

            return budgets;
        }

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
        /// 集群部署路径：保持随机 Stuff。
        /// </summary>
        public static bool TryAddConditionCauser(
            MechClusterSketch cluster,
            ThingDef conditionCauser)
        {
            return TryAddBuildingToSketch(cluster, conditionCauser);
        }

        /// <summary>向草图安全追加一座建筑（集群部署等：随机 Stuff）。</summary>
        public static bool TryAddBuildingToSketch(MechClusterSketch? cluster, ThingDef? def)
        {
            return TryAddBuildingToSketch(cluster, def, expandBy: 6, forceSteelStuff: false);
        }

        /// <summary>向草图安全追加一座建筑；expandBy 控制搜索外扩格数。</summary>
        public static bool TryAddBuildingToSketch(
            MechClusterSketch? cluster,
            ThingDef? def,
            int expandBy)
        {
            return TryAddBuildingToSketch(cluster, def, expandBy, forceSteelStuff: false);
        }

        /// <summary>
        /// 向草图安全追加一座建筑。
        /// forceSteelStuff=true 时仅用于机械巢节点：需要 Stuff 的建筑必须使用钢铁。
        /// </summary>
        public static bool TryAddBuildingToSketch(
            MechClusterSketch? cluster,
            ThingDef? def,
            int expandBy,
            bool forceSteelStuff)
        {
            if (cluster?.buildingsSketch == null || def == null || IsExcludedFromNodeCluster(def))
            {
                return false;
            }

            if (!TryResolveBuildingStuff(def, forceSteelStuff, out ThingDef? stuff))
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

            int maxCells = Mathf.Min(cells.Count, MaxSketchCellSearchPerBuilding);
            for (int i = 0; i < maxCells; i++)
            {
                IntVec3 cell = cells[i];
                if (sketch.WouldCollide(def, cell, Rot4.North)
                    || OverlapsMechSpawn(cluster, def, cell))
                {
                    continue;
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

        /// <summary>
        /// 解析建筑 Stuff。节点强制钢铁；不允许钢铁的 MadeFromStuff 建筑返回 false。
        /// 非节点路径保持随机 Stuff（与集群部署一致）。
        /// </summary>
        public static bool TryResolveBuildingStuff(
            ThingDef def,
            bool forceSteelStuff,
            out ThingDef? stuff)
        {
            stuff = null;
            if (def == null)
            {
                return false;
            }

            if (!def.MadeFromStuff)
            {
                return true;
            }

            if (forceSteelStuff)
            {
                if (ThingDefOf.Steel?.stuffProps != null
                    && ThingDefOf.Steel.stuffProps.CanMake(def))
                {
                    stuff = ThingDefOf.Steel;
                    return true;
                }

                return false;
            }

            try
            {
                stuff = GenStuff.RandomStuffByCommonalityFor(def);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 节点草图落地前：统一 Stuff 为钢铁；不允许钢铁的可选建筑从草图移除。
        /// </summary>
        public static void NormalizeSketchStuffToSteelForNode(Sketch? sketch)
        {
            if (sketch == null)
            {
                return;
            }

            List<SketchThing> things = sketch.Things;
            for (int i = things.Count - 1; i >= 0; i--)
            {
                SketchThing thing = things[i];
                ThingDef? def = thing.def;
                if (def == null || !def.MadeFromStuff)
                {
                    continue;
                }

                if (ThingDefOf.Steel?.stuffProps != null
                    && ThingDefOf.Steel.stuffProps.CanMake(def))
                {
                    thing.stuff = ThingDefOf.Steel;
                }
                else
                {
                    sketch.Remove(thing);
                }
            }
        }

        /// <summary>草图中是否仍存在非钢铁 Stuff 的 MadeFromStuff 建筑。</summary>
        public static bool SketchHasNonSteelMadeFromStuff(Sketch? sketch)
        {
            if (sketch == null)
            {
                return false;
            }

            List<SketchThing> things = sketch.Things;
            for (int i = 0; i < things.Count; i++)
            {
                SketchThing thing = things[i];
                if (thing.def != null
                    && thing.def.MadeFromStuff
                    && thing.stuff != ThingDefOf.Steel)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 节点专用：打乱全部合法状态建筑候选后逐个尝试写入草图；全部失败才返回 false。
        /// </summary>
        public static bool TryAddAnyConditionCauserForNode(
            MechClusterSketch sketch,
            float buildingPoints,
            int expandBy)
        {
            if (sketch?.buildingsSketch == null || !ModsConfig.RoyaltyActive)
            {
                return false;
            }

            List<ThingDef> causers = GetConditionCausersForNode(Mathf.RoundToInt(buildingPoints));
            if (causers.Count == 0)
            {
                return false;
            }

            ShuffleInPlace(causers);
            for (int i = 0; i < causers.Count; i++)
            {
                if (TryAddBuildingToSketch(sketch, causers[i], expandBy, forceSteelStuff: true)
                    && SketchContainsConditionCauser(sketch, buildingPoints))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>节点可用的状态建筑：合法候选且允许钢铁（或不需要 Stuff）。</summary>
        public static List<ThingDef> GetConditionCausersForNode(int threatPoints)
        {
            List<ThingDef> raw = GetConditionCausers(threatPoints);
            List<ThingDef> result = new List<ThingDef>(raw.Count);
            for (int i = 0; i < raw.Count; i++)
            {
                ThingDef def = raw[i];
                if (TryResolveBuildingStuff(def, forceSteelStuff: true, out _))
                {
                    result.Add(def);
                }
            }

            return result;
        }

        public static bool SketchContainsConditionCauser(
            MechClusterSketch? sketch,
            float buildingPoints)
        {
            if (sketch?.buildingsSketch == null)
            {
                return false;
            }

            int points = Mathf.RoundToInt(buildingPoints);
            List<SketchThing> things = sketch.buildingsSketch.Things;
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i].def != null && IsConditionCauser(things[i].def, points))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ShuffleInPlace(List<ThingDef> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Rand.Range(0, i + 1);
                ThingDef tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
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
        /// 为完整机械巢节点生成单张机械集群式建筑草图（不含守军 Pawn）。
        /// buildingPoints 是该蓝图自己的预算：由完整节点总预算拆分而来，
        /// 内部仍按原版安全边界钳制到 [400, MechClusterGenerator.MaxPoints]。
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

            for (int attempt = 0; attempt < MaxSketchGenerationAttempts; attempt++)
            {
                try
                {
                    if (TryGenerateCompletedNodeBuildingSketchOnce(
                            map,
                            buildingPoints,
                            out sketch)
                        && sketch?.buildingsSketch != null
                        && HasAnySketchContent(sketch.buildingsSketch)
                        && !SketchHasNonSteelMadeFromStuff(sketch.buildingsSketch))
                    {
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(
                        "[MAP] 完整机械巢节点集群草图生成第 "
                            + (attempt + 1)
                            + " 次失败: "
                            + ex);
                }
            }

            sketch = null!;
            Log.Error("[MAP] 完整机械巢节点在有限次数内未能生成合法集群草图，终止该节点布局生成。");
            return false;
        }

        private static bool TryGenerateCompletedNodeBuildingSketchOnce(
            Map map,
            float buildingPoints,
            out MechClusterSketch sketch)
        {
            sketch = null!;
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
            NormalizeSketchStuffToSteelForNode(sketch.buildingsSketch);

            if (ModsConfig.RoyaltyActive
                && !EnsureRoyaltyRequiredBuildings(sketch, buildingPoints))
            {
                Log.Error(
                    "[MAP] 完整机械巢节点草图在补充后仍缺少必需的低角护盾、高角护盾或地图状态建筑，终止该次布局尝试。");
                return false;
            }

            NormalizeSketchStuffToSteelForNode(sketch.buildingsSketch);
            return HasAnySketchContent(sketch.buildingsSketch)
                && !SketchHasNonSteelMadeFromStuff(sketch.buildingsSketch);
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

        private const int MaxSketchCellSearchPerBuilding = 256;

        private const int MaxSketchGenerationAttempts = 3;

        /// <summary>
        /// 补充并复查草图必需建筑。任一类补充失败或复查未齐则返回 false。
        /// 状态建筑会打乱后逐个尝试全部合法候选。
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
                if (!hasLow
                    && TryGetLowAngleShieldDef(out ThingDef low)
                    && TryResolveBuildingStuff(low, forceSteelStuff: true, out _))
                {
                    TryAddBuildingToSketch(sketch, low, expandBy, forceSteelStuff: true);
                }

                if (!hasHigh
                    && TryGetHighAngleShieldDef(out ThingDef high)
                    && TryResolveBuildingStuff(high, forceSteelStuff: true, out _))
                {
                    TryAddBuildingToSketch(sketch, high, expandBy, forceSteelStuff: true);
                }

                if (!hasCauser)
                {
                    TryAddAnyConditionCauserForNode(sketch, buildingPoints, expandBy);
                }
            }

            NormalizeSketchStuffToSteelForNode(sketch.buildingsSketch);
            return SketchHasRequiredRoyaltyBuildings(sketch, buildingPoints)
                && !SketchHasNonSteelMadeFromStuff(sketch.buildingsSketch);
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
            // 战斗威胁草图仅用于节点回退：MadeFromStuff 统一钢铁。
            if (!TryResolveBuildingStuff(def, forceSteelStuff: true, out ThingDef? resolvedStuff))
            {
                return false;
            }

            ThingDef? stuff = stuffOverride ?? resolvedStuff;
            if (def.MadeFromStuff)
            {
                if (ThingDefOf.Steel?.stuffProps == null
                    || !ThingDefOf.Steel.stuffProps.CanMake(def))
                {
                    return false;
                }

                stuff = ThingDefOf.Steel;
            }

            for (int attempt = 0; attempt < 80; attempt++)
            {
                IntVec3 cell = new IntVec3(Rand.Range(0, size.x), 0, Rand.Range(0, size.z));
                if (sketch.WouldCollide(def, cell, Rot4.North))
                {
                    continue;
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
