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
    /// 完整节点采用事务式初始化：失败时统一回滚本轮创建内容，严格要求全部守军落地后才 Succeeded。
    /// </summary>
    public static class MechHiveNodeMapGenerator
    {
        private const int MaxRequiredBuildingPlaceAttempts = 48;

        private const int MaxMapSupplementRounds = 4;

        private const int MaxPawnPlaceTriesPerRadius = 48;

        private const int PawnPlaceRadiusStep = 2;

        private const int MaxPawnPlaceExtraRadius = 48;

        /// <summary>完整节点本轮初始化事务上下文。</summary>
        private sealed class CompletedInitSession
        {
            public readonly MAPMechHiveNode Node;

            public readonly Map Map;

            public readonly Faction MechHive;

            public readonly MechHiveNodeInitAttemptRecord Record;

            public List<Thing> SpawnedThings => Record.ThingsListForRegistration;

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
                Log.Warning("[MAP] 机械巢派系不存在，机械巢节点地图不生成守军与建筑。");
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
                Log.Error("[MAP] 机械巢节点地图生成异常: " + ex);
                if (node.IsCompleted)
                {
                    node.NotifyMapContentInitFailed();
                    node.TryContinueFailedInitCleanup();
                }
                else if (node.MapInitState != MechHiveNodeMapInitState.Succeeded)
                {
                    node.NotifyMapContentInitSucceeded();
                }
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

            session.FailureStage = "草图生成";
            IntVec3 center = ResolveCenter(map);
            if (!MechClusterBuildingUtility.TryGenerateCompletedNodeBuildingSketch(
                    map,
                    MechClusterBuildingUtility.CompletedNodeBuildingPoints,
                    out MechClusterSketch sketch)
                || sketch?.buildingsSketch == null)
            {
                return FailCompletedInit(session, "草图生成", "未能生成合法机械集群草图");
            }

            MechClusterBuildingUtility.NormalizeSketchStuffToSteelForNode(sketch.buildingsSketch);
            if (MechClusterBuildingUtility.SketchHasNonSteelMadeFromStuff(sketch.buildingsSketch))
            {
                return FailCompletedInit(session, "草图材料检查", "草图含非钢铁 MadeFromStuff 建筑");
            }

            session.FailureStage = "草图落地";
            List<Thing> spawnedThings = session.SpawnedThings;
            sketch.buildingsSketch.Spawn(
                map,
                center,
                mechHive,
                Sketch.SpawnPosType.Unchanged,
                Sketch.SpawnMode.Normal,
                wipeIfCollides: true,
                forceTerrainAffordance: false,
                clearEdificeWhereFloor: false,
                spawnedThings,
                sketch.startDormant,
                buildRoofsInstantly: false,
                canSpawnThing: null,
                onFailedToSpawnThing: (IntVec3 spot, SketchEntity entity) =>
                {
                    if (entity is SketchThing sketchThing
                        && sketchThing.def != ThingDefOf.Wall
                        && sketchThing.def != ThingDefOf.Barricade)
                    {
                        entity.SpawnNear(
                            spot,
                            map,
                            12f,
                            mechHive,
                            Sketch.SpawnMode.Normal,
                            wipeIfCollides: true,
                            forceTerrainAffordance: false,
                            spawnedThings,
                            sketch.startDormant);
                    }
                });

            EnforceSteelStuffOnSpawnedThings(spawnedThings);
            record.RegisterThingsFromList(spawnedThings);

            float layoutRadius = Mathf.Max(
                12f,
                Mathf.Sqrt(
                    sketch.buildingsSketch.OccupiedSize.x * sketch.buildingsSketch.OccupiedSize.x
                    + sketch.buildingsSketch.OccupiedSize.z * sketch.buildingsSketch.OccupiedSize.z)
                    / 2f);

            session.FailureStage = "必需建筑补充";
            if (ModsConfig.RoyaltyActive
                && !EnsureRequiredRoyaltyBuildingsOnMap(
                    map,
                    center,
                    layoutRadius,
                    mechHive,
                    spawnedThings))
            {
                record.RegisterThingsFromList(spawnedThings);
                return FailCompletedInit(
                    session,
                    "必需建筑补充",
                    "落地后缺少低角护盾、高角护盾或地图状态建筑");
            }

            record.RegisterThingsFromList(spawnedThings);

            session.FailureStage = "钢铁材料复查";
            if (HasNonSteelMadeFromStuff(spawnedThings))
            {
                return FailCompletedInit(session, "钢铁材料复查", "落地后仍存在非钢铁 MadeFromStuff 建筑");
            }

            session.FailureStage = "Lord与建筑公共初始化";
            float defendRadius = layoutRadius + 6f;
            LordJob_SleepThenMechanoidsDefend lordJob = new LordJob_SleepThenMechanoidsDefend(
                spawnedThings,
                mechHive,
                defendRadius,
                center,
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
                spawnedThings,
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
                return FailCompletedInit(session, "守军生成", "10000威胁点未产生任何有效守军");
            }

            session.FailureStage = "守军放置";
            if (!TryPlaceAllGarrisonPawns(
                    map,
                    center,
                    layoutRadius + 4f,
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

            for (int i = 0; i < session.ExpectedPawns.Count; i++)
            {
                Pawn expected = session.ExpectedPawns[i];
                if (expected == null || expected.Destroyed || !expected.Spawned || expected.Map != map)
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

            for (int i = 0; i < session.PlacedPawns.Count; i++)
            {
                if (!session.ExpectedPawns.Contains(session.PlacedPawns[i]))
                {
                    return false;
                }
            }

            return true;
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
                    "[MAP] 完整机械巢节点初始化失败（阶段："
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
        /// 将预期列表中的每一名守军全部放入地图。任一失败则返回 false，failed 含未放置者。
        /// 已成功放置的留在 placed 中供外层回滚。
        /// </summary>
        private static bool TryPlaceAllGarrisonPawns(
            Map map,
            IntVec3 center,
            float baseRadius,
            List<Pawn> expected,
            List<Pawn> placed,
            out List<Pawn> failed)
        {
            placed.Clear();
            failed = new List<Pawn>();
            if (expected == null || expected.Count == 0)
            {
                return false;
            }

            int startRadius = Mathf.Max(1, Mathf.CeilToInt(baseRadius));
            int maxRadius = Mathf.Min(
                startRadius + MaxPawnPlaceExtraRadius,
                Mathf.Max(map.Size.x, map.Size.z) / 2);

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

                bool ok = false;
                for (int radius = startRadius; radius <= maxRadius; radius += PawnPlaceRadiusStep)
                {
                    if (!CellFinder.TryFindRandomCellNear(
                            center,
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
                            placed.Add(pawn);
                            ok = true;
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning("[MAP] 完整节点守军落地异常: " + ex);
                    }
                }

                if (!ok)
                {
                    failed.Add(pawn);
                }
            }

            return failed.Count == 0 && placed.Count == expected.Count;
        }

        private static bool EnsureRequiredRoyaltyBuildingsOnMap(
            Map map,
            IntVec3 center,
            float layoutRadius,
            Faction mechHive,
            List<Thing> spawnedThings)
        {
            if (!ModsConfig.RoyaltyActive)
            {
                return true;
            }

            int points = Mathf.RoundToInt(MechClusterBuildingUtility.CompletedNodeBuildingPoints);
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
                        map,
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
                        map,
                        center,
                        layoutRadius,
                        high,
                        mechHive,
                        spawnedThings);
                }

                if (!hasCauser)
                {
                    TryPlaceAnyConditionCauserOnMap(
                        map,
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
            Map map,
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
                        map,
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
            Map map,
            IntVec3 center,
            float layoutRadius,
            ThingDef def,
            Faction mechHive,
            List<Thing> spawnedThings)
        {
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
                        c => CanPlaceBuilding(map, def, c),
                        out IntVec3 cell))
                {
                    continue;
                }

                if (!TrySpawnAt(map, cell, def, stuff, mechHive, out Thing spawned))
                {
                    continue;
                }

                spawnedThings.Add(spawned);
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
                pawn.TryGetComp<CompCanBeDormant>()?.ToSleep();
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

        private static bool TrySpawnAt(
            Map map,
            IntVec3 cell,
            ThingDef def,
            ThingDef? stuff,
            Faction mechHive,
            out Thing spawned)
        {
            spawned = null!;
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

                Thing thing = ThingMaker.MakeThing(def, stuff);
                GenSpawn.Spawn(thing, cell, map, Rot4.North, WipeMode.Vanish);
                if (thing.def.CanHaveFaction)
                {
                    thing.SetFaction(mechHive);
                }

                spawned = thing;
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning("[MAP] 机械巢节点建筑生成失败（" + def.defName + "）: " + ex);
                return false;
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
