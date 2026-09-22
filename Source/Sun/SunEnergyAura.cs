using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class CompProperties_SunEnergyAura : CompProperties
    {
        public bool buildingForm;
        public CompProperties_SunEnergyAura() => compClass = typeof(CompSunEnergyAura);
    }

    /// <summary>仅玩家太阳挂载；地图索引随真实形态的生成与离图维护。</summary>
    public sealed class CompSunEnergyAura : ThingComp
    {
        public bool BuildingForm => ((CompProperties_SunEnergyAura)props).buildingForm;
        public bool Active => parent.Spawned && !parent.Destroyed
            && parent.Faction == Faction.OfPlayer
            && (!(parent is Pawn pawn) || (!pawn.Dead && pawn.RaceProps.IsMechanoid));

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            parent.Map.GetComponent<MapComponent_SunEnergyAura>().Register(this);
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            map.GetComponent<MapComponent_SunEnergyAura>().Deregister(this);
            if (parent is Pawn pawn) SunEnergyAuraUtility.SyncHealth(pawn);
            base.PostDeSpawn(map, mode);
        }

        public override void Notify_Killed(Map prevMap, DamageInfo? dinfo = null)
        {
            prevMap?.GetComponent<MapComponent_SunEnergyAura>().Deregister(this);
            base.Notify_Killed(prevMap, dinfo);
        }
    }

    public sealed class MapComponent_SunEnergyAura : MapComponent
    {
        private readonly HashSet<CompSunEnergyAura> providers = new HashSet<CompSunEnergyAura>();
        private List<Pawn> affected = new List<Pawn>();
        private bool dirty;

        public MapComponent_SunEnergyAura(Map map) : base(map) { }
        public void Register(CompSunEnergyAura provider) { providers.Add(provider); dirty = true; }
        public void Deregister(CompSunEnergyAura provider) { providers.Remove(provider); dirty = true; }

        internal int CoronaLevel
        {
            get
            {
                int level = 0;
                foreach (CompSunEnergyAura provider in providers)
                {
                    if (!provider.Active || provider.parent.Map != map) continue;
                    if (provider.BuildingForm) return 2;
                    level = 1;
                }
                return level;
            }
        }

        public override void MapComponentTick()
        {
            if (!dirty && providers.Count == 0 && affected.Count == 0) return;
            bool restore = GenTicks.TicksGame % 60 == 0;
            if (!dirty && !restore) return;
            dirty = false;
            // 旧接收者可能已经离图或被收入形态容器；不依赖容器内 Pawn 的 Tick 清理。
            foreach (Pawn pawn in affected)
                if (pawn != null && (!pawn.Spawned || pawn.Map != map))
                    SunEnergyAuraUtility.SyncHealth(pawn);
            affected.Clear();
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                int level = SunEnergyAuraUtility.Level(pawn);
                SunEnergyAuraUtility.SyncHealth(pawn);
                if (level == 0) continue;
                affected.Add(pawn);
                // 只在地图的固定周期结算，形态切换和读档补齐不能额外赠送回复。
                if (restore && MechanicalFlightEnergyUtility.TryGetEnergyFraction(pawn, out float energy))
                    MechanicalFlightEnergyUtility.TrySetEnergyFraction(pawn,
                        energy + SunEnergyAuraUtility.RestoreFraction(level));
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref affected, "sunEnergyAffected", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                affected ??= new List<Pawn>();
                dirty = true;
            }
        }
    }

    internal static class SunEnergyAuraUtility
    {
        // 0：无效果；1/2：机械体/建筑日冕；3：太阳自身。查询不写入状态。
        internal static int Level(Pawn? pawn)
        {
            if (pawn == null || !pawn.Spawned || pawn.Dead || pawn.Destroyed
                || pawn.Faction != Faction.OfPlayer) return 0;
            if (pawn.RaceProps.IsMechanoid)
            {
                CompSunEnergyAura? own = pawn.GetComp<CompSunEnergyAura>();
                if (own != null && own.Active && !own.BuildingForm) return 3;
            }
            else if (!MechFusionEnergyUtility.TryGetActiveSessionForWearer(pawn, out MechFusionSession? session)
                || session?.SourcePawn == null || !session.SourcePawn.RaceProps.IsMechanoid
                || session.SourcePawn.Faction != Faction.OfPlayer)
                return 0;
            return pawn.Map.GetComponent<MapComponent_SunEnergyAura>().CoronaLevel;
        }

        internal static float ConsumptionFactor(Pawn? pawn)
        {
            int level = Level(pawn);
            return level == 2 ? 0.05f : level == 1 ? 0.1f : 1f;
        }

        internal static float RestoreFraction(int level) =>
            level == 3 ? 0.01f : level == 2 ? 0.0025f : level == 1 ? 0.0005f : 0f;

        internal static void SyncHealth(Pawn pawn)
        {
            if (pawn.health == null) return;
            int level = Level(pawn);
            Sync(pawn, SunEnergyAuraDefOf.MAP_SunEnergy, level == 3 ? 1 : 0);
            Sync(pawn, SunEnergyAuraDefOf.MAP_SunCorona, level == 1 || level == 2 ? level : 0);
        }

        private static void Sync(Pawn pawn, HediffDef def, int severity)
        {
            Hediff? existing = pawn.health.hediffSet.GetFirstHediffOfDef(def);
            if (severity == 0)
            {
                if (existing != null) pawn.health.RemoveHediff(existing);
                return;
            }
            if (existing == null) existing = pawn.health.AddHediff(def);
            if (existing.Severity != severity) existing.Severity = severity;
        }
    }

    /// <summary>在地图同步前已失效的效果不再贡献属性，尤其是合体容器中的源 Pawn。</summary>
    public sealed class Hediff_SunEnergy : HediffWithComps
    {
        private static readonly HediffStage InactiveStage = new HediffStage();
        private bool Active
        {
            get
            {
                int level = SunEnergyAuraUtility.Level(pawn);
                return def == SunEnergyAuraDefOf.MAP_SunEnergy ? level == 3 : level == 1 || level == 2;
            }
        }
        public override HediffStage CurStage => Active ? base.CurStage : InactiveStage;
        public override bool ShouldRemove => !Active || base.ShouldRemove;
        public override string SeverityLabel => string.Empty;
    }

    public sealed class HediffExtraDescriptionProvider_SunEnergy : HediffExtraDescriptionProvider
    {
        public override IEnumerable<HediffExtraDescriptionEntry> GetEntries(Hediff hediff)
        {
            int level = SunEnergyAuraUtility.Level(hediff.pawn);
            if (level == 0) yield break;
            yield return new HediffExtraDescriptionEntry("MAP_SunEnergy_Restore",
                (SunEnergyAuraUtility.RestoreFraction(level) * 100f).ToString("0.##"));
            if (level != 3)
                yield return new HediffExtraDescriptionEntry("MAP_SunEnergy_Consumption",
                    level == 2 ? "5" : "10");
        }
    }

    [DefOf]
    public static class SunEnergyAuraDefOf
    {
        public static HediffDef MAP_SunEnergy = null!;
        public static HediffDef MAP_SunCorona = null!;
        static SunEnergyAuraDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(SunEnergyAuraDefOf));
    }
}
