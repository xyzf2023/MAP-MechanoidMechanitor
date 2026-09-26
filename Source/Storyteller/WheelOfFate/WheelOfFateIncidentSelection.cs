using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    internal static class WheelOfFateIncidentSelection
    {
        /// <summary>一次抽选内固化的候选及正向权重，不写入共享 Def 或存档。</summary>
        internal sealed class WeightedCandidate<T>
        {
            internal readonly T Value;
            internal readonly double Weight;

            internal WeightedCandidate(T value, double weight)
            {
                Value = value;
                Weight = weight;
            }
        }

        internal static bool IsFinitePositive(double weight) =>
            weight > 0d && !double.IsNaN(weight) && !double.IsInfinity(weight);

        /// <summary>收集完整事件池；保留资格限制，不预抽人口组或异象组。</summary>
        internal static List<WeightedCandidate<FiringIncident>> CollectCandidates(
            IIncidentTarget target, IEnumerable<IncidentCategoryDef> categories, StorytellerComp owner,
            Func<IncidentDef, float> baseWeight, Func<IncidentCategoryDef, IncidentParms> generateParms,
            bool skipThreatBig = false, IncidentDef? excluded = null, StoryThemeDef? theme = null)
        {
            var result = new List<WeightedCandidate<FiringIncident>>();
            var allowedCategories = new HashSet<IncidentCategoryDef>(categories);
            foreach (IncidentDef incident in DefDatabase<IncidentDef>.AllDefsListForReading)
            {
                if (incident == excluded || !allowedCategories.Contains(incident.category)
                    || (skipThreatBig && incident.category == IncidentCategoryDefOf.ThreatBig)
                    || !incident.TargetAllowed(target)) continue;
                float rawWeight = baseWeight(incident);
                if (!IsFinitePositive(rawWeight)) continue;
                double weight = (double)rawWeight * (theme?.IncidentFactor(incident) ?? 1f);
                if (!IsFinitePositive(weight)) continue;
                if (ModsConfig.AnomalyActive && !Find.Storyteller.difficulty.AnomalyPlaystyleDef.enableAnomalyContent
                    && incident.modContentPack != null && incident.modContentPack.IsOfficialMod
                    && ExpansionDefOf.Anomaly.linkedMod == incident.modContentPack.PackageId) continue;
                IncidentParms parms = generateParms(incident.category);
                if (incident.Worker.CanFireNow(parms))
                    result.Add(new WeightedCandidate<FiringIncident>(new FiringIncident(incident, owner, parms), weight));
            }
            return result;
        }

        /// <summary>以池内最小正权重统一缩放倒数，避免极小权重取倒数溢出。</summary>
        internal static bool TrySelectInverse<T>(IEnumerable<WeightedCandidate<T>> candidates, out T result)
        {
            var usable = candidates.Where(candidate => IsFinitePositive(candidate.Weight)).ToList();
            result = default!;
            if (usable.Count == 0) return false;
            if (usable.Count == 1)
            {
                result = usable[0].Value;
                return true;
            }
            double minimum = usable.Min(candidate => candidate.Weight);
            double total = usable.Sum(candidate => minimum / candidate.Weight);
            double remaining = (double)Rand.Value * total;
            foreach (var candidate in usable)
            {
                remaining -= minimum / candidate.Weight;
                if (remaining < 0d)
                {
                    result = candidate.Value;
                    return true;
                }
            }
            // Rand.Value 的上端点及累计舍入误差回退到最后一个有效候选。
            result = usable[usable.Count - 1].Value;
            return true;
        }

        /// <summary>
        /// 原版抽选方法非 virtual；保留其人口分组和回退规则，只替换权重委托。
        /// baseWeight 由调用组件提供，继续使用原版 IncidentChanceFinal。
        /// </summary>
        internal static bool TrySelect(IEnumerable<IncidentDef> incidents, out IncidentDef foundDef,
            StoryThemeDef theme, Func<IncidentDef, float> baseWeight)
        {
            // 先物化候选列表，避免人口分组失败后的回退再次执行 CanFireNow 筛选。
            List<IncidentDef> usable = incidents.ToList();
            if (theme.equalIncidentWeights)
                return usable.Where(incident =>
                    StorytellerCompProperties_WheelOfFateRandomMain.IsFinitePositive(baseWeight(incident)))
                    .TryRandomElement(out foundDef);
            bool preferPopulationIncrease = Rand.Chance(
                StorytellerComp.IncreasesPopChanceByPopIntentCurve.Evaluate(StorytellerUtilityPopulation.PopulationIntent));
            // 直接使用 Try 接口，避免默认 null 回退值使泛型被推断为 IncidentDef?。
            return usable.Where(incident =>
                    (incident.populationEffect != IncidentPopulationEffect.None) == preferPopulationIncrease)
                .TryRandomElementByWeight(Weight, out foundDef)
                || usable.TryRandomElementByWeight(Weight, out foundDef);

            float Weight(IncidentDef incident)
            {
                float weight = baseWeight(incident) * theme.IncidentFactor(incident);
                return StorytellerCompProperties_WheelOfFateRandomMain.IsFiniteNonNegative(weight)
                    ? weight : 0f;
            }
        }
    }
}
