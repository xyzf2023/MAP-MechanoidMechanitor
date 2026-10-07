using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 基础调度沿用原版 RandomMain 的平均间隔、事件筛选、人口倾向、
    /// 近期重复修正、异象候选池及事件参数生成。
    /// 原版类别选择方法为 private，因此在这里实现超时优先开关与主题类别修正。
    /// 主题倍率来自当前游戏的主题状态，不修改共享 Def，也不影响其他叙事者。
    /// </summary>
    public sealed class StorytellerComp_WheelOfFateRandomMain : StorytellerComp_RandomMain
    {
        private StorytellerCompProperties_WheelOfFateRandomMain WheelProps =>
            (StorytellerCompProperties_WheelOfFateRandomMain)props;

        internal float ExtraIncidentWeight(IncidentDef incident, IIncidentTarget target) =>
            IncidentChanceFinal(incident, target);

        public override IEnumerable<FiringIncident> MakeIntervalIncidents(IIncidentTarget target)
        {
            StorytellerCompProperties_WheelOfFateRandomMain settings = WheelProps;
            if (target == null || settings.categoryWeights.NullOrEmpty())
            {
                yield break;
            }

            GameComponent_WheelOfFateThemes? themeState = GameComponent_WheelOfFateThemes.Current;
            themeState?.UpdateTheme();
            StoryThemeDef? theme = themeState?.ActiveTheme;

            bool inSpace = target.Tile.Valid && target.Tile.LayerDef.isSpace;
            float mtbDays = settings.mtbDays * (theme?.EffectiveIncidentIntervalFactor ?? 1f);
            if (inSpace)
            {
                mtbDays *= settings.spaceMtbDayFactor;
                if ((long)GenTicks.TicksGame - Find.Storyteller.LastIncidentTick
                    < settings.spaceMinSpacingDays * GenDate.TicksPerDay)
                {
                    yield break;
                }
            }

            if (!StorytellerCompProperties_WheelOfFateRandomMain.IsFinitePositive(mtbDays)
                || !Rand.MTBEventOccurs(mtbDays, GenDate.TicksPerDay, 1000f))
            {
                yield break;
            }

            // 袭击信标地图的大型威胁交给专用 ThreatsGenerator，防止双重调度。
            bool skipThreatBig = settings.skipThreatBigIfRaidBeacon
                && target.IncidentTargetTags().Contains(IncidentTargetTagDefOf.Map_RaidBeacon);
            var candidates = new List<IncidentCategoryEntry>();
            var seenCategories = new HashSet<IncidentCategoryDef>();
            foreach (IncidentCategoryEntry entry in settings.categoryWeights)
            {
                if (entry?.category == null
                    || !StorytellerCompProperties_WheelOfFateRandomMain.IsFinitePositive(entry.weight)
                    || (skipThreatBig && entry.category == IncidentCategoryDefOf.ThreatBig)
                    || !seenCategories.Add(entry.category))
                {
                    continue;
                }

                float weight = entry.weight * (theme?.CategoryFactor(entry.category) ?? 1f);
                if (StorytellerCompProperties_WheelOfFateRandomMain.IsFinitePositive(weight))
                {
                    // 使用本次抽选的条目副本，不能将主题倍率写回共享 XML 配置。
                    candidates.Add(new IncidentCategoryEntry { category = entry.category, weight = weight });
                }
            }

            // 一个事件机会最多产出一个事件；当前类别不可用时尝试其余类别。
            // 等权主题合并类别后抽选，避免类别权重和类别内事件数量造成偏差。
            if (theme?.equalIncidentWeights == true)
            {
                if (UniformCandidates(target, candidates.Select(entry => entry.category))
                    .TryRandomElement(out var uniformIncident))
                    yield return uniformIncident;
                yield break;
            }

            if (theme?.invertIncidentWeights == true)
            {
                var weighted = WheelOfFateIncidentSelection.CollectCandidates(target,
                    candidates.Select(entry => entry.category), this,
                    incident => IncidentChanceFinal(incident, target), category => GenerateParms(category, target),
                    skipThreatBig: skipThreatBig, theme: theme);
                var categoryWeights = candidates.ToDictionary(entry => entry.category, entry => (double)entry.weight);
                var totals = weighted.GroupBy(entry => entry.Value.def.category)
                    .ToDictionary(group => group.Key, group => group.Sum(entry => entry.Weight));
                // C × w / S：先恢复类别内相对权重，再合并类别，最后统一取倒数。
                var combined = weighted.Select(entry =>
                    new WheelOfFateIncidentSelection.WeightedCandidate<FiringIncident>(entry.Value,
                        categoryWeights[entry.Value.def.category] * entry.Weight / totals[entry.Value.def.category]));
                if (WheelOfFateIncidentSelection.TrySelectInverse(combined, out var invertedIncident))
                    yield return invertedIncident;
                yield break;
            }

            // 候选集有限且每次移除一个条目，空池、零权重和失败回退均不会无限重试。
            while (candidates.Count > 0)
            {
                IncidentCategoryEntry? selected = null;
                if (StorytellerCompProperties_WheelOfFateRandomMain.IsFinitePositive(
                        settings.maxThreatBigIntervalDays)
                    && (long)GenTicks.TicksGame - target.StoryState.LastThreatBigTick
                        > settings.maxThreatBigIntervalDays * GenDate.TicksPerDay)
                {
                    // 只优先选择本来就在候选池且权重大于 0 的大型威胁类别。
                    selected = candidates.Find(entry => entry.category == IncidentCategoryDefOf.ThreatBig);
                }

                if (selected == null)
                {
                    if (!candidates.TryRandomElementByWeight(entry => entry.weight, out var randomEntry))
                    {
                        yield break;
                    }

                    selected = randomEntry;
                }

                candidates.Remove(selected);
                IncidentParms parms = GenerateParms(selected.category, target);
                if (TrySelectThemedIncident(
                    UsableIncidentsInCategory(selected.category, parms), out var incident, target, theme)
                    && WheelOfFateRaidFactionSelection.TryApply(incident, parms, theme))
                {
                    yield return new FiringIncident(incident, this, parms);
                    yield break;
                }
            }
        }

        internal List<FiringIncident> UniformCandidates(IIncidentTarget target,
            IEnumerable<IncidentCategoryDef>? categories = null, IncidentDef? excluded = null)
        {
            bool skipThreatBig = WheelProps.skipThreatBigIfRaidBeacon
                && target.IncidentTargetTags().Contains(IncidentTargetTagDefOf.Map_RaidBeacon);
            IEnumerable<IncidentCategoryDef> allowedCategories = categories
                ?? WheelProps.categoryWeights.Where(entry => entry?.category != null
                    && StorytellerCompProperties_WheelOfFateRandomMain.IsFinitePositive(entry.weight))
                    .Select(entry => entry.category);
            return WheelOfFateIncidentSelection.CollectCandidates(target, allowedCategories, this,
                incident => IncidentChanceFinal(incident, target), category => GenerateParms(category, target),
                skipThreatBig: skipThreatBig, excluded: excluded).Select(entry => entry.Value).ToList();
        }

        private bool TrySelectThemedIncident(IEnumerable<IncidentDef> incidents,
            out IncidentDef foundDef, IIncidentTarget target, StoryThemeDef? theme)
        {
            if (theme == null) return TrySelectRandomIncident(incidents, out foundDef, target);

            return WheelOfFateIncidentSelection.TrySelect(incidents, out foundDef, theme,
                incident => IncidentChanceFinal(incident, target));
        }
    }
}
