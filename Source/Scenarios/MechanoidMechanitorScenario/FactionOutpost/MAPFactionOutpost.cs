#nullable enable
using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public enum MechanoidMechanitorFactionOutpostPhase : byte
    {
        Building = 0,
        Completed = 1
    }

    /// <summary>
    /// 普通派系前哨世界对象。只保存真实所属 Faction，不保存出生时敌对/盟友/中立标签。
    /// 后续袭击、援军与显示全部读取 Faction 当前实际外交关系。
    /// 建成且非敌对、无地图的前哨可作为 ITrader 供远行队交易/赠礼。
    /// </summary>
    public sealed class MAPFactionOutpost : Site, ITrader, ITraderRestockingInfoProvider
    {
        public const int NaturalBuildDurationTicks = 900000;

        // 旧存档回退值：仅用于无可读快照的旧前哨。
        // 新前哨的守军预算由 FactionOutpostThreatPointsUtility 按财富与设置计算并保存为快照，
        // 之后不再使用固定值。
        public const int LegacyBuildingGarrisonThreatPoints =
            FactionOutpostThreatPointsUtility.LegacyBuildingGarrisonThreatPoints;
        public const int LegacyCompletedGarrisonThreatPoints =
            FactionOutpostThreatPointsUtility.LegacyCompletedGarrisonThreatPoints;

        private const int ThreatClearCheckIntervalTicks = 250;

        private MechanoidMechanitorFactionOutpostPhase phase =
            MechanoidMechanitorFactionOutpostPhase.Building;
        private int createdTick = -1;
        private int naturalCompletionTick = -1;
        private int layoutSeed;
        private bool cleaned;
        private bool completionLetterSent;
        private bool cleanedLetterSent;
        private bool mapGarrisonInitialized;

        // 布局档位：由创建前哨时保存的完成态守军预算决定，保存后不再随财富或设置变化。
        // 与预算同时固定并存档；地图生成和建成切换只读取，不重新选档。
        private FactionOutpostLayoutTier layoutTier =
            FactionOutpostLayoutTier.Baseline;

        // 交易库存追踪器：建成非敌对、无地图前哨可向远行队提供交易/赠礼。
        private FactionOutpost_TraderTracker? trader;

        private FactionOutpost_TraderTracker TraderTracker
            => trader ??= new FactionOutpost_TraderTracker(this);

        public MechanoidMechanitorFactionOutpostPhase Phase => phase;
        public bool IsBuilding => phase == MechanoidMechanitorFactionOutpostPhase.Building;
        public bool IsCompleted => phase == MechanoidMechanitorFactionOutpostPhase.Completed;
        public bool Cleaned => cleaned;
        public int LayoutSeed => layoutSeed;
        public bool MapGarrisonInitialized => mapGarrisonInitialized;

        /// <summary>
        /// 当前前哨创建时固定的布局档位，供建筑生成步骤选择尺寸与建筑群数量。
        /// </summary>
        public FactionOutpostLayoutTier LayoutTier => layoutTier;

        // 守军点数快照：-1 表示旧存档没有该字段。
        // 创建时一次性计算并保存，之后不再随财富/设置变化。
        private int buildingGarrisonThreatPointsSnapshot = -1;
        private int completedGarrisonThreatPointsSnapshot = -1;

        public int GarrisonThreatPoints =>
            IsCompleted
                ? (completedGarrisonThreatPointsSnapshot > 0
                    ? completedGarrisonThreatPointsSnapshot
                    : LegacyCompletedGarrisonThreatPoints)
                : (buildingGarrisonThreatPointsSnapshot > 0
                    ? buildingGarrisonThreatPointsSnapshot
                    : LegacyBuildingGarrisonThreatPoints);

        /// <summary>
        /// 是否持有创建时保存的守军点数快照（新建前哨为 true，旧存档为 false）。
        /// </summary>
        public bool HasGarrisonThreatPointsSnapshot =>
            buildingGarrisonThreatPointsSnapshot > 0
            || completedGarrisonThreatPointsSnapshot > 0;

        /// <summary>
        /// 完成态守军点数快照；旧存档无可读快照时回退到旧固定完成态值。
        /// </summary>
        public int CompletedGarrisonThreatPointsSnapshot =>
            completedGarrisonThreatPointsSnapshot > 0
                ? completedGarrisonThreatPointsSnapshot
                : LegacyCompletedGarrisonThreatPoints;

        public override string Label
        {
            get
            {
                string ownerName = Faction?.Name ?? base.Label;
                return IsCompleted
                    ? "MAP_MechanoidMechanitor.FactionOutpost.WorldLabel.Completed"
                        .Translate(ownerName)
                    : "MAP_MechanoidMechanitor.FactionOutpost.WorldLabel.Building"
                        .Translate(ownerName);
            }
        }

        protected override bool UseGenericEnterMapFloatMenuOption => false;

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }

            if (!DebugSettings.ShowDevGizmos || !IsBuilding || cleaned)
            {
                yield break;
            }

            Command_Action completeConstruction = new Command_Action
            {
                defaultLabel =
                    "DEV：立即完成建设",
                defaultDesc =
                    "立即将这个建设中的派系前哨切换为建设完成状态。仅用于开发者测试。",
                action = DevForceCompleteConstruction
            };

            if (base.HasMap)
            {
                completeConstruction.Disable(
                    "前哨地图已加载，无法安全切换建设阶段。请先离开该地图。");
            }

            yield return completeConstruction;
        }

        private void DevForceCompleteConstruction()
        {
            if (!DebugSettings.ShowDevGizmos || !IsBuilding || cleaned || base.HasMap)
            {
                return;
            }

            SwitchToCompleted();
        }

        /// <summary>
        /// DEV 专用：跳过自然建造时间，直接切换为建成状态。正式游戏路径不得调用。
        /// 仅创建方便测试的前哨，避免重复发送完成信件。
        /// </summary>
        public void DevForceCompleteConstructionForTest()
        {
            if (!Prefs.DevMode || !IsBuilding || cleaned || base.HasMap)
            {
                return;
            }

            phase = MechanoidMechanitorFactionOutpostPhase.Completed;
            mapGarrisonInitialized = false;
            ReplaceSitePartForCurrentPhase();
            completionLetterSent = true;
        }

        /// <summary>
        /// 初始化一座新建前哨。referenceMap 为实际用于挑选该前哨 tile 的来源玩家殖民地地图，
        /// 用于按当前财富与设置一次性计算并保存守军点数快照。
        /// 旧存档不会调用本方法（其快照字段保持 -1，回退固定值）。
        /// </summary>
        public void InitializeNewOutpost(int createdTick, int layoutSeed, Map? referenceMap)
        {
            phase = MechanoidMechanitorFactionOutpostPhase.Building;
            this.createdTick = createdTick;
            naturalCompletionTick = createdTick + NaturalBuildDurationTicks;
            this.layoutSeed = layoutSeed;
            cleaned = false;
            completionLetterSent = false;
            cleanedLetterSent = false;
            mapGarrisonInitialized = false;

            int completed =
                FactionOutpostThreatPointsUtility.CalculateCompletedGarrisonPoints(referenceMap);
            int building =
                FactionOutpostThreatPointsUtility.CalculateBuildingGarrisonPoints(completed);
            completedGarrisonThreatPointsSnapshot = completed;
            buildingGarrisonThreatPointsSnapshot = building;

            // 布局档位由创建时保存的完成态预算决定，之后不再随财富/设置变化。
            layoutTier =
                FactionOutpostLayoutUtility.GetTierForCompletedPoints(
                    completedGarrisonThreatPointsSnapshot);

            // 地点定义只表示建设阶段，布局档位由地图生成步骤读取。
            ReplaceSitePartForCurrentPhase();
        }

        /// <summary>
        /// 仅按当前 phase 重建 SitePart 列表；建筑生成步骤另行读取已保存的布局档位。
        /// 创建路径、自然建成路径、DEV 建成路径都通过本方法切换 Part，不在此打开
        /// 原版自动 Pawn / Loot（SitePartParams 保持为空）。
        /// </summary>
        private void ReplaceSitePartForCurrentPhase()
        {
            parts.Clear();

            SitePartDef partDef = IsCompleted
                ? FactionOutpostDefOf.MAP_FactionOutpost_Completed
                : FactionOutpostDefOf.MAP_FactionOutpost_Building;

            AddPart(new SitePart(this, partDef, new SitePartParams()));
        }

        public void NotifyMapGarrisonInitialized(bool success)
        {
            mapGarrisonInitialized = success;
        }

        protected override void Tick()
        {
            base.Tick();

            // 交易库存周期性刷新/清理（仅在未生成地图时，与 Site 类似）。
            if (Spawned && !base.HasMap)
            {
                TraderTracker.TraderTrackerTick();
            }

            if (!cleaned
                && !base.HasMap
                && (Faction == null || Faction.defeated || Faction.deactivated))
            {
                Destroy();
                return;
            }

            TryHandleCompletionTiming();
            if (!cleaned
                && base.HasMap
                && mapGarrisonInitialized
                && Find.TickManager.TicksGame % ThreatClearCheckIntervalTicks == 0)
            {
                TryMarkCleanedIfNoActiveThreats();
            }
        }

        private void TryHandleCompletionTiming()
        {
            if (!IsBuilding || cleaned || base.HasMap || naturalCompletionTick < 0)
            {
                return;
            }

            if (Find.TickManager.TicksGame >= naturalCompletionTick)
            {
                SwitchToCompleted();
            }
        }

        private void SwitchToCompleted()
        {
            phase = MechanoidMechanitorFactionOutpostPhase.Completed;
            mapGarrisonInitialized = false;
            ReplaceSitePartForCurrentPhase();

            if (!completionLetterSent)
            {
                completionLetterSent = true;
                SendCompletionLetter();
            }
        }

        public void SendCreationLetter()
        {
            Faction? owner = Faction;
            if (owner == null)
            {
                return;
            }

            string textKey;
            LetterDef letterDef;
            switch (FactionOutpostRelationUtility.GetCurrentKind(this))
            {
                case FactionRelationKind.Hostile:
                    textKey = "MAP_MechanoidMechanitor.FactionOutpost.Letter.Created.Hostile";
                    letterDef = LetterDefOf.NegativeEvent;
                    break;
                case FactionRelationKind.Ally:
                    textKey = "MAP_MechanoidMechanitor.FactionOutpost.Letter.Created.Ally";
                    letterDef = LetterDefOf.PositiveEvent;
                    break;
                default:
                    textKey = "MAP_MechanoidMechanitor.FactionOutpost.Letter.Created.Neutral";
                    letterDef = LetterDefOf.NeutralEvent;
                    break;
            }

            Find.LetterStack.ReceiveLetter(
                "MAP_MechanoidMechanitor.FactionOutpost.Letter.Created.Label".Translate(),
                textKey.Translate(owner.Name),
                letterDef,
                new LookTargets(this),
                owner);
        }

        private void SendCompletionLetter()
        {
            Faction? owner = Faction;
            if (owner == null)
            {
                return;
            }

            string textKey;
            LetterDef letterDef;
            switch (FactionOutpostRelationUtility.GetCurrentKind(this))
            {
                case FactionRelationKind.Hostile:
                    textKey = "MAP_MechanoidMechanitor.FactionOutpost.Letter.Completed.Hostile";
                    letterDef = LetterDefOf.NegativeEvent;
                    break;
                case FactionRelationKind.Ally:
                    textKey = "MAP_MechanoidMechanitor.FactionOutpost.Letter.Completed.Ally";
                    letterDef = LetterDefOf.PositiveEvent;
                    break;
                default:
                    textKey = "MAP_MechanoidMechanitor.FactionOutpost.Letter.Completed.Neutral";
                    letterDef = LetterDefOf.NeutralEvent;
                    break;
            }

            Find.LetterStack.ReceiveLetter(
                "MAP_MechanoidMechanitor.FactionOutpost.Letter.Completed.Label".Translate(),
                textKey.Translate(owner.Name),
                letterDef,
                new LookTargets(this),
                owner);
        }

        /// <summary>
        /// 判断前哨是否可以标记为“已清理”并发出“派系哨点被摧毁”信件。
        /// 以 RimWorld 原版 FormCaravanComp.AnyActiveThreatNow 相同的 GenHostility 标准判断：
        /// 只有当前地图没有任何“活动敌对威胁”时，前哨才会标记为 Cleaned 并发送清理信件。
        /// 因此第三方敌对事件、敌对炮塔、休眠敌对单位等也会阻止清理（这是预期行为，
        /// 与原版“地图已无活动敌对威胁才允许重组远行队/清理”保持一致）。
        /// 同时统一了联合军事行动对 outpost.Cleaned 的成功判定：任务与原版标准一致，不另加例外。
        /// </summary>
        public bool TryMarkCleanedIfNoActiveThreats()
        {
            if (cleaned || !base.HasMap || !mapGarrisonInitialized || Faction == null)
            {
                return false;
            }

            Map map = base.Map;
            if (map == null || map.Disposed)
            {
                return false;
            }

            // 与原版 FormCaravanComp.AnyActiveThreatNow 完全等价的“整张地图活动敌对威胁”判定：
            // 不限于前哨所属派系。countDormantPawnsAsHostile=true 与 canBeFogged=!CanReformFoggedEnemies
            // 与原版保持一致。
            if (GenHostility.AnyHostileActiveThreatToPlayer(
                    map,
                    countDormantPawnsAsHostile: true,
                    canBeFogged: !CanReformFoggedEnemies))
            {
                return false;
            }

            cleaned = true;

            // 前哨确认无活动敌对威胁后，向联合军事行动系统发送一次精确清除通知。
            // 仅当本前哨恰好是某个活动联合行动的精确目标时，Utility 内部才会真正结算；
            // 普通（非联合行动）前哨调用不会产生任何任务影响。不在此处操作 Quest / 发奖励。
            SymbiosisCovenantJointOperationUtility.NotifyJointOperationTargetCleared(
                this,
                "MAPFactionOutpostCleaned");

            if (!cleanedLetterSent)
            {
                cleanedLetterSent = true;
                Find.LetterStack.ReceiveLetter(
                    "MAP_MechanoidMechanitor.FactionOutpost.Letter.Cleaned.Label".Translate(),
                    "MAP_MechanoidMechanitor.FactionOutpost.Letter.Cleaned.Text"
                        .Translate(Faction.Name),
                    LetterDefOf.PositiveEvent,
                    new LookTargets(this),
                    Faction);
            }

            return true;
        }

        public bool TryValidateMapReadyForEntry(out string? failMessage)
        {
            failMessage = null;
            if (base.HasMap && mapGarrisonInitialized)
            {
                return true;
            }

            failMessage =
                "MAP_MechanoidMechanitor.FactionOutpost.Attack.MapInitFailed".Translate();
            TryUnloadFailedEmptyMap();
            return false;
        }

        private void TryUnloadFailedEmptyMap()
        {
            if (!base.HasMap || mapGarrisonInitialized)
            {
                return;
            }

            Map map = base.Map;
            if (map == null || map.Disposed)
            {
                return;
            }

            Faction? player = Faction.OfPlayerSilentFail;
            if (player != null)
            {
                var pawns = map.mapPawns.AllPawns;
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn pawn = pawns[i];
                    if (pawn != null && (pawn.Faction == player || pawn.HostFaction == player))
                    {
                        return;
                    }
                }
            }

            try
            {
                Current.Game.DeinitAndRemoveMap(map, notifyPlayer: false);
            }
            catch (Exception ex)
            {
                Log.Warning("[MAP-机械族机械师] 卸载普通派系前哨失败初始化地图时发生异常: " + ex);
            }
        }

        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Caravan caravan)
        {
            // 不调用 Site.base：原版 Site 会自动追加 VisitSite，可绕过永久关系与最终转敌复查。
            foreach (FloatMenuOption option in
                FactionOutpostInteraction.GetCaravanFloatMenuOptions(this, caravan))
            {
                yield return option;
            }
        }

        public override IEnumerable<FloatMenuOption> GetTransportersFloatMenuOptions(
            IEnumerable<IThingHolder> pods,
            Action<PlanetTile, TransportersArrivalAction> launchAction)
        {
            // 同样不调用 Site.base，运输舱仅暴露本 MOD 自定义攻击抵达动作。
            foreach (FloatMenuOption option in FactionOutpostInteraction.GetTransporterFloatMenuOptions(
                this,
                pods,
                launchAction))
            {
                yield return option;
            }
        }

        public override IEnumerable<FloatMenuOption> GetShuttleFloatMenuOptions(
            IEnumerable<IThingHolder> pods,
            Action<PlanetTile, TransportersArrivalAction> launchAction)
        {
            // 原版不会在 Site 地块自动提供组建远行队；空菜单会误报内容将失踪。
            // 显式提供原版远行队抵达动作，后续交互仍走前哨现有的关系与进入检查。
            // 不调用 Site.base，避免通用 VisitSite 绕过这些限制。
            if (!Spawned || pods == null
                || !TransportersArrivalAction_FormCaravan.CanFormCaravanAt(pods, Tile))
            {
                yield break;
            }

            foreach (FloatMenuOption option in TransportersArrivalActionUtility.GetFloatMenuOptions(
                () => TransportersArrivalAction_FormCaravan.CanFormCaravanAt(pods, Tile),
                () => new TransportersArrivalAction_FormCaravan("MessageShuttleArrived"),
                "FormCaravanHere".Translate(),
                launchAction,
                Tile))
            {
                yield return option;
            }
        }

        public override bool ShouldRemoveMapNow(out bool alsoRemoveWorldObject)
        {
            alsoRemoveWorldObject = false;
            Map map = base.Map;
            if (map == null)
            {
                return false;
            }

            TryMarkCleanedIfNoActiveThreats();

            if (map.mapPawns.AnyPawnBlockingMapRemoval)
            {
                return false;
            }

            List<PocketMapParent> pocketMaps = Find.World.pocketMaps;
            if (pocketMaps != null)
            {
                for (int i = 0; i < pocketMaps.Count; i++)
                {
                    PocketMapParent pocket = pocketMaps[i];
                    if (pocket != null
                        && pocket.sourceMap == map
                        && pocket.Map != null
                        && pocket.Map.mapPawns.AnyPawnBlockingMapRemoval)
                    {
                        return false;
                    }
                }
            }

            if (map.AnyBuildingBlockingMapRemoval
                || TransporterUtility.IncomingTransporterPreventingMapRemoval(map))
            {
                return false;
            }

            alsoRemoveWorldObject = cleaned;
            return true;
        }

        public override void Notify_MyMapAboutToBeRemoved()
        {
            base.Notify_MyMapAboutToBeRemoved();
            mapGarrisonInitialized = false;
        }

        // ===== ITrader / ITraderRestockingInfoProvider（仅建成、非敌对、无地图的前哨可交易） =====

        public TraderKindDef TraderKind => TraderTracker.TraderKind;

        public IEnumerable<Thing> Goods => TraderTracker.StockListForReading;

        public int RandomPriceFactorSeed => TraderTracker.RandomPriceFactorSeed;

        public string TraderName => TraderTracker.TraderName;

        public TradeCurrency TradeCurrency => TradeCurrency.Silver;

        public IEnumerable<Thing> ColonyThingsWillingToBuy(Pawn playerNegotiator)
            => TraderTracker.ColonyThingsWillingToBuy(playerNegotiator);

        public void GiveSoldThingToTrader(Thing toGive, int countToGive, Pawn playerNegotiator)
            => TraderTracker.GiveSoldThingToTrader(toGive, countToGive, playerNegotiator);

        public void GiveSoldThingToPlayer(Thing toGive, int countToGive, Pawn playerNegotiator)
            => TraderTracker.GiveSoldThingToPlayer(toGive, countToGive, playerNegotiator);

        public bool EverVisited => TraderTracker.EverVisited;

        public bool RestockedSinceLastVisit => TraderTracker.RestockedSinceLastVisit;

        public int NextRestockTick => TraderTracker.NextRestockTick;

        /// <summary>
        /// 判断这个前哨是否允许玩家进行“非敌对服务”（交易/赠礼的共用资格）。
        /// 仅判断前哨自身状态与所属派系关系，不访问库存；库存状态由具体交互自行决定。
        /// </summary>
        public bool CanInteractAsFriendlyCompletedOutpost
        {
            get
            {
                return IsCompleted
                    && !cleaned
                    && Spawned
                    && !base.HasMap
                    && Faction != null
                    && Faction != Faction.OfPlayer
                    && !Faction.def.permanentEnemy
                    && !Faction.HostileTo(Faction.OfPlayer);
            }
        }

        /// <summary>
        /// 综合前哨服务资格与追踪器库存判断是否可交易：必须允许非敌对服务，且追踪器中有可交易库存种类。
        /// 交易确实需要商人存在可售/可交易库存，因此仍读取库存；赠礼不读取库存。
        /// </summary>
        public bool CanTradeNow
        {
            get
            {
                if (!CanInteractAsFriendlyCompletedOutpost)
                {
                    return false;
                }

                TraderKindDef kind = TraderKind;
                if (kind == null)
                {
                    return false;
                }

                List<Thing> stock = TraderTracker.StockListForReading;
                return stock.Any(t => kind.WillTrade(t.def));
            }
        }

        public float TradePriceImprovementOffsetForPlayer
            => TraderTracker.TradePriceImprovementOffsetForPlayer;

        public override void GetChildHolders(List<IThingHolder> outChildren)
        {
            base.GetChildHolders(outChildren);
            TraderTracker.GetChildHolders(outChildren);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(
                ref phase,
                "MAP_factionOutpost_phase",
                MechanoidMechanitorFactionOutpostPhase.Building);
            Scribe_Values.Look(ref createdTick, "MAP_factionOutpost_createdTick", -1);
            Scribe_Values.Look(
                ref naturalCompletionTick,
                "MAP_factionOutpost_naturalCompletionTick",
                -1);
            Scribe_Values.Look(ref layoutSeed, "MAP_factionOutpost_layoutSeed", 0);
            Scribe_Values.Look(ref cleaned, "MAP_factionOutpost_cleaned", false);
            Scribe_Values.Look(
                ref completionLetterSent,
                "MAP_factionOutpost_completionLetterSent",
                false);
            Scribe_Values.Look(
                ref cleanedLetterSent,
                "MAP_factionOutpost_cleanedLetterSent",
                false);
            Scribe_Values.Look(
                ref mapGarrisonInitialized,
                "MAP_factionOutpost_mapGarrisonInitialized",
                false);

            // 保存创建时固定的布局档位，读档不重新选档、不重建地图。
            Scribe_Values.Look(
                ref layoutTier,
                "MAP_factionOutpost_layoutTier",
                FactionOutpostLayoutTier.Baseline);

            // 守军点数快照：默认 -1 表示旧存档没有该字段，必须继续回退固定值，不得重新计算。
            Scribe_Values.Look(
                ref buildingGarrisonThreatPointsSnapshot,
                "MAP_factionOutpost_buildingGarrisonThreatPointsSnapshot",
                -1);
            Scribe_Values.Look(
                ref completedGarrisonThreatPointsSnapshot,
                "MAP_factionOutpost_completedGarrisonThreatPointsSnapshot",
                -1);

            // 交易库存追踪器存读档；旧存档无此项时，运行时首次访问会自动创建空 tracker。
            Scribe_Deep.Look(ref trader, "trader", this);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && trader == null)
            {
                trader = new FactionOutpost_TraderTracker(this);
            }
        }
    }
}
