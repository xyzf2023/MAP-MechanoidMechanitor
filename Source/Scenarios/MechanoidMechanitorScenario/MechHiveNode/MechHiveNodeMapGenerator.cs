using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 机械巢节点地图内容生成。由 <see cref="SitePartWorker_MechHiveNode"/> 在地图生成后调用。
    /// 建设中节点使用简陋未完工布局；完整节点使用真正的机械集群式建筑草图。
    /// 完整节点建筑总预算 = 节点创建时保存的完成态守军点数快照（与 Pawn 守军预算独立，不互相扣减）；
    /// 超过单张草图上限时拆成多张蓝图，分别寻找独立合法落点后统一合并。
    /// 完整节点采用事务式初始化：失败时统一回滚本轮创建内容，严格要求全部守军落地后才 Succeeded。
    /// 事务记录（MechHiveNodeInitAttemptRecord）只用于失败回滚，建筑落地即登记；
    /// 成功后记录会被释放清空，因此交给共享 LordJob 的建筑列表必须是一个独立的 List 实例。
    /// </summary>
    public static class MechHiveNodeMapGenerator
    {
        private const int MaxRequiredBuildingPlaceAttempts = 48;

        private const int MaxMapSupplementRounds = 4;

        private const int MaxPawnPlaceTriesPerRadius = 48;

        private const int PawnPlaceRadiusStep = 2;

        private const int MaxPawnPlaceExtraRadius = 48;

        /// <summary>
        /// 多蓝图之间的最小保留间距（格）：保证相邻集群的建筑主体互不挤压、各自留有战斗空间，
        /// 同时也让补位生成不会跨集群互相覆盖。
        /// </summary>
        private const int ClusterSeparationPadding = 10;

        /// <summary>每张完整节点蓝图的落点搜索次数上限（有界搜索，不做全图穷举）。</summary>
        private const int MaxClusterPositionAttempts = 60;

        /// <summary>传入原版 MechClusterUtility.FindClusterPosition 的单次尝试上限。</summary>
        private const int VanillaClusterPositionTries = 15;

        /// <summary>共享防守半径的安全余量（格），与单蓝图时代保持一致的 6 格。</summary>
        private const float SharedDefendRadiusPadding = 6f;

        /// <summary>完整节点单张机械集群蓝图的运行期落地结果。仅用于地图初始化期间，不写入存档。</summary>
        private sealed class CompletedClusterInstance
        {
            public MechClusterSketch Sketch = null!;

            /// <summary>本蓝图分配到的建筑预算。</summary>
            public int BuildingPoints;

            public IntVec3 Center;

            public float LayoutRadius;

            /// <summary>
            /// 本蓝图建筑在地图上的实际占用范围。
            /// 初始按原始 Sketch 计算（仅用于落点验证），蓝图完整落地后由
            /// TryRecalculateClusterBounds 按实际建筑（含 SpawnNear 补位与补充建筑）重算。
            /// </summary>
            public CellRect OccupiedRect;

            /// <summary>
            /// 含 <see cref="ClusterSeparationPadding"/> 的保留范围，供后续蓝图避让。
            /// 始终等于最终 <see cref="OccupiedRect"/> 向外扩展后的结果。
            /// </summary>
            public CellRect ReservedRect;

            /// <summary>
            /// 本蓝图局部落地的全部有效建筑（含 SpawnNear 补位建筑与后续补充建筑）。
            /// 与事务记录互为补充：事务记录负责回滚，本列表负责本蓝图的必需建筑检查、
            /// 实际范围重算与守军分散。
            /// </summary>
            public readonly List<Thing> SpawnedThings = new List<Thing>();
        }

        /// <summary>完整节点本轮初始化事务上下文。</summary>
        private sealed class CompletedInitSession
        {
            public readonly MAPMechHiveNode Node;

            public readonly Map Map;

            public readonly Faction MechHive;

            public readonly MechHiveNodeInitAttemptRecord Record;

            /// <summary>
            /// 本轮事务追踪列表：只用于失败回滚，建筑落地即登记。
            /// 成功后会被 AbandonTrackingKeepContent() 清空，因此不得把它直接交给 LordJob。
            /// </summary>
            public List<Thing> SpawnedThings => Record.ThingsListForRegistration;

            /// <summary>本轮已成功落地的全部蓝图（按生成顺序，范围均为重算后的实际范围）。</summary>
            public readonly List<CompletedClusterInstance> Clusters = new List<CompletedClusterInstance>();

            /// <summary>全部蓝图中心，供共享防守范围计算与守军分散放置使用。</summary>
            public readonly List<IntVec3> ClusterCenters = new List<IntVec3>();

            public readonly List<Pawn> ExpectedPawns = new List<Pawn>();

            public readonly List<Pawn> PlacedPawns = new List<Pawn>();

            public Lord? Lord;

            public bool FailureHandled;

            public string? FailureStage;

            public CompletedInitSession(
                MAPMechHiveNode node,
                Map map,
                Faction mechHive,
                MechHiveNodeInitAttemptRecord record)
            {
                Node = node;
                Map = map;
                MechHive = mechHive;
                Record = record;
            }
        }

        public static void Generate(Map map, MAPMechHiveNode node)
        {
            if (map == null || node == null)
            {
                return;
            }

            node.NotifyMapContentInitStarted();

            Faction? mechHive = MechHiveNodeRelationUtility.GetMechHive();
            if (mechHive == null)
            {
                Log.Warning("[MAP-机械族机械师] 机械巢派系不存在，机械巢节点地图不生成守军与建筑。");
                if (node.IsCompleted)
                {
                    node.NotifyMapContentInitFailed();
                }
                else
                {
                    node.NotifyMapContentInitSucceeded();
                }

                return;
            }

            Rand.PushState(Gen.HashCombineInt(node.LayoutSeed, 0x1B3F7));
            try
            {
                if (node.IsCompleted)
                {
                    // Succeeded 仅在 GenerateCompleted 内部最终验证通过后写入。
                    GenerateCompleted(map, node, mechHive);
                }
                else
                {
                    GenerateBuilding(map, node, mechHive);
                    node.NotifyMapContentInitSucceeded();
                }
            }
            catch (Exception ex)
            {
                Log.Error("[MAP-机械族机械师] 机械巢节点地图生成异常: " + ex);
                // 两个阶段生成异常都保持 Failed，禁止进入或把缺失内容误结算为肃清。
                // 完成态继续精确回滚；建设中无事务记录，由现有安全空地图卸载流程恢复。
                node.NotifyMapContentInitFailed();
                node.TryContinueFailedInitCleanup();
            }
            finally
            {
                Rand.PopState();
            }
        }

        private static void GenerateCompleted(Map map, MAPMechHiveNode node, Faction mechHive)
        {
            MechHiveNodeInitAttemptRecord record = node.BeginCompletedInitAttempt();
            CompletedInitSession session = new CompletedInitSession(node, map, mechHive, record);
            try
            {
                TryGenerateCompletedCore(session);
            }
            catch (Exception ex)
            {
                FailCompletedInit(session, session.FailureStage ?? "未捕获异常", ex.ToString());
            }
        }

        private static bool TryGenerateCompletedCore(CompletedInitSession session)
        {
            MAPMechHiveNode node = session.Node;
            Map map = session.Map;
            Faction mechHive = session.MechHive;
            MechHiveNodeInitAttemptRecord record = session.Record;

            // 完整节点建筑总预算 = 节点创建时保存的完成态守军点数快照。
            // 建筑预算与机械族 Pawn 守军预算完全分离：二者都等于该快照，不互相扣减。
            int totalBuildingPoints = node.CompletedGarrisonThreatPointsSnapshot;
            List<int> budgets =
                MechClusterBuildingUtility.SplitBuildingPointsIntoSketchBudgets(totalBuildingPoints);

            for (int i = 0; i < budgets.Count; i++)
            {
                session.FailureStage = "蓝图生成与落地#" + (i + 1);
                if (!TryGenerateAndPlaceCluster(session, budgets[i], out CompletedClusterInstance? cluster)
                    || cluster == null)
                {
                    return FailCompletedInit(
                        session,
                        session.FailureStage,
                        "第 "
                            + (i + 1)
                            + "/"
                            + budgets.Count
                            + " 张蓝图（预算 "
                            + budgets[i]
                            + "，节点总预算 "
                            + totalBuildingPoints
                            + "，守军点数 "
                            + node.GarrisonThreatPoints
                            + "）未能生成或找不到独立合法落点");
                }

                session.Clusters.Add(cluster);
                session.ClusterCenters.Add(cluster.Center);
            }

            session.FailureStage = "钢铁材料复查";
            if (HasNonSteelMadeFromStuff(session.SpawnedThings))
            {
                return FailCompletedInit(session, "钢铁材料复查", "落地后仍存在非钢铁 MadeFromStuff 建筑");
            }

            session.FailureStage = "共享防守范围计算";
            if (!TryResolveSharedDefenseArea(session, out IntVec3 sharedDefSpot, out float sharedDefendRadius))
            {
                return FailCompletedInit(
                    session,
                    "共享防守范围计算",
                    "无法根据全部蓝图（共 " + session.Clusters.Count + " 张）计算共享防守中心与半径");
            }

            session.FailureStage = "Lord与建筑公共初始化";
            // 事务追踪列表只服务于失败回滚；LordJob 必须使用另一个独立的 List 实例，
            // 这样成功后释放事务追踪（AbandonTrackingKeepContent 清空自己的列表）
            // 也不会影响 LordJob 正在监听的建筑集合。
            List<Thing> lordJobThings = BuildLordJobThingList(session, map);
            if (!HasUsableNodeBuilding(lordJobThings, map))
            {
                return FailCompletedInit(
                    session,
                    "Lord与建筑公共初始化",
                    "全部蓝图合并后没有任何合法节点建筑，共享 LordJob 的 things 不允许为空");
            }

            // 全部蓝图的建筑一次性合并进唯一一个 LordJob：
            // 原版 LordJob_SleepThenMechanoidsDefend 的唤醒触发器监听的就是这一个 things 集合，
            // 并通过 TransitionAction_WakeAll 唤醒同一 Lord 的全部 Pawn。
            // 因此任意一个集群被攻击、或任意一名共享 Lord 守军受伤时，
            // 原版同一次状态转换会把全部集群的全部守军一起唤醒——无需任何额外广播或轮询。
            LordJob_SleepThenMechanoidsDefend lordJob = new LordJob_SleepThenMechanoidsDefend(
                lordJobThings,
                mechHive,
                sharedDefendRadius,
                sharedDefSpot,
                canAssaultColony: false,
                isMechCluster: false);
            Lord lord = LordMaker.MakeNewLord(mechHive, lordJob, map);
            if (lord == null)
            {
                return FailCompletedInit(session, "Lord创建", "MakeNewLord 返回空");
            }

            session.Lord = lord;
            record.RegisterLord(lord);

            MechClusterBuildingInitUtility.InitializeSpawnedBuildings(
                lordJobThings,
                mechHive,
                lordJob,
                lord,
                MechClusterBuildingInitUtility.InitOptions.ForCompletedNode());

            session.FailureStage = "守军生成";
            List<Pawn> generatedRaw =
                MechHiveCombatPawnUtility.GenerateCombatPawns(mechHive, map, node.GarrisonThreatPoints);
            List<Pawn> expected = session.ExpectedPawns;
            expected.Clear();
            if (generatedRaw != null)
            {
                for (int i = 0; i < generatedRaw.Count; i++)
                {
                    Pawn pawn = generatedRaw[i];
                    if (pawn != null && !pawn.Destroyed)
                    {
                        expected.Add(pawn);
                        record.RegisterPawn(pawn);
                    }
                }
            }

            int expectedCount = expected.Count;
            if (expectedCount == 0)
            {
                return FailCompletedInit(
                    session,
                    "守军生成",
                    node.GarrisonThreatPoints + " 威胁点未产生任何有效守军");
            }

            session.FailureStage = "守军放置";
            if (!TryPlaceAllGarrisonPawnsAcrossClusters(
                    map,
                    session.Clusters,
                    expected,
                    session.PlacedPawns,
                    out List<Pawn> failedToPlace))
            {
                return FailCompletedInit(
                    session,
                    "守军放置",
                    "未能将全部守军落地（预期 "
                        + expectedCount
                        + "，失败 "
                        + failedToPlace.Count
                        + "，蓝图数 "
                        + session.Clusters.Count
                        + "）");
            }

            session.FailureStage = "守军加入Lord";
            for (int i = 0; i < session.PlacedPawns.Count; i++)
            {
                Pawn pawn = session.PlacedPawns[i];
                try
                {
                    lord.AddPawn(pawn);
                }
                catch (Exception ex)
                {
                    return FailCompletedInit(session, "守军加入Lord", ex.ToString());
                }
            }

            // 全部守军加入正确 Lord 后，统一调用原版即时休眠（与 Sleeping Mechanoids 任务地图一致）。
            // 若此方法抛出异常，会被外层 GenerateCompleted 捕获并进入 FailCompletedInit 统一回滚。
            session.FailureStage = "守军初始休眠";
            GenStep_SleepingMechanoids.SendMechanoidsToSleepImmediately(session.PlacedPawns);

            session.FailureStage = "最终验证";
            if (!ValidateCompletedGarrison(session, expectedCount))
            {
                return FailCompletedInit(session, "最终验证", "守军/Lord/地图最终校验未通过");
            }

            // Succeeded 必须是成功流程的最后一步。
            node.NotifyMapContentInitSucceeded();
            session.FailureStage = null;
            return true;
        }

        private static bool ValidateCompletedGarrison(CompletedInitSession session, int expectedCount)
        {
            if (expectedCount <= 0
                || session.PlacedPawns.Count != expectedCount
                || session.ExpectedPawns.Count != expectedCount
                || session.Lord == null)
            {
                return false;
            }

            Lord lord = session.Lord;
            Map map = session.Map;
            Faction mechHive = session.MechHive;

            // 两个列表各自内部都不允许出现 null / Destroyed / 同一个 Pawn 重复出现。
            // 注意：ExpectedPawns 与 PlacedPawns 本来就应当包含同一批 Pawn，
            // 跨列表的“重复”是正确状态，绝不能据此判定失败，因此必须分别建两个集合，
            // 不能把两个列表的元素塞进同一个 HashSet 做去重计数。
            HashSet<Pawn> expectedSet = new HashSet<Pawn>();
            for (int i = 0; i < session.ExpectedPawns.Count; i++)
            {
                Pawn expected = session.ExpectedPawns[i];
                if (expected == null || expected.Destroyed || !expectedSet.Add(expected))
                {
                    return false;
                }
            }

            HashSet<Pawn> placedSet = new HashSet<Pawn>();
            for (int i = 0; i < session.PlacedPawns.Count; i++)
            {
                Pawn placed = session.PlacedPawns[i];
                if (placed == null || placed.Destroyed || !placedSet.Add(placed))
                {
                    return false;
                }
            }

            if (expectedSet.Count != expectedCount || placedSet.Count != expectedCount)
            {
                return false;
            }

            // 去重完成后，两个列表必须包含完全相同的一组守军。
            if (!expectedSet.SetEquals(placedSet))
            {
                return false;
            }

            for (int i = 0; i < session.ExpectedPawns.Count; i++)
            {
                Pawn expected = session.ExpectedPawns[i];
                if (!expected.Spawned || expected.Map != map)
                {
                    return false;
                }

                if (expected.Faction != mechHive)
                {
                    return false;
                }

                if (expected.GetLord() != lord)
                {
                    return false;
                }

                // 守军必须确实处于休眠状态（且具备休眠能力）。
                CompCanBeDormant dormantComp = expected.TryGetComp<CompCanBeDormant>();
                if (dormantComp == null || dormantComp.Awake)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>本次目标地图上仍然有效的节点建筑（非 null、未销毁、已生成、在当前地图）。</summary>
        private static bool IsValidNodeBuilding(Thing? thing, Map map)
        {
            return thing != null
                && !thing.Destroyed
                && thing.Spawned
                && thing.Map == map;
        }

        /// <summary>
        /// 构造交给唯一 LordJob_SleepThenMechanoidsDefend 的稳定建筑列表。
        /// 必须是一个新的 List 实例，绝不与 <see cref="MechHiveNodeInitAttemptRecord"/> 的
        /// 事务追踪列表共用实例：成功后 AbandonTrackingKeepContent() 会清空自己的追踪列表，
        /// LordJob 持有的建筑集合必须不受影响，否则共享状态机的唤醒监听集合会被清空。
        /// 结果只包含未销毁、已生成、位于本次地图的有效建筑，且不含重复项。
        /// </summary>
        private static List<Thing> BuildLordJobThingList(CompletedInitSession session, Map map)
        {
            List<Thing> result = new List<Thing>();
            HashSet<Thing> seen = new HashSet<Thing>();
            List<Thing> tracked = session.SpawnedThings;
            for (int i = 0; i < tracked.Count; i++)
            {
                Thing thing = tracked[i];
                if (!IsValidNodeBuilding(thing, map))
                {
                    continue;
                }

                if (!seen.Add(thing))
                {
                    continue;
                }

                result.Add(thing);
            }

            return result;
        }

        /// <summary>
        /// 为一份蓝图预算生成合法机械集群草图、寻找独立合法落点、落地并补齐本蓝图必需建筑。
        /// 落地后的全部建筑立即登记到同一个事务记录（不等全部蓝图完成），保证中途失败可精确回滚。
        /// </summary>
        private static bool TryGenerateAndPlaceCluster(
            CompletedInitSession session,
            int buildingPoints,
            out CompletedClusterInstance? cluster)
        {
            cluster = null;
            Map map = session.Map;
            Faction mechHive = session.MechHive;
            MechHiveNodeInitAttemptRecord record = session.Record;

            if (!MechClusterBuildingUtility.TryGenerateCompletedNodeBuildingSketch(
                    map,
                    buildingPoints,
                    out MechClusterSketch sketch)
                || sketch?.buildingsSketch == null)
            {
                return false;
            }

            MechClusterBuildingUtility.NormalizeSketchStuffToSteelForNode(sketch.buildingsSketch);
            if (MechClusterBuildingUtility.SketchHasNonSteelMadeFromStuff(sketch.buildingsSketch))
            {
                return false;
            }

            if (!TryFindClusterCenter(session, sketch, out IntVec3 center))
            {
                return false;
            }

            Sketch buildings = sketch.buildingsSketch;
            CompletedClusterInstance instance = new CompletedClusterInstance
            {
                Sketch = sketch,
                BuildingPoints = buildingPoints,
                Center = center,

                // 原始草图范围只是“初始落点验证依据”，不是本集群的最终范围：
                // SpawnNear 补位建筑、后续补充的低角护盾/高角护盾/状态建筑都可能落在它之外。
                // 最终 OccupiedRect / ReservedRect / LayoutRadius 由
                // TryRecalculateClusterBounds() 在全部建筑落地后按实际建筑重算。
                OccupiedRect = buildings.OccupiedRect.MovedBy(center),
                ReservedRect = buildings.OccupiedRect.MovedBy(center).ExpandedBy(ClusterSeparationPadding),
                LayoutRadius = Mathf.Max(
                    12f,
                    Mathf.Sqrt(
                        buildings.OccupiedSize.x * buildings.OccupiedSize.x
                        + buildings.OccupiedSize.z * buildings.OccupiedSize.z)
                        / 2f)
            };

            SpawnClusterSketch(session, instance);

            EnforceSteelStuffOnSpawnedThings(instance.SpawnedThings);
            record.RegisterThingsFromList(instance.SpawnedThings);

            // 每张蓝图各自补齐低角护盾、高角护盾与合法状态建筑：
            // 不把本蓝图缺少的建筑补到其它蓝图附近，任意一张仍缺则整个节点初始化失败。
            if (ModsConfig.RoyaltyActive
                && !EnsureRequiredRoyaltyBuildingsOnMap(
                    session,
                    instance.Center,
                    instance.LayoutRadius,
                    instance.BuildingPoints,
                    mechHive,
                    instance.SpawnedThings))
            {
                record.RegisterThingsFromList(instance.SpawnedThings);
                return false;
            }

            // 按本张蓝图实际落地的建筑重算范围；完全没有有效建筑则本张蓝图失败并统一回滚。
            if (!TryRecalculateClusterBounds(session, instance, out string? boundsFailure))
            {
                record.RegisterThingsFromList(instance.SpawnedThings);
                Log.Warning(
                    "[MAP-机械族机械师] 完整机械巢节点蓝图实际范围重算失败（预算 "
                        + buildingPoints
                        + "）："
                        + boundsFailure);
                return false;
            }

            record.RegisterThingsFromList(instance.SpawnedThings);
            cluster = instance;
            return true;
        }

        /// <summary>
        /// 把事务列表中本次新增的对象补入蓝图局部列表（去重，且忽略已销毁对象）。
        /// 即使 Sketch.Spawn 中途抛出异常也会执行，保证局部列表与事务记录一致。
        /// </summary>
        private static void SyncClusterLocalList(
            CompletedClusterInstance instance,
            List<Thing> registrationList,
            HashSet<Thing> preExisting)
        {
            List<Thing> clusterThings = instance.SpawnedThings;
            for (int i = 0; i < registrationList.Count; i++)
            {
                Thing thing = registrationList[i];
                if (thing == null
                    || thing.Destroyed
                    || preExisting.Contains(thing)
                    || clusterThings.Contains(thing))
                {
                    continue;
                }

                clusterThings.Add(thing);
            }
        }

        /// <summary>
        /// 按本张蓝图实际落地的建筑重算 OccupiedRect / ReservedRect / LayoutRadius。
        /// 必须覆盖原始蓝图建筑、SpawnNear 补位建筑、低角护盾、高角护盾与状态建筑，
        /// 否则后续集群间距会不足，共享防守范围也无法覆盖实际建筑。
        /// </summary>
        private static bool TryRecalculateClusterBounds(
            CompletedInitSession session,
            CompletedClusterInstance instance,
            out string? failureReason)
        {
            failureReason = null;
            Map map = session.Map;
            List<Thing> localThings = instance.SpawnedThings;

            // 本蓝图有效建筑最终确认：剔除 null / 已销毁 / 未生成 / 不在本次地图上的对象。
            for (int i = localThings.Count - 1; i >= 0; i--)
            {
                if (!IsValidNodeBuilding(localThings[i], map))
                {
                    localThings.RemoveAt(i);
                }
            }

            if (localThings.Count == 0)
            {
                failureReason = "本张蓝图最终没有任何有效建筑";
                return false;
            }

            // 每个建筑按其真实 Position、Rotation 与 def.Size 计算占用矩形，再合并成集群范围。
            CellRect occupied = GenAdj.OccupiedRect(
                localThings[0].Position,
                localThings[0].Rotation,
                localThings[0].def.Size);
            for (int i = 1; i < localThings.Count; i++)
            {
                occupied = occupied.Encapsulate(
                    GenAdj.OccupiedRect(
                        localThings[i].Position,
                        localThings[i].Rotation,
                        localThings[i].def.Size));
            }

            instance.OccupiedRect = occupied;
            instance.ReservedRect = occupied.ExpandedBy(ClusterSeparationPadding);

            // 布局半径保持原有最小值约束，且不得小于实际建筑范围：
            // 既保留原始半对角线口径，也保证覆盖相对蓝图中心最远的建筑角。
            float halfDiagonal =
                Mathf.Sqrt((float)occupied.Width * occupied.Width
                    + (float)occupied.Height * occupied.Height)
                / 2f;
            float maxCornerDistance = 0f;
            foreach (IntVec3 corner in occupied.Corners)
            {
                maxCornerDistance = Mathf.Max(maxCornerDistance, corner.DistanceTo(instance.Center));
            }

            instance.LayoutRadius = Mathf.Max(12f, halfDiagonal, maxCornerDistance);
            return true;
        }

        /// <summary>
        /// 落地单张蓝图。禁止擦除先前已登记的节点建筑：wipeIfCollides=false，
        /// 并通过 canSpawnThing / SpawnNear validator 再次拒绝会覆盖已登记建筑的格子。
        /// “落地即登记”：Sketch.Spawn 与 SpawnNear 直接把新对象写入本轮事务记录列表，
        /// 任何一个建筑生成后立刻可被精确回滚；蓝图局部列表在 finally 中同步，
        /// 即使 Sketch.Spawn 中途抛异常也不会漏掉已生成的对象（异常继续向外抛出统一回滚）。
        /// </summary>
        private static void SpawnClusterSketch(
            CompletedInitSession session,
            CompletedClusterInstance instance)
        {
            Map map = session.Map;
            Faction mechHive = session.MechHive;
            MechClusterSketch sketch = instance.Sketch;

            // 事务追踪列表直接作为 Sketch.Spawn 的 spawnedThings，
            // 保证每个建筑实例化后立刻进入事务记录（不等整张蓝图生成结束）。
            List<Thing> registrationList = session.Record.ThingsListForRegistration;
            HashSet<Thing> preExisting = new HashSet<Thing>(registrationList);

            try
            {
                sketch.buildingsSketch.Spawn(
                    map,
                    instance.Center,
                    mechHive,
                    Sketch.SpawnPosType.Unchanged,
                    Sketch.SpawnMode.Normal,
                    wipeIfCollides: false,
                    forceTerrainAffordance: false,
                    clearEdificeWhereFloor: false,
                    registrationList,
                    sketch.startDormant,
                    buildRoofsInstantly: false,
                    canSpawnThing: (SketchEntity entity, IntVec3 cell) =>
                        CanSpawnClusterEntity(session, entity, cell),
                    onFailedToSpawnThing: (IntVec3 spot, SketchEntity entity) =>
                    {
                        if (entity is SketchThing sketchThing
                            && sketchThing.def != ThingDefOf.Wall
                            && sketchThing.def != ThingDefOf.Barricade)
                        {
                            // 补位生成同样写入同一个事务记录目标，保证失败时可精确回滚。
                            entity.SpawnNear(
                                spot,
                                map,
                                12f,
                                mechHive,
                                Sketch.SpawnMode.Normal,
                                wipeIfCollides: false,
                                forceTerrainAffordance: false,
                                registrationList,
                                sketch.startDormant,
                                validator: (SketchEntity candidate, IntVec3 cell) =>
                                    CanSpawnClusterEntity(session, candidate, cell)
                                    && !OverlapsAcceptedClusterReservedRect(session, candidate, cell));
                        }
                    });
            }
            finally
            {
                // 无论成功还是中途异常，都要把本次新增的对象补入蓝图局部列表，
                // 供必需建筑补充、实际范围重算与失败信息使用。
                SyncClusterLocalList(instance, registrationList, preExisting);
            }
        }

        /// <summary>蓝图实体不得落在已登记的节点建筑上；任意占用格越出地图范围必须直接拒绝。</summary>
        private static bool CanSpawnClusterEntity(
            CompletedInitSession session,
            SketchEntity entity,
            IntVec3 cell)
        {
            Map map = session.Map;
            foreach (IntVec3 target in entity.OccupiedRect.MovedBy(cell))
            {
                // 越界格不能跳过：只要有一格越出地图，本实体就不得在此生成。
                if (!target.InBounds(map))
                {
                    return false;
                }

                List<Thing> things = target.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    if (session.SpawnedThings.Contains(things[i]))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>补位生成（SpawnNear）不得落入其它已接受蓝图的保留范围。</summary>
        private static bool OverlapsAcceptedClusterReservedRect(
            CompletedInitSession session,
            SketchEntity entity,
            IntVec3 cell)
        {
            return OverlapsAcceptedClusterReservedRect(session, entity.OccupiedRect.MovedBy(cell));
        }

        /// <summary>
        /// 本蓝图后续补充建筑（低角护盾、高角护盾、状态建筑）的完整占用矩形
        /// 不得落入任何已接受蓝图的保留范围。只检查目标格是否空闲是不够的。
        /// </summary>
        private static bool OverlapsAcceptedClusterReservedRect(
            CompletedInitSession session,
            ThingDef def,
            IntVec3 cell)
        {
            if (def == null)
            {
                return false;
            }

            return OverlapsAcceptedClusterReservedRect(
                session,
                GenAdj.OccupiedRect(cell, Rot4.North, def.Size));
        }

        private static bool OverlapsAcceptedClusterReservedRect(
            CompletedInitSession session,
            CellRect rect)
        {
            for (int i = 0; i < session.Clusters.Count; i++)
            {
                if (rect.Overlaps(session.Clusters[i].ReservedRect))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 为一张蓝图寻找独立合法落点：先复用原版机械集群落点评分，
        /// 再对候选做本 MOD 自己的严格验证（不无条件接受原版搜索失败时的随机回退格），
        /// 最后做有界随机搜索与地图中心兜底。
        /// </summary>
        private static bool TryFindClusterCenter(
            CompletedInitSession session,
            MechClusterSketch sketch,
            out IntVec3 center)
        {
            center = IntVec3.Invalid;
            if (sketch?.buildingsSketch == null)
            {
                return false;
            }

            Map map = session.Map;

            // 1) 原版评分候选：评分结果不代表满足本 MOD 的多蓝图间距与覆盖规则，必须再次验证。
            IntVec3 vanillaCandidate =
                MechClusterUtility.FindClusterPosition(map, sketch, VanillaClusterPositionTries);
            if (vanillaCandidate.IsValid && IsValidClusterCenter(session, sketch, vanillaCandidate))
            {
                center = vanillaCandidate;
                return true;
            }

            // 2) 有界随机搜索：严格限制尝试次数，不做全图穷举。
            for (int attempt = 0; attempt < MaxClusterPositionAttempts; attempt++)
            {
                IntVec3 candidate = CellFinderLoose.RandomCellWith(c => c.Standable(map), map, 30);
                if (!candidate.IsValid || !IsValidClusterCenter(session, sketch, candidate))
                {
                    continue;
                }

                center = candidate;
                return true;
            }

            // 3) 最后兜底：地图中心附近（首张蓝图常用）。
            IntVec3 anchor = ResolveCenter(map);
            if (anchor.IsValid && IsValidClusterCenter(session, sketch, anchor))
            {
                center = anchor;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 本 MOD 的严格落点验证：草图整体在地图范围内、不贴近导致越界的地图边缘、
        /// 不与之前已接受蓝图的保留范围重叠、关键建筑地形承载合法、
        /// 不覆盖之前已经生成并登记的节点建筑。
        /// </summary>
        private static bool IsValidClusterCenter(
            CompletedInitSession session,
            MechClusterSketch sketch,
            IntVec3 center)
        {
            Map map = session.Map;
            Sketch buildings = sketch.buildingsSketch;
            if (buildings == null || !center.InBounds(map))
            {
                return false;
            }

            if (buildings.AnyThingOutOfBounds(map, center))
            {
                return false;
            }

            CellRect occupied = buildings.OccupiedRect.MovedBy(center);
            if (!occupied.InBounds(map) || occupied.InNoBuildEdgeArea(map))
            {
                return false;
            }

            // 与前序已接受蓝图的保留范围保持固定间距，避免互相挤压或覆盖。
            CellRect reserved = occupied.ExpandedBy(ClusterSeparationPadding);
            for (int i = 0; i < session.Clusters.Count; i++)
            {
                if (reserved.Overlaps(session.Clusters[i].ReservedRect))
                {
                    return false;
                }
            }

            List<SketchEntity> entities = buildings.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                SketchEntity entity = entities[i];
                if (entity.IsSpawningBlocked(center, map) || !entity.CanBuildOnTerrain(center, map))
                {
                    return false;
                }

                // 不覆盖之前已经生成并登记的节点建筑（不依赖 wipeIfCollides 擦除前一个集群）。
                foreach (IntVec3 cell in entity.OccupiedRect.MovedBy(center))
                {
                    if (!cell.InBounds(map))
                    {
                        return false;
                    }

                    List<Thing> things = cell.GetThingList(map);
                    for (int t = 0; t < things.Count; t++)
                    {
                        if (session.SpawnedThings.Contains(things[t]))
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// 根据全部蓝图的实际占用范围计算共享防守中心与半径，保证所有集群都在同一防御区域内，
        /// 而不是只覆盖第一张蓝图。
        /// </summary>
        private static bool TryResolveSharedDefenseArea(
            CompletedInitSession session,
            out IntVec3 defSpot,
            out float defendRadius)
        {
            defSpot = IntVec3.Invalid;
            defendRadius = 0f;
            Map map = session.Map;
            if (session.Clusters.Count == 0)
            {
                return false;
            }

            CellRect total = session.Clusters[0].OccupiedRect;
            for (int i = 1; i < session.Clusters.Count; i++)
            {
                total = total.Encapsulate(session.Clusters[i].OccupiedRect);
            }

            defSpot = total.CenterCell;
            if (!defSpot.InBounds(map))
            {
                defSpot = defSpot.ClampInsideMap(map);
            }

            float maxDistance = 0f;
            for (int i = 0; i < session.Clusters.Count; i++)
            {
                CompletedClusterInstance cluster = session.Clusters[i];
                foreach (IntVec3 corner in cluster.OccupiedRect.Corners)
                {
                    maxDistance = Mathf.Max(maxDistance, corner.DistanceTo(defSpot));
                }

                maxDistance = Mathf.Max(maxDistance, session.ClusterCenters[i].DistanceTo(defSpot));
            }

            defendRadius = maxDistance + SharedDefendRadiusPadding;
            return defSpot.InBounds(map) && defendRadius > 0f;
        }

        /// <summary>共享 LordJob 的 things 列表不允许为空：至少存在一个合法、未销毁、已落地的节点建筑。</summary>
        private static bool HasUsableNodeBuilding(List<Thing> things, Map map)
        {
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (thing != null && !thing.Destroyed && thing.Spawned && thing.Map == map)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>统一失败出口：精确登记并清理本轮创建内容，标记 Failed，不 Cleaned、不发攻克信。</summary>
        private static bool FailCompletedInit(
            CompletedInitSession session,
            string stage,
            string reason)
        {
            if (session == null)
            {
                return false;
            }

            MechHiveNodeInitAttemptRecord record = session.Record;
            session.FailureStage = stage;
            if (!record.FailureLogged)
            {
                record.FailureLogged = true;
                Log.Error(
                    "[MAP-机械族机械师] 完整机械巢节点初始化失败（阶段："
                        + stage
                        + "）："
                        + reason
                        + "。已回滚本轮内容并拒绝进入。");
            }

            if (!session.FailureHandled)
            {
                session.FailureHandled = true;
                if (session.Lord != null)
                {
                    record.RegisterLord(session.Lord);
                }

                record.RegisterThingsFromList(session.SpawnedThings);
                for (int i = 0; i < session.ExpectedPawns.Count; i++)
                {
                    record.RegisterPawn(session.ExpectedPawns[i]);
                }

                for (int i = 0; i < session.PlacedPawns.Count; i++)
                {
                    record.RegisterPawn(session.PlacedPawns[i]);
                }

                record.TryCleanupRegisteredContent(session.Map);
            }

            if (session.Node != null && !session.Node.Cleaned)
            {
                session.Node.NotifyMapContentInitFailed();
            }

            return false;
        }

        /// <summary>
        /// 将预期列表中的每一名守军全部放入地图，并按稳定顺序尽量平均分散到全部蓝图附近。
        /// 任一失败则返回 false，failed 含未放置者；已成功放置的留在 placed 中供外层回滚。
        /// 守军总预算只在此处落地一次，蓝图数量增加不会重复生成守军。
        /// </summary>
        private static bool TryPlaceAllGarrisonPawnsAcrossClusters(
            Map map,
            List<CompletedClusterInstance> clusters,
            List<Pawn> expected,
            List<Pawn> placed,
            out List<Pawn> failed)
        {
            placed.Clear();
            failed = new List<Pawn>();
            if (expected == null || expected.Count == 0 || clusters == null || clusters.Count == 0)
            {
                return false;
            }

            int clusterCount = clusters.Count;
            for (int i = 0; i < expected.Count; i++)
            {
                Pawn? pawn = expected[i];
                if (pawn == null)
                {
                    // 空引用不入 failed 列表；placed 数量将与 expected 不一致，整体判定失败。
                    continue;
                }

                if (pawn.Destroyed)
                {
                    failed.Add(pawn);
                    continue;
                }

                if (pawn.Spawned)
                {
                    if (pawn.Map == map)
                    {
                        placed.Add(pawn);
                        continue;
                    }

                    failed.Add(pawn);
                    continue;
                }

                // 稳定轮转分配：当前处于 Rand.PushState(LayoutSeed) 作用域内，顺序确定可复现。
                int startCluster = i % clusterCount;
                bool ok = false;
                for (int c = 0; c < clusterCount && !ok; c++)
                {
                    CompletedClusterInstance cluster = clusters[(startCluster + c) % clusterCount];
                    ok = TryPlacePawnNearCluster(map, cluster, pawn);
                    if (ok)
                    {
                        placed.Add(pawn);
                    }
                }

                if (!ok)
                {
                    failed.Add(pawn);
                }
            }

            return failed.Count == 0 && placed.Count == expected.Count;
        }

        /// <summary>
        /// 尝试在指定蓝图的中心与布局半径附近落地一名守军；该集群失败时由调用方换下一个集群。
        /// </summary>
        private static bool TryPlacePawnNearCluster(
            Map map,
            CompletedClusterInstance cluster,
            Pawn pawn)
        {
            int startRadius = Mathf.Max(1, Mathf.CeilToInt(cluster.LayoutRadius + 4f));
            int limit = Mathf.Max(map.Size.x, map.Size.z) / 2;
            int maxRadius = Mathf.Max(
                startRadius,
                Mathf.Min(startRadius + MaxPawnPlaceExtraRadius, Mathf.Max(1, limit)));

            for (int radius = startRadius; radius <= maxRadius; radius += PawnPlaceRadiusStep)
            {
                if (!CellFinder.TryFindRandomCellNear(
                        cluster.Center,
                        map,
                        radius,
                        c => IsValidPawnCell(map, c),
                        out IntVec3 cell,
                        MaxPawnPlaceTriesPerRadius))
                {
                    continue;
                }

                try
                {
                    GenSpawn.Spawn(pawn, cell, map, Rot4.Random);
                    if (pawn.Spawned && pawn.Map == map)
                    {
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning("[MAP-机械族机械师] 完整节点守军落地异常: " + ex);
                }
            }

            return false;
        }

        /// <summary>
        /// 为单张蓝图补齐低角护盾、高角护盾与合法状态建筑。
        /// buildingPoints 为该蓝图自己的预算，候选状态建筑按该预算筛选，与其它蓝图互不借用。
        /// </summary>
        private static bool EnsureRequiredRoyaltyBuildingsOnMap(
            CompletedInitSession session,
            IntVec3 center,
            float layoutRadius,
            int buildingPoints,
            Faction mechHive,
            List<Thing> spawnedThings)
        {
            if (!ModsConfig.RoyaltyActive)
            {
                return true;
            }

            int points = Mathf.Max(1, buildingPoints);
            for (int round = 0; round < MaxMapSupplementRounds; round++)
            {
                EvaluateSpawnedRoyaltyRequirements(
                    spawnedThings,
                    points,
                    out bool hasLow,
                    out bool hasHigh,
                    out bool hasCauser);

                if (hasLow && hasHigh && hasCauser)
                {
                    return true;
                }

                if (!hasLow
                    && MechClusterBuildingUtility.TryGetLowAngleShieldDef(out ThingDef low)
                    && MechClusterBuildingUtility.TryResolveBuildingStuff(
                        low,
                        forceSteelStuff: true,
                        out _))
                {
                    TryPlaceRequiredBuilding(
                        session,
                        center,
                        layoutRadius,
                        low,
                        mechHive,
                        spawnedThings);
                }

                if (!hasHigh
                    && MechClusterBuildingUtility.TryGetHighAngleShieldDef(out ThingDef high)
                    && MechClusterBuildingUtility.TryResolveBuildingStuff(
                        high,
                        forceSteelStuff: true,
                        out _))
                {
                    TryPlaceRequiredBuilding(
                        session,
                        center,
                        layoutRadius,
                        high,
                        mechHive,
                        spawnedThings);
                }

                if (!hasCauser)
                {
                    TryPlaceAnyConditionCauserOnMap(
                        session,
                        center,
                        layoutRadius,
                        points,
                        mechHive,
                        spawnedThings);
                }
            }

            EvaluateSpawnedRoyaltyRequirements(
                spawnedThings,
                points,
                out bool finalLow,
                out bool finalHigh,
                out bool finalCauser);
            return finalLow && finalHigh && finalCauser;
        }

        private static bool TryPlaceAnyConditionCauserOnMap(
            CompletedInitSession session,
            IntVec3 center,
            float layoutRadius,
            int points,
            Faction mechHive,
            List<Thing> spawnedThings)
        {
            List<ThingDef> causers = MechClusterBuildingUtility.GetConditionCausersForNode(points);
            if (causers.Count == 0)
            {
                return false;
            }

            ShuffleInPlace(causers);
            for (int i = 0; i < causers.Count; i++)
            {
                ThingDef def = causers[i];
                if (TryPlaceRequiredBuilding(
                        session,
                        center,
                        layoutRadius,
                        def,
                        mechHive,
                        spawnedThings)
                    && HasSpawnedConditionCauser(spawnedThings, points))
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

        private static bool HasSpawnedConditionCauser(List<Thing> spawnedThings, int points)
        {
            for (int i = 0; i < spawnedThings.Count; i++)
            {
                Thing thing = spawnedThings[i];
                if (thing != null
                    && !thing.Destroyed
                    && MechClusterBuildingUtility.IsConditionCauser(thing.def, points))
                {
                    return true;
                }
            }

            return false;
        }

        private static void EvaluateSpawnedRoyaltyRequirements(
            List<Thing> spawnedThings,
            int points,
            out bool hasLow,
            out bool hasHigh,
            out bool hasCauser)
        {
            hasLow = false;
            hasHigh = false;
            hasCauser = false;
            for (int i = 0; i < spawnedThings.Count; i++)
            {
                Thing thing = spawnedThings[i];
                if (thing == null || thing.Destroyed)
                {
                    continue;
                }

                if (MechClusterBuildingUtility.IsLowAngleShieldDef(thing.def))
                {
                    hasLow = true;
                }

                if (MechClusterBuildingUtility.IsHighAngleShieldDef(thing.def))
                {
                    hasHigh = true;
                }

                if (MechClusterBuildingUtility.IsConditionCauser(thing.def, points))
                {
                    hasCauser = true;
                }
            }
        }

        private static bool TryPlaceRequiredBuilding(
            CompletedInitSession session,
            IntVec3 center,
            float layoutRadius,
            ThingDef def,
            Faction mechHive,
            List<Thing> spawnedThings)
        {
            Map map = session.Map;
            if (!MechClusterBuildingUtility.TryResolveBuildingStuff(
                    def,
                    forceSteelStuff: true,
                    out ThingDef? stuff))
            {
                return false;
            }

            int maxRadius = Mathf.CeilToInt(layoutRadius) + 12;
            for (int attempt = 0; attempt < MaxRequiredBuildingPlaceAttempts; attempt++)
            {
                int radius = Mathf.Min(maxRadius, Mathf.CeilToInt(layoutRadius) + attempt / 4);
                if (!CellFinder.TryFindRandomCellNear(
                        center,
                        map,
                        radius,
                        c => CanPlaceBuilding(map, def, c)
                            && !OverlapsAcceptedClusterReservedRect(session, def, c),
                        out IntVec3 cell))
                {
                    continue;
                }

                // 传入事务记录：补充建筑成功落地后立刻登记，不等整个补充流程结束再批量登记。
                if (!TrySpawnAt(map, cell, def, stuff, mechHive, out Thing spawned, session.Record))
                {
                    continue;
                }

                spawnedThings.Add(spawned);
                session.Record.RegisterThing(spawned);
                return true;
            }

            return false;
        }

        private static void EnforceSteelStuffOnSpawnedThings(List<Thing> spawnedThings)
        {
            for (int i = spawnedThings.Count - 1; i >= 0; i--)
            {
                Thing thing = spawnedThings[i];
                if (thing == null || thing.Destroyed || !thing.def.MadeFromStuff)
                {
                    continue;
                }

                if (thing.Stuff == ThingDefOf.Steel)
                {
                    continue;
                }

                // 不允许保留非钢铁 Stuff：销毁后由补充流程按需重建必需类别。
                thing.Destroy(DestroyMode.Vanish);
                spawnedThings.RemoveAt(i);
            }
        }

        private static bool HasNonSteelMadeFromStuff(List<Thing> spawnedThings)
        {
            for (int i = 0; i < spawnedThings.Count; i++)
            {
                Thing thing = spawnedThings[i];
                if (thing != null
                    && !thing.Destroyed
                    && thing.def.MadeFromStuff
                    && thing.Stuff != ThingDefOf.Steel)
                {
                    return true;
                }
            }

            return false;
        }

        private static void GenerateBuilding(Map map, MAPMechHiveNode node, Faction mechHive)
        {
            IntVec3 center = ResolveCenter(map);
            float layoutRadius = 9f;

            List<Thing> allBuildings = new List<Thing>();

            List<ThingDef> turretPalette = MechClusterBuildingUtility.GetMechTurretDefs();
            if (turretPalette.Count == 0)
            {
                if (MechHiveNodeDefOf.Turret_AutoInferno != null)
                {
                    turretPalette.Add(MechHiveNodeDefOf.Turret_AutoInferno);
                }

                if (MechHiveNodeDefOf.Turret_AutoChargeBlaster != null)
                {
                    turretPalette.Add(MechHiveNodeDefOf.Turret_AutoChargeBlaster);
                }
            }

            int turretCount = Rand.RangeInclusive(2, 4);
            for (int i = 0; i < turretCount && turretPalette.Count > 0; i++)
            {
                ThingDef turretDef = turretPalette.RandomElement();
                if (TrySpawnBuilding(map, center, layoutRadius, turretDef, mechHive, out Thing turret))
                {
                    allBuildings.Add(turret);
                }
            }

            SpawnStructures(map, center, layoutRadius, mechHive, allBuildings);
            EnforceSteelStuffOnSpawnedThings(allBuildings);

            float defendRadius = layoutRadius + 6f;
            LordJob_SleepThenMechanoidsDefend lordJob = new LordJob_SleepThenMechanoidsDefend(
                allBuildings,
                mechHive,
                defendRadius,
                center,
                canAssaultColony: false,
                isMechCluster: false);
            Lord lord = LordMaker.MakeNewLord(mechHive, lordJob, map);

            // 建设中节点：复用同一初始化链路（休眠 + CompSpawnerPawn 首次生成时间），无 Royalty 必需建筑。
            MechClusterBuildingInitUtility.InitializeSpawnedBuildings(
                allBuildings,
                mechHive,
                lordJob,
                lord,
                new MechClusterBuildingInitUtility.InitOptions(
                    startDormant: true,
                    immediateAllConditionCausers: false,
                    immediateConditionCauserDef: null,
                    applyRandomInitiation: false,
                    randomInitiationDays: 0f,
                    assemblerDelayTicks: (int)(
                        MechClusterBuildingInitUtility.MechAssemblerInitialDelayDays.RandomInRange
                        * 60000f)));

            List<Pawn> generated =
                MechHiveCombatPawnUtility.GenerateCombatPawns(mechHive, map, node.GarrisonThreatPoints);
            List<Pawn> spawnedPawns = PlacePawns(map, center, layoutRadius + 4f, generated);
            for (int i = 0; i < spawnedPawns.Count; i++)
            {
                lord.AddPawn(spawnedPawns[i]);
            }

            // 全部守军加入 Lord 后，统一调用原版即时休眠（与完整机械巢节点顺序一致）。
            if (spawnedPawns.Count > 0)
            {
                GenStep_SleepingMechanoids.SendMechanoidsToSleepImmediately(spawnedPawns);
                ValidateInProgressGarrisonDormant(map, lord, spawnedPawns);
            }
        }

        /// <summary>建设中节点守军初始休眠的轻量校验：仅记录明确错误，不引入事务回滚体系。</summary>
        private static void ValidateInProgressGarrisonDormant(
            Map map,
            Lord lord,
            List<Pawn> spawnedPawns)
        {
            if (spawnedPawns == null)
            {
                return;
            }

            for (int i = 0; i < spawnedPawns.Count; i++)
            {
                Pawn pawn = spawnedPawns[i];
                CompCanBeDormant dormantComp = pawn.TryGetComp<CompCanBeDormant>();
                if (pawn == null
                    || pawn.Destroyed
                    || !pawn.Spawned
                    || pawn.Map != map
                    || pawn.GetLord() != lord
                    || dormantComp == null
                    || dormantComp.Awake)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 建设中机械巢节点守军休眠校验失败："
                            + (pawn != null ? pawn.ToString() : "null"));
                }
            }
        }

        private static IntVec3 ResolveCenter(Map map)
        {
            IntVec3 center = map.Center;
            if (center.Standable(map) && !center.Fogged(map))
            {
                return center;
            }

            if (CellFinder.TryFindRandomCellNear(
                    map.Center,
                    map,
                    20,
                    c => c.Standable(map) && !c.Fogged(map),
                    out IntVec3 result))
            {
                return result;
            }

            return map.Center;
        }

        private static void SpawnStructures(
            Map map,
            IntVec3 center,
            float radius,
            Faction mechHive,
            List<Thing> allBuildings)
        {
            int wallSegments = Rand.RangeInclusive(1, 2);
            for (int i = 0; i < wallSegments; i++)
            {
                if (!CellFinder.TryFindRandomCellNear(
                        center,
                        map,
                        Mathf.CeilToInt(radius),
                        c => CanPlaceBuilding(map, ThingDefOf.Wall, c),
                        out IntVec3 start))
                {
                    continue;
                }

                Rot4 dir = Rand.Bool ? Rot4.East : Rot4.North;
                int segLen = Rand.RangeInclusive(2, 3);
                IntVec3 cursor = start;
                for (int j = 0; j < segLen; j++)
                {
                    if (CanPlaceBuilding(map, ThingDefOf.Wall, cursor)
                        && TrySpawnAt(
                            map,
                            cursor,
                            ThingDefOf.Wall,
                            ThingDefOf.Steel,
                            mechHive,
                            out Thing wall))
                    {
                        allBuildings.Add(wall);
                    }

                    cursor += dir.FacingCell;
                }
            }

            int barricades = Rand.RangeInclusive(3, 5);
            ThingDef barricadeDef = ThingDefOf.Barricade;
            for (int i = 0; i < barricades; i++)
            {
                if (CellFinder.TryFindRandomCellNear(
                        center,
                        map,
                        Mathf.CeilToInt(radius),
                        c => CanPlaceBuilding(map, barricadeDef, c),
                        out IntVec3 cell)
                    && TrySpawnAt(
                        map,
                        cell,
                        barricadeDef,
                        ThingDefOf.Steel,
                        mechHive,
                        out Thing barricade))
                {
                    allBuildings.Add(barricade);
                }
            }
        }

        private static List<Pawn> PlacePawns(
            Map map,
            IntVec3 center,
            float radius,
            List<Pawn> pawns)
        {
            List<Pawn> spawned = new List<Pawn>();
            if (pawns == null || pawns.Count == 0)
            {
                return spawned;
            }

            List<Pawn> unused = new List<Pawn>();
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn == null)
                {
                    continue;
                }

                if (!CellFinder.TryFindRandomCellNear(
                        center,
                        map,
                        Mathf.CeilToInt(radius),
                        c => IsValidPawnCell(map, c),
                        out IntVec3 cell))
                {
                    unused.Add(pawn);
                    continue;
                }

                GenSpawn.Spawn(pawn, cell, map, Rot4.Random);
                spawned.Add(pawn);
            }

            MechHiveCombatPawnUtility.DiscardPawns(unused);
            return spawned;
        }

        private static bool IsValidPawnCell(Map map, IntVec3 c)
        {
            if (!c.InBounds(map) || c.Fogged(map) || !c.Standable(map))
            {
                return false;
            }

            if (c.GetEdifice(map) != null)
            {
                return false;
            }

            Building_Door? door = c.GetDoor(map);
            if (door != null)
            {
                return false;
            }

            return !c.GetThingList(map).Exists(t => t is Pawn);
        }

        private static bool TrySpawnBuilding(
            Map map,
            IntVec3 center,
            float radius,
            ThingDef def,
            Faction mechHive,
            out Thing spawned)
        {
            spawned = null!;
            if (def == null || MechClusterBuildingUtility.IsExcludedFromNodeCluster(def))
            {
                return false;
            }

            if (!MechClusterBuildingUtility.TryResolveBuildingStuff(
                    def,
                    forceSteelStuff: true,
                    out ThingDef? stuff))
            {
                return false;
            }

            if (!CellFinder.TryFindRandomCellNear(
                    center,
                    map,
                    Mathf.CeilToInt(radius),
                    c => CanPlaceBuilding(map, def, c),
                    out IntVec3 cell))
            {
                return false;
            }

            return TrySpawnAt(map, cell, def, stuff, mechHive, out spawned);
        }

        /// <summary>
        /// 生成并落地一座建筑。
        /// record 非空表示“完整节点事务路径”：对象成功落地后立即登记进事务记录，
        /// 后续步骤（如设置派系）再抛异常也能被精确回滚。
        /// record 为空表示“建设中节点非事务路径”：保持原有规则，
        /// 已落地但未能返回的对象在 catch 中就地安全销毁，绝不留未登记的地图对象。
        /// </summary>
        private static bool TrySpawnAt(
            Map map,
            IntVec3 cell,
            ThingDef def,
            ThingDef? stuff,
            Faction mechHive,
            out Thing spawned,
            MechHiveNodeInitAttemptRecord? record = null)
        {
            spawned = null!;
            Thing? thing = null;
            try
            {
                if (def.MadeFromStuff)
                {
                    if (ThingDefOf.Steel?.stuffProps == null
                        || !ThingDefOf.Steel.stuffProps.CanMake(def))
                    {
                        return false;
                    }

                    stuff = ThingDefOf.Steel;
                }
                else
                {
                    stuff = null;
                }

                thing = ThingMaker.MakeThing(def, stuff);
                GenSpawn.Spawn(thing, cell, map, Rot4.North, WipeMode.Vanish);

                // 事务路径：落地即登记，之后任何异常都由 FailCompletedInit 精确清理。
                record?.RegisterThing(thing);

                if (thing.def.CanHaveFaction)
                {
                    thing.SetFaction(mechHive);
                }

                spawned = thing;
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning("[MAP-机械族机械师] 机械巢节点建筑生成失败（" + def.defName + "）: " + ex);

                // 已实例化但本次调用不会返回的对象：已登记的交给统一回滚，未登记的就地销毁，
                // 避免留下“既未返回、也未登记/清理”的地图对象。
                if (thing != null && !thing.Destroyed && !IsTrackedByRecord(record, thing))
                {
                    SafelyDestroyThing(thing, def);
                }

                return false;
            }
        }

        private static bool IsTrackedByRecord(MechHiveNodeInitAttemptRecord? record, Thing thing)
        {
            if (record == null)
            {
                return false;
            }

            IReadOnlyList<Thing> tracked = record.TrackedThings;
            for (int i = 0; i < tracked.Count; i++)
            {
                if (tracked[i] == thing)
                {
                    return true;
                }
            }

            return false;
        }

        private static void SafelyDestroyThing(Thing thing, ThingDef def)
        {
            try
            {
                // 原版 Thing.Destroy 内部会在 Spawned 时先 DeSpawn，无需手工 DeSpawn。
                if (!thing.Destroyed && thing.def.destroyable)
                {
                    thing.Destroy(DestroyMode.Vanish);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(
                    "[MAP-机械族机械师] 机械巢节点建筑异常残留清理失败（"
                        + (def?.defName ?? "?")
                        + "）: "
                        + ex);
            }
        }

        private static bool CanPlaceBuilding(Map map, ThingDef def, IntVec3 c)
        {
            CellRect rect = GenAdj.OccupiedRect(c, Rot4.North, def.Size);
            if (!rect.InBounds(map))
            {
                return false;
            }

            foreach (IntVec3 cell in rect)
            {
                if (cell.Fogged(map)
                    || !cell.Standable(map)
                    || cell.GetEdifice(map) != null
                    || cell.InNoBuildEdgeArea(map)
                    || !GenConstruct.CanBuildOnTerrain(def, cell, map, Rot4.North))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
