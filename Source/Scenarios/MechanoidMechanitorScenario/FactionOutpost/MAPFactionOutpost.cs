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
    /// </summary>
    public sealed class MAPFactionOutpost : Site
    {
        public const int NaturalBuildDurationTicks = 900000;
        public const int BuildingGarrisonThreatPoints = 2000;
        public const int CompletedGarrisonThreatPoints = 10000;

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

        public MechanoidMechanitorFactionOutpostPhase Phase => phase;
        public bool IsBuilding => phase == MechanoidMechanitorFactionOutpostPhase.Building;
        public bool IsCompleted => phase == MechanoidMechanitorFactionOutpostPhase.Completed;
        public bool Cleaned => cleaned;
        public int LayoutSeed => layoutSeed;
        public bool MapGarrisonInitialized => mapGarrisonInitialized;
        public int GarrisonThreatPoints =>
            IsCompleted ? CompletedGarrisonThreatPoints : BuildingGarrisonThreatPoints;

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
                    "MAP_MechanoidMechanitor.FactionOutpost.Dev.CompleteConstruction.Label"
                        .Translate(),
                defaultDesc =
                    "MAP_MechanoidMechanitor.FactionOutpost.Dev.CompleteConstruction.Desc"
                        .Translate(),
                action = DevForceCompleteConstruction
            };

            if (base.HasMap)
            {
                completeConstruction.Disable(
                    "MAP_MechanoidMechanitor.FactionOutpost.Dev.CompleteConstruction.MapLoaded"
                        .Translate());
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

        public void InitializeNewOutpost(int createdTick, int layoutSeed)
        {
            phase = MechanoidMechanitorFactionOutpostPhase.Building;
            this.createdTick = createdTick;
            naturalCompletionTick = createdTick + NaturalBuildDurationTicks;
            this.layoutSeed = layoutSeed;
            cleaned = false;
            completionLetterSent = false;
            cleanedLetterSent = false;
            mapGarrisonInitialized = false;
        }

        public void NotifyMapGarrisonInitialized(bool success)
        {
            mapGarrisonInitialized = success;
        }

        protected override void Tick()
        {
            base.Tick();

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
                TryMarkCleanedIfNoDefenders();
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

            parts.Clear();
            AddPart(new SitePart(
                this,
                FactionOutpostDefOf.MAP_FactionOutpost_Completed,
                new SitePartParams()));

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

        public bool TryMarkCleanedIfNoDefenders()
        {
            if (cleaned || !base.HasMap || !mapGarrisonInitialized || Faction == null)
            {
                return false;
            }

            Map map = base.Map;
            if (map == null || map.Disposed
                || FactionOutpostThreatUtility.AnyStandingDefender(map, Faction))
            {
                return false;
            }

            cleaned = true;
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
                Log.Warning("[MAP] 卸载普通派系前哨失败初始化地图时发生异常: " + ex);
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
            // 不开放通用 Shuttle 进入，避免绕过永久关系限制与最终转敌复查。
            yield break;
        }

        public override bool ShouldRemoveMapNow(out bool alsoRemoveWorldObject)
        {
            alsoRemoveWorldObject = false;
            Map map = base.Map;
            if (map == null)
            {
                return false;
            }

            TryMarkCleanedIfNoDefenders();

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
        }
    }
}
