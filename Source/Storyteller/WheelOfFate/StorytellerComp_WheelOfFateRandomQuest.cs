using System.Linq;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>保留原版任务周期；常规主题保留人口分组，等权和倒数主题合并任务池。</summary>
    public sealed class StorytellerComp_WheelOfFateRandomQuest : StorytellerComp_OnOffCycle
    {
        public override IncidentParms GenerateParms(IncidentCategoryDef incCat, IIncidentTarget target)
        {
            IncidentParms parms = base.GenerateParms(incCat, target);
            GameComponent_WheelOfFateThemes? state = GameComponent_WheelOfFateThemes.Current;
            state?.UpdateTheme();
            StoryThemeDef? theme = state?.ActiveTheme;
            if (theme?.equalIncidentWeights == true)
            {
                if (DefDatabase<QuestScriptDef>.AllDefsListForReading.Where(candidate =>
                        candidate.IsRootRandomSelected && candidate.CanRun(parms.points, target)
                        && StorytellerCompProperties_WheelOfFateRandomMain.IsFinitePositive(
                            NaturalRandomQuestChooser.GetNaturalRandomSelectionWeight(
                                candidate, parms.points, target.StoryState)))
                    .TryRandomElement(out var uniformQuest))
                    parms.questScriptDef = uniformQuest;
                return parms;
            }
            if (theme?.invertIncidentWeights == true)
            {
                var candidates = DefDatabase<QuestScriptDef>.AllDefsListForReading.Where(candidate =>
                        candidate.IsRootRandomSelected && candidate.CanRun(parms.points, target))
                    .Select(candidate => new WheelOfFateIncidentSelection.WeightedCandidate<QuestScriptDef>(candidate,
                        (double)NaturalRandomQuestChooser.GetNaturalRandomSelectionWeight(
                            candidate, parms.points, target.StoryState) * theme.QuestFactor(candidate)));
                if (WheelOfFateIncidentSelection.TrySelectInverse(candidates, out var invertedQuest))
                    parms.questScriptDef = invertedQuest;
                return parms;
            }
            if (theme == null || theme.charityWeightFactor == 1f)
            {
                parms.questScriptDef = NaturalRandomQuestChooser.ChooseNaturalRandomQuest(parms.points, target);
                return parms;
            }

            // 对齐 ChooseNaturalRandomQuest：人口增长分组失败后只回退到非增长组。
            bool increasesPopulation = Rand.Chance(NaturalRandomQuestChooser.PopulationIncreasingQuestChance());
            if (TryChoose(increasesPopulation, out var quest)
                || (increasesPopulation && TryChoose(false, out quest)))
                parms.questScriptDef = quest;
            return parms;

            bool TryChoose(bool incPop, out QuestScriptDef result)
            {
                return DefDatabase<QuestScriptDef>.AllDefsListForReading.Where(candidate =>
                        candidate.IsRootRandomSelected && candidate.rootIncreasesPopulation == incPop
                        && candidate.CanRun(parms.points, target))
                    .TryRandomElementByWeight(candidate =>
                        NaturalRandomQuestChooser.GetNaturalRandomSelectionWeight(candidate, parms.points, target.StoryState)
                        * theme.QuestFactor(candidate), out result);
            }
        }
    }

    public sealed class StorytellerCompProperties_WheelOfFateRandomQuest : StorytellerCompProperties_RandomQuest
    {
        public StorytellerCompProperties_WheelOfFateRandomQuest()
        {
            compClass = typeof(StorytellerComp_WheelOfFateRandomQuest);
        }
    }
}
