using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>每个主题周期只启动一次四波攻势；期限、目标与去重记录随主题额外事件存档。</summary>
    public sealed class WheelOfFateRaidWaves : IExposable
    {
        private int nextWaveTick = -1;
        private int wavesRemaining;
        private Map? targetMap;
        private List<Faction> usedFactions = new List<Faction>();
        private List<RaidStrategyDef> usedStrategies = new List<RaidStrategyDef>();
        private List<PawnsArrivalModeDef> usedArrivalModes = new List<PawnsArrivalModeDef>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref nextWaveTick, "nextWaveTick", -1);
            Scribe_Values.Look(ref wavesRemaining, "wavesRemaining", 0);
            Scribe_References.Look(ref targetMap, "targetMap");
            Scribe_Collections.Look(ref usedFactions, "usedFactions", LookMode.Reference);
            Scribe_Collections.Look(ref usedStrategies, "usedStrategies", LookMode.Def);
            Scribe_Collections.Look(ref usedArrivalModes, "usedArrivalModes", LookMode.Def);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                usedFactions ??= new List<Faction>();
                usedStrategies ??= new List<RaidStrategyDef>();
                usedArrivalModes ??= new List<PawnsArrivalModeDef>();
                usedFactions.RemoveAll(faction => faction == null);
                usedStrategies.RemoveAll(strategy => strategy == null);
                usedArrivalModes.RemoveAll(mode => mode == null);
                if (wavesRemaining < 1 || wavesRemaining > 4 || nextWaveTick < 0) Reset();
            }
        }

        internal void Reset()
        {
            nextWaveTick = -1;
            wavesRemaining = 0;
            targetMap = null;
            usedFactions.Clear();
            usedStrategies.Clear();
            usedArrivalModes.Clear();
        }

        internal void Begin()
        {
            Reset();
            wavesRemaining = 4;
            nextWaveTick = GenTicks.TicksGame + Rand.RangeInclusive(2500, 5000);
        }

        internal bool IsDue(int now) => wavesRemaining > 0 && nextWaveTick >= 0 && now >= nextWaveTick;

        internal void Tick(int now, StorytellerComp_WheelOfFateRandomMain? comp)
        {
            if (!IsDue(now)) return;
            // 首波选择一个殖民地，此后固定目标，切换查看地图不会转移后续攻势。
            if (wavesRemaining == 4) targetMap = WheelOfFateExtraIncidents.HomeMap();
            if (targetMap == null || !Find.Maps.Contains(targetMap) || !targetMap.IsPlayerHome)
            {
                Reset();
                return;
            }

            // 先消费机会，避免嵌套调用或失败在下一 tick 重复生成；四波结束后不重新计时。
            wavesRemaining--;
            nextWaveTick = wavesRemaining > 0 ? now + Rand.RangeInclusive(600, 1250) : -1;
            if (comp != null) TryRaid(comp, targetMap);
        }

        private void TryRaid(StorytellerComp_WheelOfFateRandomMain comp, Map map)
        {
            IncidentDef incident = IncidentDefOf.RaidEnemy;
            if (!(incident.Worker is IncidentWorker_RaidEnemy worker)) return;
            // 此处保持完整点数：战术/入场资格仍按正常袭击计算，最终倍率由生成入口应用。
            IncidentParms parms = comp.GenerateParms(incident.category, map);
            if (!StorytellerCompProperties_WheelOfFateRandomMain.IsFinitePositive(parms.points)
                || !worker.CanFireNow(parms)) return;

            var candidates = new Dictionary<Faction, List<RaidOption>>();
            foreach (Faction faction in WheelOfFateRaidFactionSelection.Candidates(worker, parms))
            {
                List<RaidOption> options = Options(parms, faction, map);
                if (options.Count > 0) candidates.Add(faction, options);
            }
            List<Faction> factions = candidates.Keys.ToList();
            // 优先本轮尚未登场的派系；可用派系不足时才允许重复，不强改外交关系。
            if (factions.Any(faction => !usedFactions.Contains(faction)))
                factions.RemoveAll(faction => usedFactions.Contains(faction));
            if (factions.Count == 0) return;
            // 在同样优先的派系之间也比较方式，避免抽到只能重复旧方式的派系。
            int minRepeats = factions.Min(faction => candidates[faction].Min(option => Repeats(option)));
            factions.RemoveAll(faction => candidates[faction].All(option => Repeats(option) > minRepeats));
            if (!factions.TryRandomElementByWeight(
                faction => WheelOfFateRaidFactionSelection.BaseWeight(faction, parms), out var selectedFaction)) return;

            List<RaidOption> choices = candidates[selectedFaction];
            if (!choices.Where(option => Repeats(option) == minRepeats)
                .TryRandomElementByWeight(option => option.Weight, out var selected)) return;

            if (WheelOfFateRaidPoints.TryFire(new FiringIncident(incident, comp, selected.Parms)))
            {
                usedFactions.Add(selected.Parms.faction);
                usedStrategies.Add(selected.Parms.raidStrategy);
                usedArrivalModes.Add(selected.Parms.raidArrivalMode);
            }
        }

        private int Repeats(RaidOption option) =>
            (usedStrategies.Contains(option.Parms.raidStrategy) ? 1 : 0)
            + (usedArrivalModes.Contains(option.Parms.raidArrivalMode) ? 1 : 0);

        private static List<RaidOption> Options(IncidentParms source, Faction faction, Map map)
        {
            var options = new List<RaidOption>();
            IncidentParms factionParms = source.ShallowCopy();
            factionParms.faction = faction;
            foreach (RaidStrategyDef strategy in DefDatabase<RaidStrategyDef>.AllDefsListForReading)
            {
                if (strategy.arriveModes.NullOrEmpty()
                    || !strategy.Worker.CanUseWith(factionParms, PawnGroupKindDefOf.Combat)) continue;
                float strategyWeight = strategy.Worker.SelectionWeightForFaction(map, faction, source.points);
                if (!StorytellerCompProperties_WheelOfFateRandomMain.IsFinitePositive(strategyWeight)) continue;
                IncidentParms strategyParms = factionParms.ShallowCopy();
                strategyParms.raidStrategy = strategy;
                var modes = new List<RaidOption>();
                foreach (PawnsArrivalModeDef mode in strategy.arriveModes.Distinct())
                {
                    if (mode == null || !mode.Worker.CanUseWith(strategyParms)) continue;
                    float weight = mode.Worker.GetSelectionWeight(strategyParms);
                    // 与原版私有 ModeWeight 一致，保留太空入场方式的最低权重。
                    if (map.Tile.Valid && map.Tile.LayerDef.isSpace)
                        weight = System.Math.Max(weight, mode.minSpaceSelectionWeight);
                    if (!StorytellerCompProperties_WheelOfFateRandomMain.IsFinitePositive(weight)) continue;
                    IncidentParms modeParms = strategyParms.ShallowCopy();
                    modeParms.raidArrivalMode = mode;
                    modes.Add(new RaidOption(modeParms, weight));
                }
                double total = modes.Sum(mode => (double)mode.Weight);
                foreach (RaidOption mode in modes)
                    options.Add(new RaidOption(mode.Parms, (float)(strategyWeight * (mode.Weight / total))));
            }
            return options;
        }

        private sealed class RaidOption
        {
            internal readonly IncidentParms Parms;
            internal readonly float Weight;

            internal RaidOption(IncidentParms parms, float weight)
            {
                Parms = parms;
                Weight = weight;
            }
        }
    }
}
