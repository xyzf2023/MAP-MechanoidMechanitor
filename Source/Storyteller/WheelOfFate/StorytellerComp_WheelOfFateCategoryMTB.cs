using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>用于命运之轮的世界杂项调度；保留原版间隔，只给事件抽选增加主题倍率。</summary>
    public sealed class StorytellerComp_WheelOfFateCategoryMTB : StorytellerComp_CategoryMTB
    {
        public override IEnumerable<FiringIncident> MakeIntervalIncidents(IIncidentTarget target)
        {
            if (target == null || Props.category == null) yield break;
            GameComponent_WheelOfFateThemes? state = GameComponent_WheelOfFateThemes.Current;
            state?.UpdateTheme();
            StoryThemeDef? theme = state?.ActiveTheme;
            float mtbDays = Props.mtbDays;
            if (Props.mtbDaysFactorByDaysPassedCurve != null)
                mtbDays *= Props.mtbDaysFactorByDaysPassedCurve.Evaluate(GenDate.DaysPassedSinceSettleFloat);
            if (!StorytellerCompProperties_WheelOfFateRandomMain.IsFinitePositive(mtbDays)
                || !Rand.MTBEventOccurs(mtbDays, GenDate.TicksPerDay, 1000f)) yield break;

            if (theme?.equalIncidentWeights == true)
            {
                StorytellerComp_WheelOfFateRandomMain? main = Find.Storyteller.storytellerComps
                    .OfType<StorytellerComp_WheelOfFateRandomMain>().FirstOrDefault();
                if (main != null && main.UniformCandidates(target, new[] { Props.category })
                    .TryRandomElement(out var uniformIncident))
                    yield return uniformIncident;
                yield break;
            }

            if (theme?.invertIncidentWeights == true)
            {
                var candidates = WheelOfFateIncidentSelection.CollectCandidates(target,
                    new[] { Props.category }, this, candidate => IncidentChanceFinal(candidate, target),
                    category => GenerateParms(category, target), theme: theme);
                if (WheelOfFateIncidentSelection.TrySelectInverse(candidates, out var invertedIncident))
                    yield return invertedIncident;
                yield break;
            }

            IncidentParms parms = GenerateParms(Props.category, target);
            IEnumerable<IncidentDef> incidents = UsableIncidentsInCategory(Props.category, parms);
            IncidentDef incident;
            bool selected = theme == null
                ? TrySelectRandomIncident(incidents, out incident, target)
                : WheelOfFateIncidentSelection.TrySelect(incidents, out incident, theme,
                    candidate => IncidentChanceFinal(candidate, target));
            if (selected) yield return new FiringIncident(incident, this, parms);
        }
    }

    public sealed class StorytellerCompProperties_WheelOfFateCategoryMTB : StorytellerCompProperties_CategoryMTB
    {
        public StorytellerCompProperties_WheelOfFateCategoryMTB()
        {
            compClass = typeof(StorytellerComp_WheelOfFateCategoryMTB);
        }

        public override IEnumerable<string> ConfigErrors(StorytellerDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef)) yield return error;
            if (category == null) yield return "命运之轮：世界事件调度必须指定类别。";
            if (!StorytellerCompProperties_WheelOfFateRandomMain.IsFinitePositive(mtbDays))
                yield return "命运之轮：世界事件调度 mtbDays 必须是大于 0 的有限数值。";
        }
    }
}
