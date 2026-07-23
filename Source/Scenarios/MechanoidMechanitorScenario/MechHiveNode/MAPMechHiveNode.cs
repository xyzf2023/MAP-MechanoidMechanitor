using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>完整节点地图内容初始化结果。</summary>
    public enum MechHiveNodeMapInitState : byte
    {
        None = 0,
        Succeeded = 1,
        Failed = 2
    }

    /// <summary>
    /// 机械巢节点世界对象。继承原版 <see cref="Site"/>，同一个对象在“建设中/建设完成”两个阶段间
    /// 原地切换（保留 WorldObject ID），并保存节点生命周期的全部运行状态。
    /// 不计入 <see cref="Find.WorldObjects"/> 的 Settlements，仅作为自定义地点存在。
    /// </summary>
    public class MAPMechHiveNode : Site
    {
        /// <summary>正常建设时长：15 个游戏日。</summary>
        public const int NaturalBuildDurationTicks = 900000;

        /// <summary>材料交付后的加速完成延迟：3 个游戏小时。</summary>
        public const int AcceleratedCompletionDelayTicks = 7500;

        /// <summary>材料需求按完整游戏日阶梯衰减：12 个完整日后归零（720000 tick）。</summary>
        public const int DemandDecayDurationTicks = 720000;

        /// <summary>每个完整游戏日的 tick 数。</summary>
        public const int TicksPerDay = 60000;

        /// <summary>材料需求衰减所跨越的完整游戏日数。</summary>
        public const int DemandDecayFullDays = 12;

        /// <summary>建设中节点守军威胁点数。</summary>
        public const int BuildingGarrisonThreatPoints = 2000;

        /// <summary>完整节点守军威胁点数。</summary>
        public const int CompletedGarrisonThreatPoints = 10000;

        /// <summary>地图加载期间威胁清空检查间隔（tick）。</summary>
        private const int ThreatClearCheckIntervalTicks = 250;

        private MechanoidMechanitorMechHiveNodePhase phase =
            MechanoidMechanitorMechHiveNodePhase.Building;

        private int createdTick = -1;

        private int naturalCompletionTick = -1;

        private int acceleratedCompletionTick = -1;

        private bool naturalTimerStopped;

        private bool materialDelivered;

        private int layoutSeed;

        private ThingDef? demandMaterialDef;

        private int initialDemandCount;

        private bool cleaned;

        private bool completionLetterSent;

        private bool cleanedLetterSent;

        private MechHiveNodeMapInitState mapInitState = MechHiveNodeMapInitState.None;

        public MechanoidMechanitorMechHiveNodePhase Phase => phase;

        public bool IsBuilding => phase == MechanoidMechanitorMechHiveNodePhase.Building;

        public bool IsCompleted => phase == MechanoidMechanitorMechHiveNodePhase.Completed;

        public bool Cleaned => cleaned;

        public int LayoutSeed => layoutSeed;

        public int CreatedTick => createdTick;

        public ThingDef? DemandMaterialDef => demandMaterialDef;

        public int InitialDemandCount => initialDemandCount;

        public MechHiveNodeMapInitState MapInitState => mapInitState;

        /// <summary>地图内容已成功初始化，允许商队/运输舱进入。</summary>
        public bool IsMapContentReady =>
            mapInitState == MechHiveNodeMapInitState.Succeeded
            || (mapInitState == MechHiveNodeMapInitState.None && base.HasMap);

        /// <summary>完整节点地图初始化明确失败。</summary>
        public bool IsMapContentFailed => mapInitState == MechHiveNodeMapInitState.Failed;

        /// <summary>守军威胁点数：建设中 2000，完整 10000。建筑布局不占用该预算。</summary>
        public int GarrisonThreatPoints =>
            IsCompleted ? CompletedGarrisonThreatPoints : BuildingGarrisonThreatPoints;

        /// <summary>节点已存在的 tick 数。</summary>
        public int AgeTicks =>
            createdTick < 0 ? 0 : Mathf.Max(0, Find.TickManager.TicksGame - createdTick);

        /// <summary>
        /// 当前材料需求量。按已度过的完整游戏日阶梯变化（整数除法 age/60000），
        /// 公式：Ceil(初始量 × max(0, 1 - 完整天数/12))。
        /// 第 0 个完整日内保持初始量；720000 tick 时准确归零。
        /// 已交付、已清理、非建设阶段不会重新产生需求。
        /// </summary>
        public int CurrentDemandCount
        {
            get
            {
                if (cleaned
                    || materialDelivered
                    || !IsBuilding
                    || demandMaterialDef == null
                    || initialDemandCount <= 0)
                {
                    return 0;
                }

                int fullDays = AgeTicks / TicksPerDay;
                if (fullDays >= DemandDecayFullDays)
                {
                    return 0;
                }

                float factor = Mathf.Max(0f, 1f - (float)fullDays / DemandDecayFullDays);
                return Mathf.CeilToInt(initialDemandCount * factor);
            }
        }

        /// <summary>是否存在有效材料需求（不考虑关系）。</summary>
        public bool HasActiveMaterialDemand =>
            IsBuilding
            && !cleaned
            && !materialDelivered
            && demandMaterialDef != null
            && CurrentDemandCount > 0;

        /// <summary>是否已交付材料。</summary>
        public bool MaterialDelivered => materialDelivered;

        /// <summary>
        /// 是否显示并允许材料交付：需求有效且玩家与机械巢为盟友（含永久盟友）。
        /// </summary>
        public bool AllowsMaterialDelivery =>
            HasActiveMaterialDemand && MechHiveNodeRelationUtility.IsAllyForNode();

        // 自定义节点不使用原版“通用进入地图”入口，避免绕过关系限制。
        protected override bool UseGenericEnterMapFloatMenuOption => false;

        /// <summary>
        /// 初始化一个新生成的建设中节点。仅在世界对象创建时调用一次。
        /// </summary>
        public void InitializeNewNode(
            int createdTick,
            int layoutSeed,
            ThingDef? demandMaterialDef,
            int initialDemandCount)
        {
            this.phase = MechanoidMechanitorMechHiveNodePhase.Building;
            this.createdTick = createdTick;
            this.naturalCompletionTick = createdTick + NaturalBuildDurationTicks;
            this.acceleratedCompletionTick = -1;
            this.naturalTimerStopped = false;
            this.materialDelivered = false;
            this.layoutSeed = layoutSeed;
            this.demandMaterialDef = demandMaterialDef;
            this.initialDemandCount = Mathf.Max(0, initialDemandCount);
            this.cleaned = false;
            this.completionLetterSent = false;
            this.cleanedLetterSent = false;
            this.mapInitState = MechHiveNodeMapInitState.None;
        }

        public void NotifyMapContentInitStarted()
        {
            mapInitState = MechHiveNodeMapInitState.None;
        }

        public void NotifyMapContentInitSucceeded()
        {
            mapInitState = MechHiveNodeMapInitState.Succeeded;
        }

        public void NotifyMapContentInitFailed()
        {
            mapInitState = MechHiveNodeMapInitState.Failed;
        }

        /// <summary>
        /// 材料交付成功后调用：停止 15 天自然计时，清除需求，安排 3 小时后加速完成。
        /// 重复调用安全（已交付则忽略）。
        /// </summary>
        public void NotifyMaterialsDelivered()
        {
            if (materialDelivered)
            {
                return;
            }

            materialDelivered = true;
            naturalTimerStopped = true;
            acceleratedCompletionTick = Find.TickManager.TicksGame + AcceleratedCompletionDelayTicks;
        }

        protected override void Tick()
        {
            base.Tick();
            TryHandleUpgradeTiming();
            TryPeriodicThreatClearCheck();
        }

        /// <summary>
        /// 地图加载期间按固定间隔检查威胁；玩家仍在地图上时也必须能完成清理结算。
        /// </summary>
        private void TryPeriodicThreatClearCheck()
        {
            if (cleaned || !base.HasMap)
            {
                return;
            }

            if (Find.TickManager.TicksGame % ThreatClearCheckIntervalTicks != 0)
            {
                return;
            }

            TryMarkCleanedIfNoThreats();
        }

        /// <summary>
        /// 若当前地图已无有效节点威胁，则立即且仅一次标记为 Cleaned 并发送提示。
        /// 不依赖玩家是否仍在地图上，也不卸载地图。
        /// </summary>
        public bool TryMarkCleanedIfNoThreats()
        {
            if (cleaned)
            {
                return false;
            }

            Map map = base.Map;
            if (map == null || map.Disposed)
            {
                return false;
            }

            if (MechHiveNodeThreatUtility.AnyMechHiveThreatOnMap(map))
            {
                return false;
            }

            MarkCleaned();
            return true;
        }

        /// <summary>幂等清理状态转换：标记 Cleaned，停止参与未清理节点逻辑，并发送一次提示。</summary>
        private void MarkCleaned()
        {
            if (cleaned)
            {
                return;
            }

            cleaned = true;
            if (!cleanedLetterSent)
            {
                cleanedLetterSent = true;
                SendCleanedLetter();
            }
        }

        private void SendCleanedLetter()
        {
            Find.LetterStack.ReceiveLetter(
                "MAP_MechanoidMechanitor.MechHiveNode.Letter.Cleaned.Label".Translate(),
                "MAP_MechanoidMechanitor.MechHiveNode.Letter.Cleaned.Text".Translate(),
                LetterDefOf.PositiveEvent,
                new LookTargets(this),
                MechHiveNodeRelationUtility.GetMechHive());
        }

        /// <summary>
        /// 升级计时检查。地图仍加载时延迟；已清理则不再升级（等待移除）。
        /// </summary>
        private void TryHandleUpgradeTiming()
        {
            if (!IsBuilding || cleaned)
            {
                return;
            }

            // 地图仍加载：不在已生成地图中替换布局，延迟到地图卸载后再检查。
            if (base.HasMap)
            {
                return;
            }

            int now = Find.TickManager.TicksGame;
            bool shouldComplete = false;
            if (acceleratedCompletionTick >= 0 && now >= acceleratedCompletionTick)
            {
                shouldComplete = true;
            }
            else if (!naturalTimerStopped && naturalCompletionTick >= 0 && now >= naturalCompletionTick)
            {
                shouldComplete = true;
            }

            if (shouldComplete)
            {
                SwitchToCompleted();
            }
        }

        /// <summary>
        /// 原地切换为完整节点：仅替换主 SitePart 与阶段，不重建世界对象。
        /// </summary>
        private void SwitchToCompleted()
        {
            phase = MechanoidMechanitorMechHiveNodePhase.Completed;
            naturalTimerStopped = true;
            acceleratedCompletionTick = -1;
            mapInitState = MechHiveNodeMapInitState.None;

            // 切换主 SitePart（标签、说明、地图生成配置随之切换）。
            parts.Clear();
            SitePart part = new SitePart(
                this,
                MechHiveNodeDefOf.MAP_MechHiveNode_Completed,
                new SitePartParams());
            AddPart(part);

            if (!completionLetterSent)
            {
                completionLetterSent = true;
                SendCompletionLetter();
            }
        }

        /// <summary>建设中节点生成后发送的中性事件信封。</summary>
        public void SendCreationLetter()
        {
            Find.LetterStack.ReceiveLetter(
                "MAP_MechanoidMechanitor.MechHiveNode.Letter.Created.Label".Translate(),
                "MAP_MechanoidMechanitor.MechHiveNode.Letter.Created.Text".Translate(),
                LetterDefOf.NeutralEvent,
                new LookTargets(this),
                MechHiveNodeRelationUtility.GetMechHive());
        }

        /// <summary>
        /// 节点建设完毕信件：根据当时玩家与机械巢的实际关系选择语气与内容。
        /// </summary>
        private void SendCompletionLetter()
        {
            string label = "MAP_MechanoidMechanitor.MechHiveNode.Letter.Completed.Label".Translate();
            string text;
            LetterDef letterDef;

            if (MechHiveNodeRelationUtility.IsAlly())
            {
                text = "MAP_MechanoidMechanitor.MechHiveNode.Letter.Completed.Ally".Translate();
                letterDef = LetterDefOf.PositiveEvent;
            }
            else if (MechHiveNodeRelationUtility.IsHostile())
            {
                text = "MAP_MechanoidMechanitor.MechHiveNode.Letter.Completed.Hostile".Translate();
                letterDef = LetterDefOf.NegativeEvent;
            }
            else
            {
                text = "MAP_MechanoidMechanitor.MechHiveNode.Letter.Completed.Neutral".Translate();
                letterDef = LetterDefOf.NeutralEvent;
            }

            Find.LetterStack.ReceiveLetter(
                label,
                text,
                letterDef,
                new LookTargets(this),
                MechHiveNodeRelationUtility.GetMechHive());
        }

        public override bool ShouldRemoveMapNow(out bool alsoRemoveWorldObject)
        {
            alsoRemoveWorldObject = false;
            Map map = base.Map;
            if (map == null)
            {
                return false;
            }

            // 威胁清空与地图卸载拆分：先判定战斗完成（不受玩家 Pawn 阻挡）。
            TryMarkCleanedIfNoThreats();

            // 与原版 Site.ShouldRemoveMapNow 对齐：地图内阻止卸载的 Pawn。
            if (map.mapPawns.AnyPawnBlockingMapRemoval)
            {
                return false;
            }

            // 以当前地图为来源的口袋地图中，仍有阻止卸载的 Pawn（对齐原版 Site）。
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

            if (map.AnyBuildingBlockingMapRemoval)
            {
                return false;
            }

            if (TransporterUtility.IncomingTransporterPreventingMapRemoval(map))
            {
                return false;
            }

            // Cleaned：玩家离开后移除世界对象；未清理（撤退）：仅卸载地图，保留节点。
            alsoRemoveWorldObject = cleaned;
            return true;
        }

        /// <summary>
        /// 在确认没有任何玩家 Pawn / 运输舱内容进入后，安全卸载失败的空地图。
        /// 保留节点世界对象，供以后重新尝试；不标记 Cleaned。
        /// </summary>
        public void TryUnloadFailedEmptyMap()
        {
            if (!IsMapContentFailed || !base.HasMap)
            {
                return;
            }

            Map map = base.Map;
            if (map == null || map.Disposed)
            {
                return;
            }

            if (!IsFailedMapSafeToUnload(map))
            {
                return;
            }

            Current.Game.DeinitAndRemoveMap(map, notifyPlayer: false);
            mapInitState = MechHiveNodeMapInitState.None;
        }

        private static bool IsFailedMapSafeToUnload(Map map)
        {
            if (map.mapPawns.AnyPawnBlockingMapRemoval)
            {
                return false;
            }

            if (map.AnyBuildingBlockingMapRemoval)
            {
                return false;
            }

            if (TransporterUtility.IncomingTransporterPreventingMapRemoval(map))
            {
                return false;
            }

            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn == null || pawn.Destroyed)
                {
                    continue;
                }

                if (pawn.Faction == Faction.OfPlayer
                    || pawn.HostFaction == Faction.OfPlayer)
                {
                    return false;
                }
            }

            return true;
        }

        public override void Notify_MyMapRemoved(Map map)
        {
            base.Notify_MyMapRemoved(map);
            // 地图卸载后，失败状态可重置以便下次重新生成；成功状态同样清零。
            if (!cleaned)
            {
                mapInitState = MechHiveNodeMapInitState.None;
            }
        }

        public override string GetInspectString()
        {
            StringBuilder sb = new StringBuilder(base.GetInspectString());
            if (AllowsMaterialDelivery && demandMaterialDef != null)
            {
                if (sb.Length > 0)
                {
                    sb.AppendLine();
                }

                sb.Append(
                    "MAP_MechanoidMechanitor.MechHiveNode.MaterialDemand".Translate(
                        demandMaterialDef.LabelCap,
                        CurrentDemandCount));
            }

            return sb.ToString();
        }

        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Caravan caravan)
        {
            // 不调用 base（Site 会追加 VisitSite，可能绕过关系限制）。仅提供本 MOD 自定义入口。
            foreach (FloatMenuOption option in
                MechHiveNodeCaravanInteraction.GetFloatMenuOptions(this, caravan))
            {
                yield return option;
            }
        }

        public override IEnumerable<FloatMenuOption> GetTransportersFloatMenuOptions(
            IEnumerable<IThingHolder> pods,
            Action<PlanetTile, TransportersArrivalAction> launchAction)
        {
            // 不调用 base（避免 VisitSite 绕过限制）。完全由本 MOD 决定运输舱行为。
            foreach (FloatMenuOption option in
                MechHiveNodeTransportInteraction.GetFloatMenuOptions(this, pods, launchAction))
            {
                yield return option;
            }
        }

        public override IEnumerable<FloatMenuOption> GetShuttleFloatMenuOptions(
            IEnumerable<IThingHolder> pods,
            Action<PlanetTile, TransportersArrivalAction> launchAction)
        {
            // 不提供原版班车 VisitSite 入口，避免绕过永久关系与进攻限制（班车不作为节点抵达方式）。
            yield break;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref phase, "MAP_phase", MechanoidMechanitorMechHiveNodePhase.Building);
            Scribe_Values.Look(ref createdTick, "MAP_createdTick", -1);
            Scribe_Values.Look(ref naturalCompletionTick, "MAP_naturalCompletionTick", -1);
            Scribe_Values.Look(ref acceleratedCompletionTick, "MAP_acceleratedCompletionTick", -1);
            Scribe_Values.Look(ref naturalTimerStopped, "MAP_naturalTimerStopped", false);
            Scribe_Values.Look(ref materialDelivered, "MAP_materialDelivered", false);
            Scribe_Values.Look(ref layoutSeed, "MAP_layoutSeed", 0);
            Scribe_Defs.Look(ref demandMaterialDef, "MAP_demandMaterialDef");
            Scribe_Values.Look(ref initialDemandCount, "MAP_initialDemandCount", 0);
            Scribe_Values.Look(ref cleaned, "MAP_cleaned", false);
            Scribe_Values.Look(ref completionLetterSent, "MAP_completionLetterSent", false);
            Scribe_Values.Look(ref cleanedLetterSent, "MAP_cleanedLetterSent", false);
            Scribe_Values.Look(ref mapInitState, "MAP_mapInitState", MechHiveNodeMapInitState.None);
        }
    }
}
