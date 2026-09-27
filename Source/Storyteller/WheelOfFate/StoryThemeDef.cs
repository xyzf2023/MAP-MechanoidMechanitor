using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>XML 主题配置；只保存定义，不保存某次推演的运行状态。</summary>
    public sealed class StoryThemeDef : Def
    {
        public string themePoolTag = "MAP_WheelOfFate";
        public float selectionWeight = 1f;
        public int minDaysPassed;
        public FloatRange durationDays = new FloatRange(8f, 12f);
        public float incidentIntervalFactor = 1f;
        public bool equalIncidentWeights;
        public bool invertIncidentWeights;
        public bool triggerLinkedIncident;
        public bool triggerEncirclementRaids;
        public bool suppressRandomIncidents;
        public bool onlyPositiveRandomIncidents;
        public bool blockPositiveRandomIncidents;
        public float positiveRandomIncidentWeightFactor = 1f;
        // 补充由执行代码指定正面信件、而非在 IncidentDef 中声明信件类型的事件。
        public List<IncidentDef> positiveRandomIncidents = new List<IncidentDef>();
        public bool requiresActivatedMonolith;
        public PsychicRitualDef_VoidProvocation? initialVoidProvocation;
        public LetterDef? letterDef;
        public float charityWeightFactor = 1f;
        public float permanentEnemyRaidFactionFactor = 1f;
        public IntRange extraCharityIntervalTicks;
        public IntRange extraHerdMigrationIntervalTicks;
        public bool ignoreHerdMigrationAllowedBiomes;
        public float caravanSpeedFactor = 1f;
        public float gravEngineCooldownFactor = 1f;
        public IntRange initialWeatherDelayTicks;
        public List<IncidentDef> initialWeatherIncidents = new List<IncidentDef>();
        public bool randomizeNaturalWeather;
        public IntRange randomWeatherDurationTicks = new IntRange(15000, 45000);
        public List<WeatherDef> randomWeatherPool = new List<WeatherDef>();
        public List<IncidentCategoryEntry> categoryWeightFactors = new List<IncidentCategoryEntry>();
        public List<StoryThemeIncidentWeightRule> incidentWeightRules = new List<StoryThemeIncidentWeightRule>();
        public List<StoryThemeRaidFactionWeight> raidFactionWeightFactors = new List<StoryThemeRaidFactionWeight>();

        public bool CanSelect => !themePoolTag.NullOrEmpty()
            && !(equalIncidentWeights && invertIncidentWeights)
            && minDaysPassed >= 0
            && StorytellerCompProperties_WheelOfFateRandomMain.IsFiniteNonNegative(selectionWeight)
            && StorytellerCompProperties_WheelOfFateRandomMain.IsFinitePositive(durationDays.min)
            && StorytellerCompProperties_WheelOfFateRandomMain.IsFinitePositive(durationDays.max)
            && durationDays.max >= durationDays.min
            && durationDays.max <= int.MaxValue / (float)GenDate.TicksPerDay
            && StorytellerCompProperties_WheelOfFateRandomMain.IsFinitePositive(incidentIntervalFactor);

        // 开发者指定主题只跳过天数，仍需满足 DLC、玩法和巨石激活条件。
        public bool CanSelectInCurrentGame => CanSelect
            && (!requiresActivatedMonolith || WheelOfFateVoidProvocation.MonolithActivated);

        public bool CanSelectNow => CanSelectInCurrentGame
            && selectionWeight > 0f
            && GenDate.DaysPassedSinceSettleFloat >= minDaysPassed;

        public float RaidFactionFactor(FactionDef faction)
        {
            float factor = faction.permanentEnemy
                && StorytellerCompProperties_WheelOfFateRandomMain.IsFiniteNonNegative(permanentEnemyRaidFactionFactor)
                ? permanentEnemyRaidFactionFactor : 1f;
            if (raidFactionWeightFactors != null)
            {
                foreach (StoryThemeRaidFactionWeight entry in raidFactionWeightFactors)
                {
                    if (entry != null && entry.factionDef == faction
                        && StorytellerCompProperties_WheelOfFateRandomMain.IsFiniteNonNegative(entry.factor))
                        factor *= entry.factor;
                }
            }

            return factor;
        }

        public float CategoryFactor(IncidentCategoryDef category)
        {
            float factor = 1f;
            if (categoryWeightFactors != null)
            {
                foreach (IncidentCategoryEntry entry in categoryWeightFactors)
                {
                    if (entry?.category == category
                        && StorytellerCompProperties_WheelOfFateRandomMain.IsFiniteNonNegative(entry.weight))
                    {
                        factor *= entry.weight;
                    }
                }
            }

            return factor;
        }

        /// <summary>仅供自然随机调度筛选，不限制任务、队列及外部逻辑执行事件。</summary>
        public bool AllowsRandomIncident(IncidentDef incident)
        {
            if (!onlyPositiveRandomIncidents && !blockPositiveRandomIncidents) return true;
            bool positive = IsPositiveRandomIncident(incident);
            return (!onlyPositiveRandomIncidents || positive)
                && (!blockPositiveRandomIncidents || !positive);
        }

        private bool IsPositiveRandomIncident(IncidentDef incident) =>
            incident.letterDef == LetterDefOf.PositiveEvent
            || (incident.letterDef == null && positiveRandomIncidents != null
                && positiveRandomIncidents.Contains(incident));

        public float IncidentFactor(IncidentDef incident)
        {
            float factor = QuestFactor(incident.questScriptDef);
            if (IsPositiveRandomIncident(incident)
                && StorytellerCompProperties_WheelOfFateRandomMain.IsFiniteNonNegative(positiveRandomIncidentWeightFactor))
                factor *= positiveRandomIncidentWeightFactor;
            if (incidentWeightRules != null)
            {
                foreach (StoryThemeIncidentWeightRule rule in incidentWeightRules)
                {
                    if (rule != null && rule.Matches(incident))
                    {
                        factor *= rule.factor;
                    }
                }
            }

            return factor;
        }

        public float QuestFactor(QuestScriptDef? quest)
        {
            return quest?.defaultCharity == true
                && StorytellerCompProperties_WheelOfFateRandomMain.IsFiniteNonNegative(charityWeightFactor)
                ? charityWeightFactor : 1f;
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (equalIncidentWeights && invertIncidentWeights)
                yield return "命运之轮主题：等权抽选与倒数权重不能同时启用。";
            if (onlyPositiveRandomIncidents && blockPositiveRandomIncidents)
                yield return "命运之轮主题：仅允许正面随机事件与阻止正面随机事件不能同时启用。";
            if (themePoolTag.NullOrEmpty()) yield return "命运之轮主题：themePoolTag 不得为空。";
            if (minDaysPassed < 0) yield return "命运之轮主题：minDaysPassed 不得小于 0。";
            if (!StorytellerCompProperties_WheelOfFateRandomMain.IsFiniteNonNegative(selectionWeight))
                yield return "命运之轮主题：selectionWeight 必须是非负有限数值；0 表示不参与轮换。";
            if (!StorytellerCompProperties_WheelOfFateRandomMain.IsFinitePositive(durationDays.min)
                || !StorytellerCompProperties_WheelOfFateRandomMain.IsFinitePositive(durationDays.max)
                || durationDays.max < durationDays.min
                || durationDays.max > int.MaxValue / (float)GenDate.TicksPerDay)
                yield return "命运之轮主题：durationDays 必须是可转换为游戏 tick 的有效正数范围。";
            if (!StorytellerCompProperties_WheelOfFateRandomMain.IsFinitePositive(incidentIntervalFactor))
                yield return "命运之轮主题：incidentIntervalFactor 必须是大于 0 的有限数值。";
            if (letterDef == null) yield return "命运之轮主题：必须配置详细公告的 letterDef。";
            if (!StorytellerCompProperties_WheelOfFateRandomMain.IsFiniteNonNegative(positiveRandomIncidentWeightFactor))
                yield return "命运之轮主题：正面随机事件权重倍率必须为非负有限数值。";
            if (!StorytellerCompProperties_WheelOfFateRandomMain.IsFiniteNonNegative(charityWeightFactor)
                || !StorytellerCompProperties_WheelOfFateRandomMain.IsFiniteNonNegative(permanentEnemyRaidFactionFactor))
                yield return "命运之轮主题：仁善和永久敌对派系倍率必须为非负有限数值。";
            if (!ValidOptionalInterval(extraCharityIntervalTicks) || !ValidOptionalInterval(initialWeatherDelayTicks)
                || !ValidOptionalInterval(extraHerdMigrationIntervalTicks))
                yield return "命运之轮主题：额外事件间隔须为 0~0（停用）或有效正整数范围。";
            if (!StorytellerCompProperties_WheelOfFateRandomMain.IsFinitePositive(caravanSpeedFactor)
                || !StorytellerCompProperties_WheelOfFateRandomMain.IsFinitePositive(gravEngineCooldownFactor))
                yield return "命运之轮主题：远行队速度和逆重引擎冷却倍率必须为正有限数值。";
            if (initialWeatherDelayTicks.max > 0 && (initialWeatherIncidents.NullOrEmpty()
                || initialWeatherIncidents.Exists(incident => incident == null)))
                yield return "命运之轮主题：启用首次天气尝试时须配置有效事件。";
            if (label.NullOrEmpty() || description.NullOrEmpty())
                yield return "命运之轮主题：label 和 description 不得为空。";

            if (randomizeNaturalWeather)
            {
                if (randomWeatherDurationTicks.min <= 0
                    || randomWeatherDurationTicks.max < randomWeatherDurationTicks.min)
                    yield return "命运之轮主题：随机天气持续时间须为有效正整数范围。";
                var weathers = new HashSet<WeatherDef>();
                if (randomWeatherPool != null)
                {
                    foreach (WeatherDef weather in randomWeatherPool)
                    {
                        if (weather == null)
                            yield return "命运之轮主题：随机天气池须包含有效天气引用。";
                        else if (!weathers.Add(weather))
                            yield return $"命运之轮主题：随机天气 {weather.defName} 重复配置。";
                    }
                }
                if (weathers.Count < 2)
                    yield return "命运之轮主题：随机天气池至少需要两种不同天气，以避免连续重复。";
            }

            if (positiveRandomIncidents != null)
            {
                var incidents = new HashSet<IncidentDef>();
                foreach (IncidentDef incident in positiveRandomIncidents)
                {
                    if (incident == null)
                        yield return "命运之轮主题：正面随机事件补充名单须包含有效事件引用。";
                    else if (!incidents.Add(incident))
                        yield return $"命运之轮主题：正面随机事件 {incident.defName} 重复配置。";
                }
            }

            if (categoryWeightFactors != null)
            {
                var categories = new HashSet<IncidentCategoryDef>();
                foreach (IncidentCategoryEntry entry in categoryWeightFactors)
                {
                    if (entry?.category == null
                        || !StorytellerCompProperties_WheelOfFateRandomMain.IsFiniteNonNegative(entry.weight))
                        yield return "命运之轮主题：类别倍率必须包含有效类别和非负有限数值。";
                    else if (!categories.Add(entry.category))
                        yield return $"命运之轮主题：类别 {entry.category.defName} 的倍率重复配置。";
                }
            }

            if (incidentWeightRules != null)
            {
                foreach (StoryThemeIncidentWeightRule rule in incidentWeightRules)
                {
                    if (rule == null || !rule.IsValid)
                        yield return "命运之轮主题：事件规则须指定 incident 或 letterDef，factor 须为非负有限数值。";
                }
            }

            if (raidFactionWeightFactors != null)
            {
                var factions = new HashSet<FactionDef>();
                foreach (StoryThemeRaidFactionWeight entry in raidFactionWeightFactors)
                {
                    if (entry?.factionDef == null
                        || !StorytellerCompProperties_WheelOfFateRandomMain.IsFiniteNonNegative(entry.factor))
                        yield return "命运之轮主题：袭击派系倍率须指定有效 factionDef 和非负有限 factor。";
                    else if (!factions.Add(entry.factionDef))
                        yield return $"命运之轮主题：袭击派系 {entry.factionDef.defName} 重复配置。";
                }
            }
        }

        private static bool ValidOptionalInterval(IntRange range) =>
            (range.min == 0 && range.max == 0) || (range.min > 0 && range.max >= range.min);
    }

    /// <summary>仅影响主随机池产生的普通敌对袭击，不改动外交关系或任务指定派系。</summary>
    public sealed class StoryThemeRaidFactionWeight
    {
        public FactionDef? factionDef;
        public float factor = 1f;
    }

    /// <summary>同时填写事件与信封类型时按 AND 匹配，多条命中规则的倍率相乘。</summary>
    public sealed class StoryThemeIncidentWeightRule
    {
        public IncidentDef? incident;
        public LetterDef? letterDef;
        public float factor = 1f;

        public bool IsValid => (incident != null || letterDef != null)
            && StorytellerCompProperties_WheelOfFateRandomMain.IsFiniteNonNegative(factor);

        public bool Matches(IncidentDef candidate)
        {
            return IsValid && (incident == null || incident == candidate)
                && (letterDef == null || letterDef == candidate.letterDef);
        }
    }

    /// <summary>显式标记使用主题系统的叙事者及其主题池，其他叙事者不受影响。</summary>
    public sealed class WheelOfFateThemeExtension : DefModExtension
    {
        public string themePoolTag = "MAP_WheelOfFate";
        public StoryThemeDef? initialTheme;
        public float initialThemeMaxDaysPassed = 5f;

        public override IEnumerable<string> ConfigErrors()
        {
            if (themePoolTag.NullOrEmpty()) yield return "命运之轮：themePoolTag 不得为空。";
            if (!StorytellerCompProperties_WheelOfFateRandomMain.IsFiniteNonNegative(initialThemeMaxDaysPassed))
                yield return "命运之轮：开局主题最晚启用天数必须为非负有限数值。";
            if (initialTheme != null && (!initialTheme.CanSelect || initialTheme.themePoolTag != themePoolTag))
                yield return "命运之轮：开局主题须配置有效且属于当前主题池。";
        }
    }
}
