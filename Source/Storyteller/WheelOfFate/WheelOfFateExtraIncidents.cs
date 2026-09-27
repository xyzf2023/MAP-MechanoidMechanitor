using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>整局共享的主题额外尝试；保存期限，失败不补发，切换主题清空。</summary>
    public sealed class WheelOfFateExtraIncidents : IExposable
    {
        private int nextCharityTick = -1;
        private int nextHerdMigrationTick = -1;
        private int weatherTick = -1; // -1 尚未计时，-2 本轮已尝试。
        private QuestScriptDef? lastCharityQuest;
        private WheelOfFateRaidWaves raidWaves = new WheelOfFateRaidWaves();

        public void ExposeData()
        {
            Scribe_Values.Look(ref nextCharityTick, "nextCharityTick", -1);
            Scribe_Values.Look(ref nextHerdMigrationTick, "nextHerdMigrationTick", -1);
            Scribe_Values.Look(ref weatherTick, "weatherTick", -1);
            Scribe_Defs.Look(ref lastCharityQuest, "lastCharityQuest");
            Scribe_Deep.Look(ref raidWaves, "raidWaves");
            if (Scribe.mode == LoadSaveMode.PostLoadInit && raidWaves == null)
                raidWaves = new WheelOfFateRaidWaves();
        }

        internal void Reset()
        {
            nextCharityTick = -1;
            nextHerdMigrationTick = -1;
            weatherTick = -1;
            lastCharityQuest = null;
            raidWaves.Reset();
        }

        internal bool IsDue(int now) => (nextCharityTick >= 0 && now >= nextCharityTick)
            || (nextHerdMigrationTick >= 0 && now >= nextHerdMigrationTick)
            || (weatherTick >= 0 && now >= weatherTick)
            || raidWaves.IsDue(now);

        internal void Begin(StoryThemeDef theme)
        {
            Reset();
            InitializeTimers(theme, GenTicks.TicksGame);
            if (theme.triggerEncirclementRaids) raidWaves.Begin();
            // 只在新周期入口尝试一次；读档和逐 tick 的计时初始化不会补发。
            if (theme.initialVoidProvocation != null)
                WheelOfFateVoidProvocation.TryQueue(theme.initialVoidProvocation);
        }

        private void InitializeTimers(StoryThemeDef theme, int now)
        {
            if (Enabled(theme.extraHerdMigrationIntervalTicks) && nextHerdMigrationTick == -1)
                nextHerdMigrationTick = now + theme.extraHerdMigrationIntervalTicks.RandomInRange;
            if (Enabled(theme.extraCharityIntervalTicks) && nextCharityTick == -1)
                nextCharityTick = now + theme.extraCharityIntervalTicks.RandomInRange;
            if (Enabled(theme.initialWeatherDelayTicks) && weatherTick == -1)
                weatherTick = now + theme.initialWeatherDelayTicks.RandomInRange;
        }

        internal void Tick(StoryThemeDef? theme)
        {
            if (theme == null)
            {
                Reset();
                return;
            }

            int now = GenTicks.TicksGame;
            InitializeTimers(theme, now);
            if (!IsDue(now)) return;

            StorytellerComp_WheelOfFateRandomMain? comp = Find.Storyteller.storytellerComps
                .OfType<StorytellerComp_WheelOfFateRandomMain>().FirstOrDefault();
            raidWaves.Tick(now, comp);
            if (nextHerdMigrationTick >= 0 && now >= nextHerdMigrationTick)
            {
                nextHerdMigrationTick = Enabled(theme.extraHerdMigrationIntervalTicks)
                    ? now + theme.extraHerdMigrationIntervalTicks.RandomInRange : -1;
                if (comp != null) TryHerdMigration(comp);
            }
            if (nextCharityTick >= 0 && now >= nextCharityTick)
            {
                // 先安排下一轮，空池或执行失败均静默结束本次机会。
                nextCharityTick = Enabled(theme.extraCharityIntervalTicks)
                    ? now + theme.extraCharityIntervalTicks.RandomInRange : -1;
                if (comp != null) TryCharity(comp);
            }
            if (weatherTick >= 0 && now >= weatherTick)
            {
                weatherTick = -2;
                if (comp != null) TryWeather(comp, theme);
            }
        }

        private static bool Enabled(IntRange range) => range.min > 0 && range.max >= range.min;

        internal static Map? HomeMap()
        {
            Map current = Find.CurrentMap;
            if (current != null && current.IsPlayerHome) return current;
            return Current.Game.PlayerHomeMaps.TryRandomElement(out var map) ? map : null;
        }

        private void TryCharity(StorytellerComp_WheelOfFateRandomMain comp)
        {
            Map? map = HomeMap();
            if (map == null) return;
            var candidates = new List<CharityCandidate>();

            // 固定任务事件使用地图或世界目标。基础权重为零的专用事件只纳入
            // 当前叙事者已启用的周期组件（如乞讨者），不重放一次性开局剧情。
            foreach (IncidentDef incident in DefDatabase<IncidentDef>.AllDefsListForReading)
            {
                QuestScriptDef quest = incident.questScriptDef;
                if (quest?.defaultCharity != true || incident.category == null) continue;
                IIncidentTarget target = incident.TargetAllowed(map) ? (IIncidentTarget)map : Find.World;
                if (!incident.TargetAllowed(target)) continue;
                IncidentParms parms = comp.GenerateParms(incident.category, target);
                parms.questScriptDef = quest;
                float weight = comp.ExtraIncidentWeight(incident, target);
                if (incident.Worker.BaseChanceThisGame == 0f
                    && Find.Storyteller.storytellerComps.Any(source =>
                        source.props is StorytellerCompProperties_OnOffCycle cycle
                        && cycle.incident == incident && source.props.Enabled
                        && (source.props.allowedTargetTags.NullOrEmpty()
                            || source.props.allowedTargetTags.Intersect(target.IncidentTargetTags()).Any())
                        && (source.props.disallowedTargetTags.NullOrEmpty()
                            || !source.props.disallowedTargetTags.Intersect(target.IncidentTargetTags()).Any())
                        && GenDate.DaysPassedSinceSettleFloat > source.props.minDaysPassed))
                    weight = 1f;
                Add(incident, quest, parms, weight);
            }

            // 随机仁善任务必须保留原版最早天数、点数、进度、冷却和近期重复权重。
            IncidentDef randomIncident = IncidentDefOf.GiveQuest_Random;
            IncidentParms randomParms = comp.GenerateParms(randomIncident.category, Find.World);
            foreach (QuestScriptDef quest in DefDatabase<QuestScriptDef>.AllDefsListForReading)
            {
                if (!quest.defaultCharity || !quest.IsRootRandomSelected) continue;
                IncidentParms parms = randomParms.ShallowCopy();
                parms.questScriptDef = quest;
                Add(randomIncident, quest, parms, NaturalRandomQuestChooser.GetNaturalRandomSelectionWeight(
                    quest, parms.points, parms.target.StoryState));
            }

            // 也参考普通调度刚生成的仁善任务；同一脚本的不同事件入口视为重复。
            QuestScriptDef? previous = Find.QuestManager.QuestsListForReading
                .Where(quest => quest.charity && quest.root != null)
                .OrderByDescending(quest => quest.appearanceTick).FirstOrDefault()?.root ?? lastCharityQuest;
            if (candidates.Any(candidate => candidate.Quest != previous))
                candidates.RemoveAll(candidate => candidate.Quest == previous);
            bool incPop = Rand.Chance(NaturalRandomQuestChooser.PopulationIncreasingQuestChance());
            bool primaryOk = candidates.Where(candidate => candidate.IncreasesPopulation == incPop)
                .TryRandomElementByWeight(candidate => candidate.Weight, out var selectedPrimary);
            bool fallbackOk = candidates.TryRandomElementByWeight(candidate => candidate.Weight, out var selectedFallback);
            if (!primaryOk && !fallbackOk) return;
            CharityCandidate? selected = selectedPrimary ?? selectedFallback;
            if (selected == null) return;

            // CanFireNow 对同 Worker 的结果有同 tick 缓存，因此任务资格独立检查，
            // 最后仍由 TryFire 处理原版执行与 StoryState 成功记录。
            if (selected.Quest.CanRun(selected.Parms.points, selected.Parms.target)
                && Find.Storyteller.TryFire(new FiringIncident(selected.Incident, comp, selected.Parms)))
                lastCharityQuest = selected.Quest;

            void Add(IncidentDef incident, QuestScriptDef quest, IncidentParms parms, float weight)
            {
                if (!StorytellerCompProperties_WheelOfFateRandomMain.IsFinitePositive(weight)
                    || !quest.CanRun(parms.points, parms.target) || !incident.Worker.CanFireNow(parms)) return;
                candidates.Add(new CharityCandidate(incident, quest, parms, weight));
            }
        }

        private static void TryHerdMigration(StorytellerComp_WheelOfFateRandomMain comp)
        {
            Map? map = HomeMap();
            IncidentDef? incident = DefDatabase<IncidentDef>.GetNamedSilentFail("HerdMigration");
            if (map == null || incident?.category == null) return;
            IncidentParms parms = comp.GenerateParms(incident.category, map);
            // 与自然抽选共用白名单补丁；保留其他资格检查，失败不补发。
            if (incident.Worker.CanFireNow(parms))
                Find.Storyteller.TryFire(new FiringIncident(incident, comp, parms));
        }

        private static void TryWeather(StorytellerComp_WheelOfFateRandomMain comp, StoryThemeDef theme)
        {
            Map? map = HomeMap();
            if (map == null || theme.initialWeatherIncidents.NullOrEmpty()) return;
            var candidates = new List<FiringIncident>();
            foreach (IncidentDef incident in theme.initialWeatherIncidents.Distinct())
            {
                if (incident == null) continue;
                IncidentParms parms = comp.GenerateParms(incident.category, map);
                if (incident.Worker.CanFireNow(parms))
                    candidates.Add(new FiringIncident(incident, comp, parms));
            }
            if (candidates.TryRandomElement(out var selected)) Find.Storyteller.TryFire(selected);
        }

        private sealed class CharityCandidate
        {
            internal readonly IncidentDef Incident;
            internal readonly QuestScriptDef Quest;
            internal readonly IncidentParms Parms;
            internal readonly float Weight;
            internal bool IncreasesPopulation => Quest.rootIncreasesPopulation
                || Incident.populationEffect != IncidentPopulationEffect.None;

            internal CharityCandidate(IncidentDef incident, QuestScriptDef quest, IncidentParms parms, float weight)
            {
                Incident = incident;
                Quest = quest;
                Parms = parms;
                Weight = weight;
            }
        }
    }
}
