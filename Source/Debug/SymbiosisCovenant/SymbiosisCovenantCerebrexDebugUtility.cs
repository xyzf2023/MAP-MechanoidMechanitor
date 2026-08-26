using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 共生盟约 × 奥德赛「机械主巢 / 主脑 BOSS」DEV 快速测试工具。
    ///
    /// 设计原则（严格遵循需求，不替正式功能完成发信、战斗或撤离）：
    /// 1. 仅在启用奥德赛 DLC 时由菜单入口调用（菜单层已做 OdysseyActive 判定）。
    /// 2. 尽量复用原版流程：
    ///    - 主巢任务严格通过 QuestUtility.GenerateQuestAndMakeAvailable 创建；
    ///    - 地图生成与进入通过原版 GetOrGenerateMapUtility + CaravanEnterMapUtility，
    ///      包裹在 LongEventHandler 中执行，避免同步生成 250×250 主巢地图卡住 UI；
    ///    - Pawn 通过原版 CaravanMaker.MakeCaravan 转为临时商队，由 CaravanEnterMapUtility 正常落位。
    /// 3. 共生盟约环境通过现有运行时配置工具开启，团结度通过 DevSetUnity 设置，
    ///    成员通过 DevForceJoinCovenant 设置，好感通过现有正式 Faction API 包装设置；
    ///    不反射修改任何私有字段。
    /// 4. 不生成 CerebrexCore / CerebrexStabilizer，不手工发送 MapGenerated 信号，
    ///    不手工调用支援 Part 的 Enable，不修改 offer/stage 字段来跳过流程，
    ///    不为“维持地图”新增任何 Harmony / MapComponent / GameComponent。
    /// 5. 不删除、清理或覆盖玩家已有的主巢任务、主巢地图、盟约记录、Pawn 或世界目标。
    /// 6. 不新增任何存档 GameComponent 或测试状态。
    /// </summary>
    public static class SymbiosisCovenantCerebrexDebugUtility
    {
        private const string DevTrustReason =
            "MAP_MechanoidMechanitor.Symbiosis.TrustReason.Dev";

        private const string MapGenKey = "GeneratingMapForNewEncounter";

        private const float TargetUnity = 700f;
        private const int TargetCovenantLevel = 5;
        private const int MinTrustForMember = 50;
        private const int MaxSupportMembers = 3;

        private const float ThreatLow = 3000f;
        private const float ThreatMid = 6000f;
        private const float ThreatHigh = 10000f;

        private const string NoSelectedPawnMessage =
            "请先在当前地图选中至少一个需要进入主巢测试的玩家 Pawn。";

        private const string ExistingTargetMessage =
            "当前已经存在机械主巢任务或世界目标，请使用“定位现有主脑测试目标”，"
            + "或读取测试前存档后重新生成。";

        // ===== 公开入口 =====

        /// <summary>
        /// 一键准备并进入：准备 L5 共生盟约环境、准备至多 3 个参战盟友、
        /// 生成原版 Gravcore_Mechhive 任务，并将玩家主动选中的 Pawn 安全送入主巢地图。
        /// 不替正式功能发信、生成援军或撤离。
        /// </summary>
        public static bool TryPrepareAndEnterTest(float points, out string message)
        {
            message = string.Empty;

            if (!TryRequirePreconditions(out message))
            {
                return false;
            }

            if (points <= 0f)
            {
                message = "威胁点数必须大于 0。";
                return false;
            }

            // “一键准备并进入”额外：不允许 LongEvent 进行中时再开新进入。
            if (LongEventHandler.AnyEventNowOrWaiting)
            {
                message = "当前有地图生成 / 长事件正在处理，请稍候再试。";
                return false;
            }

            Map sourceMap = Find.CurrentMap;
            if (sourceMap == null)
            {
                message = "当前没有可用地图。";
                return false;
            }

            if (sourceMap.generatorDef == MapGeneratorDefOf.Mechhive)
            {
                message = "当前已在机械主巢地图上，无法再次一键准备并进入。";
                return false;
            }

            // 先收集选中 Pawn（在任何状态修改之前失败，避免污染存档）。
            if (!TryCollectSelectedTransferPawns(out List<Pawn> pawns, out message))
            {
                return false;
            }

            // 重复主巢任务检查（在任何生成之前）。
            if (TryHasBlockingExistingCerebrexTarget(out _))
            {
                message = ExistingTargetMessage;
                return false;
            }

            // 准备共生盟约环境（L5 + 盟友）。
            if (!TryPrepareCovenantEnvironment(
                    out GameComponent_SymbiosisCovenantState? state,
                    out List<Faction> preparedMembers,
                    out message))
            {
                return false;
            }

            if (state == null)
            {
                message = "共生盟约组件为空。";
                return false;
            }

            // 生成合法原版 Gravcore_Mechhive 任务。
            if (!TryGenerateCerebrexQuest(
                    points,
                    out Quest? quest,
                    out Site? site,
                    out QuestPart_SymbiosisCovenantCerebrexSupport? supportPart,
                    out message))
            {
                return false;
            }

            if (quest == null || site == null || supportPart == null)
            {
                message = "主巢任务生成后内部引用不完整，已停止进入。";
                return false;
            }

            // 将选中 Pawn 安全转为临时玩家商队。
            if (!TryCreateTransferCaravan(sourceMap, pawns, out Caravan? caravan, out message))
            {
                return false;
            }

            // LongEvent：生成主巢地图并进入（避免同步生成大地图卡住 UI）。
            bool enterSucceeded = false;
            string enterMessage = string.Empty;
            bool wasGenerated = !site.HasMap;
            Map? targetMap = null;

            LongEventHandler.QueueLongEvent(
                () =>
                {
                    try
                    {
                        targetMap = GetOrGenerateMapUtility.GetOrGenerateMap(
                            site.Tile,
                            site.PreferredMapSize,
                            null);

                        if (targetMap == null)
                        {
                            enterMessage = "主巢地图生成失败（GetOrGenerateMap 返回空）。";
                            return;
                        }

                        if (caravan == null)
                        {
                            enterMessage = "临时商队为空，无法进入主巢地图。";
                            return;
                        }

                        if (wasGenerated)
                        {
                            Find.TickManager.Notify_GeneratedPotentiallyHostileMap();
                        }

                        CaravanEnterMapUtility.Enter(
                            caravan,
                            targetMap,
                            CaravanEnterMode.Edge,
                            CaravanDropInventoryMode.DoNotDrop,
                            draftColonists: true);

                        enterSucceeded = true;
                    }
                    catch (Exception ex)
                    {
                        Log.Error(
                            "[MAP-CerebrexDebug] 进入主巢地图时发生异常：\n" + ex);
                        enterMessage = "进入主巢地图时发生异常，详见日志"
                            + "（未删除任何 Pawn，也未销毁仍持有 Pawn 的商队）。";
                    }
                },
                MapGenKey,
                doAsynchronously: false,
                ex => Log.Error(
                    "[MAP-CerebrexDebug] 进入主巢地图长事件未捕获异常：\n" + ex));

            if (!enterSucceeded)
            {
                message = enterMessage.NullOrEmpty()
                    ? "进入主巢地图失败。"
                    : enterMessage;
                return false;
            }

            // 进入后验证（只读断言，不修改支援 Part 字段）。
            if (!TryVerifyEnteredCerebrex(
                    state,
                    site,
                    supportPart,
                    pawns,
                    preparedMembers,
                    out string verifyMessage))
            {
                message = verifyMessage;
                return false;
            }

            // 切换镜头到主巢地图，并定位第一个成功进入的玩家 Pawn。
            Pawn firstEntered = pawns.FirstOrDefault(
                p => p != null && p.Spawned && p.Map == site.Map
                    && p.Faction == Faction.OfPlayer);

            Map? enteredMap = site.Map;
            Current.Game.CurrentMap = enteredMap!;
            if (firstEntered != null)
            {
                CameraJumper.TryJumpAndSelect(firstEntered);
            }
            else if (enteredMap != null)
            {
                CameraJumper.TryJumpAndSelect(new GlobalTargetInfo(enteredMap.Center, enteredMap));
            }

            message =
                "已进入机械主巢地图（威胁点数 "
                + points.ToString("F0")
                + "，团结度 "
                + state.Unity.ToString("F0")
                + "，L"
                + state.CovenantLevel
                + "）。\n"
                + "已准备参战盟约成员："
                + string.Join("、", preparedMembers.Select(f => f.Name))
                + "\n"
                + "已进入玩家 Pawn："
                + pawns.Count(p => p != null && p.Spawned && p.Map == site.Map)
                + " 名。\n"
                + "盟约来信预计将在 offerDelayTicks 后按正式逻辑出现，"
                + "请继续人工测试（接受 / 拒绝 / 超时 / 分波支援 / 主脑技能 / "
                + "稳定器 / 主脑防御解除 / 清空威胁 / 撤离）。";

            return true;
        }

        /// <summary>
        /// 仅生成世界目标：准备 L5 共生盟约环境并生成合法的原版 Gravcore_Mechhive 任务，
        /// 但不转移 Pawn、不立即生成地图、不切换当前地图。
        /// 返回信息包含任务名 / Site 名 / 威胁点数 / 团结度 / 盟约等级 / 成功成员。
        /// </summary>
        public static bool TryPrepareAndGenerateTarget(float points, out string message)
        {
            message = string.Empty;

            if (!TryRequirePreconditions(out message))
            {
                return false;
            }

            if (points <= 0f)
            {
                message = "威胁点数必须大于 0。";
                return false;
            }

            if (TryHasBlockingExistingCerebrexTarget(out _))
            {
                message = ExistingTargetMessage;
                return false;
            }

            if (!TryPrepareCovenantEnvironment(
                    out GameComponent_SymbiosisCovenantState? state,
                    out List<Faction> preparedMembers,
                    out message))
            {
                return false;
            }

            if (state == null)
            {
                message = "共生盟约组件为空。";
                return false;
            }

            if (!TryGenerateCerebrexQuest(
                    points,
                    out Quest? quest,
                    out Site? site,
                    out QuestPart_SymbiosisCovenantCerebrexSupport? supportPart,
                    out message))
            {
                return false;
            }

            if (quest == null || site == null || supportPart == null)
            {
                message = "主巢任务生成后内部引用不完整。";
                return false;
            }

            message =
                "已生成机械主巢任务："
                + quest.name
                + "（QuestID="
                + quest.id
                + "）\n"
                + "世界目标："
                + site.Label
                + "\n"
                + "威胁点数："
                + points.ToString("F0")
                + "\n"
                + "团结度："
                + state.Unity.ToString("F0")
                + "，盟约等级：L"
                + state.CovenantLevel
                + "\n"
                + "已准备盟约成员："
                + string.Join("、", preparedMembers.Select(f => f.Name))
                + "\n"
                + "地图尚未生成，可通过世界地图正常进入。";

            return true;
        }

        /// <summary>
        /// 定位现有主脑测试目标：
        /// 有地图则优先定位玩家 Pawn → CerebrexCore → 主巢地图；
        /// 无地图则定位世界地图上的 Site。不改变任务 / 盟约 / 派系关系 / 支援状态。
        /// </summary>
        public static bool TryFocusExistingTestTarget(out string message)
        {
            message = string.Empty;

            if (!TryRequirePreconditions(out message))
            {
                return false;
            }

            if (!TryFindExistingCerebrexQuest(
                    out Quest? quest,
                    out Site? site,
                    out QuestPart_SymbiosisCovenantCerebrexSupport? supportPart))
            {
                message = "当前没有机械主巢任务或世界目标。";
                return false;
            }

            if (site == null || !site.Spawned)
            {
                message = "定位失败：主巢世界目标未生成或已失效。";
                return false;
            }

            if (site.HasMap && site.Map != null)
            {
                Map map = site.Map!;

                Pawn? playerPawn = null;
                foreach (Pawn p in map.mapPawns.AllPawnsSpawned)
                {
                    if (p != null && p.Faction == Faction.OfPlayer
                        && !p.Dead && !p.Destroyed)
                    {
                        playerPawn = p;
                        break;
                    }
                }

                if (playerPawn != null)
                {
                    Current.Game.CurrentMap = map;
                    CameraJumper.TryJumpAndSelect(playerPawn);
                    message = "已定位到主巢地图的玩家 Pawn：" + playerPawn.Label;
                    return true;
                }

                CompCerebrexCore? core =
                    SymbiosisCovenantCerebrexSupportUtility.FindCerebrexCore(map);
                if (core != null && core.parent != null)
                {
                    Current.Game.CurrentMap = map;
                    CameraJumper.TryJumpAndSelect(core.parent);
                    message = "已定位到主巢地图的 CerebrexCore。";
                    return true;
                }

                Current.Game.CurrentMap = map;
                CameraJumper.TryJumpAndSelect(new GlobalTargetInfo(map.Center, map));
                message = "已定位到主巢地图。";
                return true;
            }

            // 尚未生成地图：仅在世界上定位 Site，不为定位而生成地图。
            CameraJumper.TryJumpAndSelect(site);
            message = "已定位到世界地图上的主巢世界目标：" + site.Label;
            return true;
        }

        /// <summary>
        /// 输出结构化主脑测试状态到 RimWorld 日志（只读，不推进任何业务）。
        /// </summary>
        public static bool TryLogTestStatus(out string message)
        {
            message = string.Empty;

            if (!TryRequirePreconditions(out message))
            {
                return false;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[MAP-CerebrexDebug] ===== 主脑 BOSS 测试状态 =====");
            sb.AppendLine("OdysseyActive = " + ModsConfig.OdysseyActive);
            sb.AppendLine(
                "MechanitorScenarioEnabled = "
                + GameComponent_MechanoidMechanitorScenarioState.IsEnabled);

            GameComponent_SymbiosisCovenantState? state =
                GameComponent_SymbiosisCovenantState.CurrentComponent;
            bool active = GameComponent_SymbiosisCovenantState.IsActive;
            sb.AppendLine("CovenantActive = " + active);

            if (state != null)
            {
                sb.AppendLine("Initialized = " + state.Initialized);
                sb.AppendLine(
                    "PublicDeclarationBroadcast = " + state.PublicDeclarationBroadcast);
                sb.AppendLine("Unity = " + state.Unity.ToString("F0"));
                sb.AppendLine("CovenantLevel = " + state.CovenantLevel);
                sb.AppendLine("CovenantMemberCount = " + state.CovenantMemberCount);

                sb.AppendLine("---- 成员 ----");
                foreach (SymbiosisCovenantFactionRecord record in state.GetRecordsSorted())
                {
                    Faction? f = record.Faction;
                    if (f == null)
                    {
                        continue;
                    }

                    sb.AppendLine("Faction = " + f.Name + " [" + f.def.defName + "]");
                    sb.AppendLine("  Trust = " + record.Trust);
                    sb.AppendLine("  CovenantMember = " + record.CovenantMember);
                    sb.AppendLine(
                        "  WithPlayer = " + f.RelationKindWith(Faction.OfPlayer));
                    sb.AppendLine(
                        "  WithMechanoids = " + f.RelationKindWith(Faction.OfMechanoids));
                    sb.AppendLine(
                        "  HasCombat = "
                        + f.def.pawnGroupMakers.Any(
                            m => m.kindDef == PawnGroupKindDefOf.Combat));
                }
            }

            sb.AppendLine("---- 任务 / 世界目标 ----");
            if (TryFindExistingCerebrexQuest(
                    out Quest? quest,
                    out Site? site,
                    out QuestPart_SymbiosisCovenantCerebrexSupport? supportPart))
            {
                sb.AppendLine("QuestId = " + quest?.id);
                sb.AppendLine("QuestState = " + (quest?.State.ToString() ?? "-"));
                sb.AppendLine("SiteExists = " + (site != null));
                if (site != null)
                {
                    sb.AppendLine("SiteSpawned = " + site.Spawned);
                    sb.AppendLine("SiteHasMap = " + site.HasMap);
                    sb.AppendLine(
                        "SiteActualThreatPoints = " + site.ActualThreatPoints.ToString("F0"));
                }

                bool hasCore = quest != null
                    && quest.PartsListForReading.Any(p => p is QuestPart_CerebrexCore);
                sb.AppendLine("HasQuestPart_CerebrexCore = " + hasCore);
                sb.AppendLine("HasSupportPart = " + (supportPart != null));

                if (supportPart != null)
                {
                    sb.AppendLine("  stage = " + supportPart.stage);
                    sb.AppendLine("  offerSent = " + supportPart.offerSent);
                    sb.AppendLine("  offerResolved = " + supportPart.offerResolved);
                    sb.AppendLine("  mapEnteredTick = " + supportPart.mapEnteredTick);
                    sb.AppendLine(
                        "  covenantLevelSnapshot = " + supportPart.covenantLevelSnapshot);
                    sb.AppendLine(
                        "  threatPointsSnapshot = "
                        + supportPart.threatPointsSnapshot.ToString("F0"));
                    sb.AppendLine(
                        "  perWaveSupportPoints = "
                        + supportPart.perWaveSupportPoints.ToString("F0"));
                    sb.AppendLine("  waveCount = " + supportPart.waveCount);
                    sb.AppendLine(
                        "  coreDefencesLowered = " + supportPart.coreDefencesLowered);
                    sb.AppendLine("  evacMode = " + supportPart.evacMode);
                    sb.AppendLine(
                        "  evacVehicles.Count = " + supportPart.evacVehicles.Count);
                    sb.AppendLine(
                        "  invalidReason = " + (supportPart.invalidReason ?? "-"));
                }

                if (site != null && site.HasMap && site.Map != null)
                {
                    Map map = site.Map!;
                    sb.AppendLine("---- 地图 ----");
                    sb.AppendLine(
                        "generatorDef = " + (map.generatorDef?.defName ?? "-"));
                    sb.AppendLine(
                        "PlayerPawns = "
                        + map.mapPawns.AllPawnsSpawned.Count(
                            p => p.Faction == Faction.OfPlayer));
                    sb.AppendLine(
                        "CerebrexCore = "
                        + map.listerThings.ThingsOfDef(ThingDefOf.CerebrexCore).Count);
                    sb.AppendLine(
                        "CerebrexStabilizer = "
                        + map.listerThings.ThingsOfDef(ThingDefOf.CerebrexStabilizer).Count);
                    sb.AppendLine(
                        "HostileActiveThreat = "
                        + GenHostility.AnyHostileActiveThreatToPlayer(
                            map,
                            countDormantPawnsAsHostile: true,
                            canBeFogged: true));
                }
            }
            else
            {
                sb.AppendLine("无 Gravcore_Mechhive 任务或主巢世界目标。");
            }

            Log.Message(sb.ToString());
            message = "主脑测试状态已写入 RimWorld 日志。";
            return true;
        }

        // ===== 统一前置检查 =====

        private static bool TryRequirePreconditions(out string message)
        {
            message = string.Empty;

            if (!Prefs.DevMode)
            {
                message = "当前未启用开发者模式。";
                return false;
            }

            if (Current.Game == null)
            {
                message = "没有活动存档。";
                return false;
            }

            if (Current.ProgramState != ProgramState.Playing)
            {
                message = "当前不在游戏进行状态。";
                return false;
            }

            if (Find.World == null
                || Find.TickManager == null
                || Find.FactionManager == null)
            {
                message = "必要的游戏系统未就绪（世界 / 时钟 / 派系管理）。";
                return false;
            }

            if (!ModsConfig.OdysseyActive)
            {
                message = "未启用奥德赛 DLC，无法生成机械主巢任务。";
                return false;
            }

            if (QuestScriptDefOf.Gravcore_Mechhive == null)
            {
                message = "找不到 Gravcore_Mechhive 任务定义。";
                return false;
            }

            if (SitePartDefOf.OrbitalMechhive == null)
            {
                message = "找不到 OrbitalMechhive 主巢站点定义。";
                return false;
            }

            if (Faction.OfPlayerSilentFail == null)
            {
                message = "找不到玩家派系。";
                return false;
            }

            if (Faction.OfMechanoids == null)
            {
                message = "找不到机械族派系。";
                return false;
            }

            return true;
        }

        // ===== 选中 Pawn 收集 =====

        private static bool TryCollectSelectedTransferPawns(
            out List<Pawn> pawns,
            out string message)
        {
            pawns = new List<Pawn>();
            message = string.Empty;

            Map currentMap = Find.CurrentMap;
            if (currentMap == null)
            {
                message = "当前没有地图，无法读取选中的 Pawn。";
                return false;
            }

            if (Find.Selector == null || Find.Selector.SelectedPawns == null)
            {
                message = "无法读取选中 Pawn。";
                return false;
            }

            Faction player = Faction.OfPlayer;
            if (player == null)
            {
                message = "找不到玩家派系。";
                return false;
            }

            HashSet<Pawn> seen = new HashSet<Pawn>();
            foreach (Pawn pawn in Find.Selector.SelectedPawns)
            {
                if (pawn == null)
                {
                    continue;
                }

                // 只接受玩家派系、未死亡/销毁/丢弃、当前地图 Spawn 的 Pawn。
                if (pawn.Faction != player)
                {
                    continue;
                }

                if (pawn.Dead || pawn.Destroyed || pawn.Discarded)
                {
                    continue;
                }

                if (!pawn.Spawned)
                {
                    continue;
                }

                if (pawn.Map != currentMap)
                {
                    continue;
                }

                // 排除运输容器 / 休眠舱 / 其他 ThingOwner 中的 Pawn，
                // 以及被另一个 Pawn 搬运的对象。
                if (pawn.ParentHolder != null)
                {
                    continue;
                }

                if (seen.Contains(pawn))
                {
                    continue;
                }

                seen.Add(pawn);
                pawns.Add(pawn);
            }

            if (pawns.Count == 0)
            {
                message = NoSelectedPawnMessage;
                return false;
            }

            return true;
        }

        // ===== 现有主巢任务 / 世界目标检查 =====

        /// <summary>
        /// 查找进行中 / 历史的 Gravcore_Mechhive 任务，并优先从支援 Part 的公开
        /// site 引用获取精确 Site；若任务引用异常，仍会扫描世界对象中已有的
        /// OrbitalMechhive Site。用于“定位现有目标”。
        /// </summary>
        private static bool TryFindExistingCerebrexQuest(
            out Quest? quest,
            out Site? site,
            out QuestPart_SymbiosisCovenantCerebrexSupport? supportPart)
        {
            quest = null;
            site = null;
            supportPart = null;

            if (Find.QuestManager != null)
            {
                // 优先进行中，其次历史（已结束）任务。
                Quest? bestOngoing = null;
                QuestPart_SymbiosisCovenantCerebrexSupport? bestOngoingSupport = null;
                Site? bestOngoingSite = null;

                Quest? bestHistorical = null;
                QuestPart_SymbiosisCovenantCerebrexSupport? bestHistoricalSupport = null;
                Site? bestHistoricalSite = null;

                foreach (Quest q in Find.QuestManager.QuestsListForReading)
                {
                    if (q == null || q.root == null)
                    {
                        continue;
                    }

                    bool isGravcore = q.root == QuestScriptDefOf.Gravcore_Mechhive
                        || q.root.defName == "Gravcore_Mechhive";
                    if (!isGravcore)
                    {
                        continue;
                    }

                    QuestPart_SymbiosisCovenantCerebrexSupport? support = null;
                    foreach (QuestPart p in q.PartsListForReading)
                    {
                        if (p is QuestPart_SymbiosisCovenantCerebrexSupport sp)
                        {
                            support = sp;
                            break;
                        }
                    }

                    Site? candidate = support?.site;
                    if (candidate == null
                        || candidate.MainSitePartDef != SitePartDefOf.OrbitalMechhive)
                    {
                        continue;
                    }

                    if (q.State == QuestState.Ongoing)
                    {
                        bestOngoing = q;
                        bestOngoingSupport = support;
                        bestOngoingSite = candidate;
                        break;
                    }

                    if ((q.State == QuestState.EndedSuccess
                            || q.State == QuestState.EndedFailed
                            || q.State == QuestState.EndedUnknownOutcome)
                        && bestHistorical == null)
                    {
                        bestHistorical = q;
                        bestHistoricalSupport = support;
                        bestHistoricalSite = candidate;
                    }
                }

                if (bestOngoing != null)
                {
                    quest = bestOngoing;
                    supportPart = bestOngoingSupport;
                    site = bestOngoingSite;
                    return true;
                }

                if (bestHistorical != null)
                {
                    quest = bestHistorical;
                    supportPart = bestHistoricalSupport;
                    site = bestHistoricalSite;
                    return true;
                }
            }

            // 退路：世界对象中存在 OrbitalMechhive Site（即使任务引用异常也要能定位）。
            if (Find.WorldObjects != null)
            {
                foreach (WorldObject wo in Find.WorldObjects.AllWorldObjects)
                {
                    if (wo is Site s && s.MainSitePartDef == SitePartDefOf.OrbitalMechhive)
                    {
                        site = s;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 用于生成前的重复检查：仅当存在进行中 Gravcore_Mechhive 任务
        /// 或世界对象中已存在 OrbitalMechhive Site 时才返回 true，
        /// 以阻止生成第二个主巢任务 / 世界目标。
        /// 不删除、不标记、不销毁任何已有任务 / Site / 地图。
        /// </summary>
        private static bool TryHasBlockingExistingCerebrexTarget(out string reason)
        {
            reason = string.Empty;

            if (Find.QuestManager != null)
            {
                foreach (Quest q in Find.QuestManager.QuestsListForReading)
                {
                    if (q == null || q.root == null)
                    {
                        continue;
                    }

                    bool isGravcore = q.root == QuestScriptDefOf.Gravcore_Mechhive
                        || q.root.defName == "Gravcore_Mechhive";
                    if (!isGravcore)
                    {
                        continue;
                    }

                    if (q.State == QuestState.Ongoing)
                    {
                        reason = "已存在进行中的 Gravcore_Mechhive 任务。";
                        return true;
                    }
                }
            }

            if (Find.WorldObjects != null)
            {
                foreach (WorldObject wo in Find.WorldObjects.AllWorldObjects)
                {
                    if (wo is Site s
                        && s.MainSitePartDef == SitePartDefOf.OrbitalMechhive)
                    {
                        reason = "已存在 OrbitalMechhive 主巢世界目标。";
                        return true;
                    }
                }
            }

            return false;
        }

        // ===== 共生盟约环境准备 =====

        private static bool TryPrepareCovenantEnvironment(
            out GameComponent_SymbiosisCovenantState? state,
            out List<Faction> preparedMembers,
            out string message)
        {
            state = null;
            preparedMembers = new List<Faction>();
            message = string.Empty;

            // A. 启用机械族机械师剧情状态（不直接反射修改私有字段）。
            if (!GameComponent_MechanoidMechanitorScenarioState.IsEnabled)
            {
                GameComponent_MechanoidMechanitorScenarioState.EnableForCurrentGame();
                if (!GameComponent_MechanoidMechanitorScenarioState.IsEnabled)
                {
                    message = "无法启用机械族机械师剧情状态。";
                    return false;
                }
            }

            // B. 通过现有运行时配置工具启用共生盟约
            //    （负责关闭肃清指令、Normalize、刷新缓存与同步运行状态）。
            bool innerOk = false;
            string? innerMessage = null;
            bool bootstrapOk =
                MechanoidMechanitorStoryRuntimeBootstrapUtility
                    .TryExecuteWithRuntimeConfiguration(
                        (out string actionMessage) =>
                        {
                            innerOk = MechanoidMechanitorStoryRuntimeConfigurationUtility
                                .TrySetSymbiosisCovenantEnabled(
                                    true,
                                    out string reason);
                            actionMessage = reason;
                            innerMessage = reason;
                            return innerOk;
                        },
                        out string bootstrapMessage);

            if (!bootstrapOk
                || !innerOk
                || !GameComponent_SymbiosisCovenantState.IsActive)
            {
                message = "无法启用共生盟约（运行时配置未生效）："
                    + (innerMessage ?? bootstrapMessage ?? "原因未知");
                return false;
            }

            // C. 同步并验证盟约组件。
            state = GameComponent_SymbiosisCovenantState.CurrentComponent;
            if (state == null)
            {
                message = "找不到共生盟约 GameComponent。";
                return false;
            }

            state.SynchronizeNow();
            if (!state.Initialized)
            {
                message = "共生盟约初始化失败。";
                return false;
            }

            // D. 完成公开声明（合法发送现有机械巢报复信件，不重复发送）。
            if (!state.PublicDeclarationBroadcast)
            {
                if (!state.TryBroadcastPublicDeclaration())
                {
                    message = "无法广播公开脱离声明（可能受最终惩罚锁定或条件不足）。";
                    return false;
                }
            }

            if (!state.PublicDeclarationBroadcast)
            {
                message = "公开脱离声明未能生效。";
                return false;
            }

            // 验证机械巢已变为敌对；不得强行反射覆盖 FactionRelation。
            if (!Faction.OfMechanoids.HostileTo(Faction.OfPlayer))
            {
                message = "机械巢未变为敌对，可能受其他剧情状态或关系锁影响。";
                return false;
            }

            // E + F. 选择并准备测试盟约成员。
            List<Faction> candidates = SelectCerebrexMemberCandidates(state);
            if (candidates.Count == 0)
            {
                message = "当前世界缺少可生成战斗编组的普通派系。";
                return false;
            }

            List<Faction> succeeded = new List<Faction>();
            foreach (Faction faction in candidates)
            {
                if (succeeded.Count >= MaxSupportMembers)
                {
                    break;
                }

                if (TryPrepareCerebrexSupportMember(state, faction, out _))
                {
                    succeeded.Add(faction);
                }
            }

            if (succeeded.Count == 0)
            {
                message = "没有派系能够成功准备为参战盟约成员。";
                return false;
            }

            preparedMembers = succeeded;

            // G. 设置团结度与等级（不反射修改私有字段）。
            state.DevSetUnity(TargetUnity);
            state.DevRecalculateCovenantLevel();

            if (state.Unity < TargetUnity
                || state.CovenantLevel != TargetCovenantLevel
                || state.CovenantMemberCount < 1)
            {
                message =
                    "共生盟约环境验证失败（团结度="
                    + state.Unity.ToString("F0")
                    + "，等级=L"
                    + state.CovenantLevel
                    + "，成员数="
                    + state.CovenantMemberCount
                    + "）。";
                return false;
            }

            return true;
        }

        private static List<Faction> SelectCerebrexMemberCandidates(
            GameComponent_SymbiosisCovenantState state)
        {
            List<Faction> result = new List<Faction>();
            if (Find.FactionManager == null)
            {
                return result;
            }

            List<Faction> all = Find.FactionManager.AllFactionsListForReading
                .Where(f => f != null && IsValidCerebrexMemberCandidate(f))
                .ToList();

            // 优先级 1：已是盟约成员且具备基本资格。
            List<Faction> members = all
                .Where(f => state.GetRecord(f)?.CovenantMember == true)
                .ToList();
            // 优先级 2：已有记录但尚未加入。
            List<Faction> recorded = all
                .Where(f => state.GetRecord(f) != null
                    && !(state.GetRecord(f)?.CovenantMember == true))
                .ToList();
            // 优先级 3：其他可创建盟约记录的合法普通派系。
            List<Faction> others = all
                .Where(f => state.GetRecord(f) == null)
                .ToList();

            foreach (Faction f in members)
            {
                if (!result.Contains(f))
                {
                    result.Add(f);
                }
            }

            foreach (Faction f in recorded)
            {
                if (!result.Contains(f))
                {
                    result.Add(f);
                }
            }

            foreach (Faction f in others)
            {
                if (!result.Contains(f))
                {
                    result.Add(f);
                }
            }

            return result;
        }

        private static bool IsValidCerebrexMemberCandidate(Faction faction)
        {
            if (faction == null)
            {
                return false;
            }

            if (faction.IsPlayer)
            {
                return false;
            }

            if (faction == Faction.OfMechanoids)
            {
                return false;
            }

            if (faction.def == null)
            {
                return false;
            }

            if (faction.def.permanentEnemy)
            {
                return false;
            }

            if (faction.Hidden || faction.temporary
                || faction.defeated || faction.deactivated)
            {
                return false;
            }

            if (!MechanoidMechanitorOrdinaryFactionUtility.IsOrdinaryFaction(faction))
            {
                return false;
            }

            if (!faction.def.pawnGroupMakers.Any(
                    m => m.kindDef == PawnGroupKindDefOf.Combat))
            {
                return false;
            }

            return true;
        }

        private static bool TryPrepareCerebrexSupportMember(
            GameComponent_SymbiosisCovenantState state,
            Faction faction,
            out string message)
        {
            message = string.Empty;

            SymbiosisCovenantFactionRecord? record = state.GetRecord(faction);
            if (record == null)
            {
                if (!state.DevRecreateRecord(faction))
                {
                    message = "无法为 " + faction.Name + " 创建盟约记录。";
                    return false;
                }

                record = state.GetRecord(faction);
            }

            if (record == null)
            {
                message = faction.Name + " 的盟约记录创建后仍为空。";
                return false;
            }

            // 与玩家好感 +100（正式 Faction API 包装）。
            if (!SymbiosisCovenantDebugUtility.TrySetDevGoodwill(
                    faction,
                    Faction.OfPlayer,
                    100,
                    out string gwReason))
            {
                message = "无法将 " + faction.Name
                    + " 与玩家好感设为 +100：" + gwReason;
                return false;
            }

            if (faction.HostileTo(Faction.OfPlayer)
                || faction.RelationKindWith(Faction.OfPlayer) != FactionRelationKind.Ally)
            {
                message = faction.Name + " 与玩家未成功变为盟友（关系可能被锁定）。";
                return false;
            }

            // 与机械巢好感 -100。
            if (!SymbiosisCovenantDebugUtility.TrySetDevGoodwill(
                    faction,
                    Faction.OfMechanoids,
                    -100,
                    out string mhReason))
            {
                message = "无法将 " + faction.Name
                    + " 与机械巢好感设为 -100：" + mhReason;
                return false;
            }

            if (!faction.HostileTo(Faction.OfMechanoids))
            {
                message = faction.Name + " 与机械巢未成功变为敌对。";
                return false;
            }

            // 信任度至少 50（不降低玩家已有信任）。
            if (record.Trust < MinTrustForMember)
            {
                if (!state.DevSetTrust(faction, MinTrustForMember, DevTrustReason))
                {
                    message = "无法设置 " + faction.Name + " 的信任度。";
                    return false;
                }
            }

            // 加入盟约（若尚未加入）。
            if (!record.CovenantMember)
            {
                if (!state.DevForceJoinCovenant(faction))
                {
                    message = "无法让 " + faction.Name + " 加入盟约。";
                    return false;
                }
            }

            record = state.GetRecord(faction);
            if (record?.CovenantMember != true)
            {
                message = faction.Name + " 加入盟约后状态未生效。";
                return false;
            }

            // 资格检查：地图尚未生成时执行等价的静态检查，
            // 地图生成后再由正式 IsFactionEligible 验证。
            Map referenceMap = Find.CurrentMap;
            if (referenceMap == null)
            {
                message = "无法获取当前地图用于资格检查。";
                return false;
            }

            if (!SymbiosisCovenantCerebrexSupportUtility.IsFactionEligible(
                    faction,
                    referenceMap,
                    Faction.OfMechanoids,
                    state))
            {
                message = faction.Name + " 未通过主脑支援资格检查。";
                return false;
            }

            return true;
        }

        // ===== 生成合法原版主巢任务 =====

        private static bool TryGenerateCerebrexQuest(
            float points,
            out Quest? quest,
            out Site? site,
            out QuestPart_SymbiosisCovenantCerebrexSupport? supportPart,
            out string message)
        {
            quest = null;
            site = null;
            supportPart = null;
            message = string.Empty;

            // 1. 重复任务检查（外部通常已查，这里再确认一次）。
            if (TryHasBlockingExistingCerebrexTarget(out string blockReason))
            {
                message = blockReason;
                return false;
            }

            // 2. 建立 Slate 并执行原版 CanRun 检查（不得绕过原版条件）。
            Slate slate = new Slate();
            slate.Set("points", points);

            if (!QuestScriptDefOf.Gravcore_Mechhive.CanRun(slate, Find.World))
            {
                message = "Gravcore_Mechhive 任务当前无法运行"
                    + "（CanRun 返回 false），未绕过原版条件。";
                return false;
            }

            // 3. 严格通过原版 API 生成任务（autoAccept=true 的原版任务）。
            Quest? generated = QuestUtility.GenerateQuestAndMakeAvailable(
                QuestScriptDefOf.Gravcore_Mechhive,
                slate);

            if (generated == null)
            {
                message = "Gravcore_Mechhive 任务生成失败。";
                return false;
            }

            quest = generated;

            // 4. 验证 quest。
            if (quest.root != QuestScriptDefOf.Gravcore_Mechhive
                && (quest.root == null || quest.root.defName != "Gravcore_Mechhive"))
            {
                message = "生成的任务根定义不是 Gravcore_Mechhive。";
                return false;
            }

            if (quest.State != QuestState.Ongoing)
            {
                message = "生成的任务状态不是进行中（autoAccept 未生效）。";
                return false;
            }

            bool hasCore = false;
            supportPart = null;
            foreach (QuestPart p in quest.PartsListForReading)
            {
                if (p is QuestPart_CerebrexCore)
                {
                    hasCore = true;
                }
                else if (p is QuestPart_SymbiosisCovenantCerebrexSupport sp)
                {
                    supportPart = sp;
                }
            }

            if (!hasCore)
            {
                message = "生成的任务中缺少 QuestPart_CerebrexCore。";
                return false;
            }

            // 5. 支援 Part 缺失时回填（不谎报测试环境完成）。
            if (supportPart == null)
            {
                SymbiosisCovenantCerebrexSupportUtility.BackfillMissingSupportQuestParts();
                foreach (QuestPart p in quest.PartsListForReading)
                {
                    if (p is QuestPart_SymbiosisCovenantCerebrexSupport sp)
                    {
                        supportPart = sp;
                        break;
                    }
                }
            }

            if (supportPart == null)
            {
                message = "主脑任务已生成，但盟约支援 QuestPart 注入失败。";
                Log.Error(
                    "[MAP-CerebrexDebug] 主脑任务已生成但支援 QuestPart 注入失败；"
                    + "quest=" + quest.id
                    + "，缺失部件=QuestPart_SymbiosisCovenantCerebrexSupport。");
                return false;
            }

            // 6. 站点验证。
            site = supportPart.site;
            if (site == null)
            {
                message = "支援 QuestPart 未引用任何 Site。";
                return false;
            }

            if (site.MainSitePartDef != SitePartDefOf.OrbitalMechhive)
            {
                message = "生成的 Site 主站点部件不是 OrbitalMechhive。";
                return false;
            }

            if (!site.Spawned)
            {
                message = "生成的 Site 未加入世界对象。";
                return false;
            }

            // 7. 新任务地图尚未生成时，stage 应保持 WaitingForMap（仅记录）。
            if (!site.HasMap
                && supportPart.stage
                != QuestPart_SymbiosisCovenantCerebrexSupport
                    .CerebrexSupportStage.WaitingForMap)
            {
                Log.Warning(
                    "[MAP-CerebrexDebug] 支援 Part 在地图未生成时 stage 不是 "
                    + "WaitingForMap（" + supportPart.stage + "）。");
            }

            return true;
        }

        // ===== Pawn 转移与主巢地图生成 =====

        private static bool TryCreateTransferCaravan(
            Map sourceMap,
            List<Pawn> pawns,
            out Caravan? caravan,
            out string message)
        {
            caravan = null;
            message = string.Empty;

            Faction player = Faction.OfPlayer;
            if (player == null)
            {
                message = "找不到玩家派系。";
                return false;
            }

            // 再次验证所有 Pawn 仍处于合法状态。
            foreach (Pawn p in pawns)
            {
                if (p == null
                    || !p.Spawned
                    || p.Map != sourceMap
                    || p.Faction != player
                    || p.Dead
                    || p.Destroyed
                    || p.Discarded)
                {
                    message = "存在非法 Pawn，无法组成商队。";
                    return false;
                }
            }

            // 原版商队持有链：MakeCaravan 内部负责 DeSpawn / WorldPawn 持有关系。
            caravan = CaravanMaker.MakeCaravan(
                pawns,
                player,
                sourceMap.Tile,
                addToWorldPawnsIfNotAlready: true);

            if (caravan == null || !caravan.Spawned)
            {
                message = "临时商队生成失败。";
                return false;
            }

            // 验证所有 Pawn 均进入商队，且原地图不再 Spawn 这些 Pawn。
            foreach (Pawn p in pawns)
            {
                if (!caravan.PawnsListForReading.Contains(p))
                {
                    message = "部分 Pawn 未进入临时商队，已中止进入"
                        + "（不销毁任何 Pawn）。";
                    return false;
                }

                if (p.Spawned && p.Map == sourceMap)
                {
                    message = "部分 Pawn 仍停留在原地图，已中止进入"
                        + "（不销毁任何 Pawn）。";
                    return false;
                }
            }

            return true;
        }

        private static bool TryVerifyEnteredCerebrex(
            GameComponent_SymbiosisCovenantState state,
            Site site,
            QuestPart_SymbiosisCovenantCerebrexSupport supportPart,
            List<Pawn> pawns,
            List<Faction> preparedMembers,
            out string message)
        {
            message = string.Empty;

            Map? map = site.Map;
            if (map == null || !site.HasMap)
            {
                message = "主巢地图未成功生成。";
                return false;
            }

            if (map.generatorDef != MapGeneratorDefOf.Mechhive)
            {
                message = "生成的地图不是机械主巢地图。";
                return false;
            }

            if (map.listerThings.ThingsOfDef(ThingDefOf.CerebrexCore).Count == 0)
            {
                message = "主巢地图中未找到 CerebrexCore。";
                return false;
            }

            // 不擅自补生成 CerebrexStabilizer；按原版生成结果记录数量。
            int stabilizerCount =
                map.listerThings.ThingsOfDef(ThingDefOf.CerebrexStabilizer).Count;
            if (stabilizerCount == 0)
            {
                Log.Warning(
                    "[MAP-CerebrexDebug] 主巢地图中未找到 CerebrexStabilizer"
                    + "（按原版生成结果，不补生成）。");
            }

            // 至少一个转移 Pawn 在地图上。
            int enteredCount = pawns.Count(
                p => p != null && p.Spawned && p.Map == map && p.Faction == Faction.OfPlayer);
            if (enteredCount == 0)
            {
                message = "没有 Pawn 成功进入主巢地图。";
                return false;
            }

            // 支援 Part 已由 MapGenerated 信号启用。
            if (supportPart.State != QuestPartState.Enabled
                && supportPart.stage
                == QuestPart_SymbiosisCovenantCerebrexSupport
                    .CerebrexSupportStage.WaitingForMap)
            {
                message = "盟约支援 QuestPart 未由 MapGenerated 信号启用"
                    + "（仍处于 WaitingForMap）。";
                return false;
            }

            if (supportPart.stage
                == QuestPart_SymbiosisCovenantCerebrexSupport.CerebrexSupportStage.Invalid)
            {
                message = "盟约支援 QuestPart 进入无效状态（"
                    + (supportPart.invalidReason ?? "原因未知") + "）。";
                return false;
            }

            if (supportPart.mapEnteredTick < 0)
            {
                message = "支援 QuestPart 未记录进入 tick"
                    + "（MapGenerated 信号可能未触发）。";
                return false;
            }

            // 不应在此时已发送援助询问（offerDelayTicks 尚未经过）。
            if (supportPart.offerSent)
            {
                message = "支援 QuestPart 错误地提前发送了援助询问。";
                return false;
            }

            // 至少一个已准备成员通过正式资格检查（地图已生成后）。
            bool anyEligible = false;
            foreach (Faction member in preparedMembers)
            {
                if (member != null
                    && SymbiosisCovenantCerebrexSupportUtility.IsFactionEligible(
                        member,
                        map,
                        Faction.OfMechanoids,
                        state))
                {
                    anyEligible = true;
                    break;
                }
            }

            if (!anyEligible)
            {
                message = "进入后没有任何盟约成员通过主脑支援资格检查"
                    + "（测试环境搭建不完整）。";
                return false;
            }

            // offerId 应在 Enable 后生成；若为空仅告警，不阻断（核心环境已就绪）。
            if (string.IsNullOrEmpty(supportPart.offerId))
            {
                Log.Warning(
                    "[MAP-CerebrexDebug] 支援 Part 的 offerId 在地图生成后为空"
                    + "（请检查支援 Part 的 Enable 是否完整执行）。");
            }

            return true;
        }
    }
}
