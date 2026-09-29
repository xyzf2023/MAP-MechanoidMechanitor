using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class SymbiosisCovenantMilitaryAidLevelSettings
    {
        public int level;
        public float offerChance;
        public float supportPointsFactor;
    }

    public sealed class SymbiosisCovenantMilitaryAidResponderTier
    {
        public IntRange eligibleResponderCount = IntRange.One;
        public float additionalChance;
    }

    public sealed class SymbiosisCovenantMilitaryAidDef : Def
    {
        public List<SymbiosisCovenantMilitaryAidLevelSettings> levels =
            new List<SymbiosisCovenantMilitaryAidLevelSettings>();
        public List<SymbiosisCovenantMilitaryAidResponderTier> responderChanceTiers =
            new List<SymbiosisCovenantMilitaryAidResponderTier>();
        public List<string> triggerRaidIncidentDefNames = new List<string>();
        public float maxOfferChance = 0.85f;
        public float repeatedResponderWeight = 0.5f;
        public float minimumInitialActiveThreatCombatPower = 120f;
        public int evaluationDelayTicks = 600;
        public int offerTimeoutTicks = 5000;
        public int acceptedCooldownTicks = 60000;

        public SymbiosisCovenantMilitaryAidLevelSettings? GetLevelSettings(int level)
        {
            for (int i = 0; i < levels.Count; i++)
            {
                if (levels[i].level == level)
                {
                    return levels[i];
                }
            }
            return null;
        }

        public float GetResponderChanceBonus(int eligibleResponderCount)
        {
            for (int i = 0; i < responderChanceTiers.Count; i++)
            {
                SymbiosisCovenantMilitaryAidResponderTier tier = responderChanceTiers[i];
                if (eligibleResponderCount >= tier.eligibleResponderCount.min
                    && eligibleResponderCount <= tier.eligibleResponderCount.max)
                {
                    return tier.additionalChance;
                }
            }
            return 0f;
        }

        public bool CanTriggerFrom(IncidentDef? incidentDef)
        {
            return incidentDef != null
                && triggerRaidIncidentDefNames.Contains(incidentDef.defName);
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }

            HashSet<int> seenLevels = new HashSet<int>();
            for (int i = 0; i < levels.Count; i++)
            {
                SymbiosisCovenantMilitaryAidLevelSettings settings = levels[i];
                if (settings.level < 3 || settings.level > 5)
                {
                    yield return $"{defName}: 军事援助等级必须在 3..5 范围内，当前为 {settings.level}。";
                }
                if (!seenLevels.Add(settings.level))
                {
                    yield return $"{defName}: 军事援助等级 {settings.level} 重复。";
                }
                if (settings.offerChance < 0f || settings.offerChance > 1f)
                {
                    yield return $"{defName}: 等级 {settings.level} 的援助提议概率 offerChance 必须在 0..1 范围内。";
                }
                if (settings.supportPointsFactor < 0f)
                {
                    yield return $"{defName}: 等级 {settings.level} 的支援点数倍率 supportPointsFactor 不能为负。";
                }
            }

            for (int level = 3; level <= 5; level++)
            {
                if (!seenLevels.Contains(level))
                {
                    yield return $"{defName}: 缺少等级 {level} 的军事援助设置。";
                }
            }

            for (int i = 0; i < responderChanceTiers.Count; i++)
            {
                SymbiosisCovenantMilitaryAidResponderTier tier = responderChanceTiers[i];
                if (tier.eligibleResponderCount.min < 1
                    || tier.eligibleResponderCount.max < tier.eligibleResponderCount.min)
                {
                    yield return $"{defName}: responderChanceTiers[{i}] 的响应派系数量范围无效。";
                }
                if (tier.additionalChance < 0f || tier.additionalChance > 1f)
                {
                    yield return $"{defName}: responderChanceTiers[{i}] 的额外概率 additionalChance 无效。";
                }
                for (int j = i + 1; j < responderChanceTiers.Count; j++)
                {
                    SymbiosisCovenantMilitaryAidResponderTier other = responderChanceTiers[j];
                    if (tier.eligibleResponderCount.min <= other.eligibleResponderCount.max
                        && other.eligibleResponderCount.min <= tier.eligibleResponderCount.max)
                    {
                        yield return $"{defName}: responderChanceTiers[{i}] 与第 {j} 档重叠。";
                    }
                }
            }

            HashSet<string> incidentNames = new HashSet<string>();
            for (int i = 0; i < triggerRaidIncidentDefNames.Count; i++)
            {
                string name = triggerRaidIncidentDefNames[i];
                if (name.NullOrEmpty())
                {
                    yield return $"{defName}: triggerRaidIncidentDefNames 不能包含空值。";
                }
                else if (!incidentNames.Add(name))
                {
                    yield return $"{defName}: 触发袭击事件 defName '{name}' 重复。";
                }
            }

            if (triggerRaidIncidentDefNames.Count == 0)
            {
                yield return $"{defName}: 必须至少配置一个触发袭击事件的 defName。";
            }
            if (maxOfferChance < 0f || maxOfferChance > 1f)
            {
                yield return $"{defName}: maxOfferChance 必须在 0..1 范围内。";
            }
            if (repeatedResponderWeight < 0f
                || minimumInitialActiveThreatCombatPower < 0f
                || evaluationDelayTicks <= 0
                || offerTimeoutTicks <= 0
                || acceptedCooldownTicks < 0)
            {
                yield return $"{defName}: 军事援助全局设置包含无效值。";
            }
        }
    }

    [DefOf]
    public static class SymbiosisCovenantMilitaryAidDefOf
    {
        public static SymbiosisCovenantMilitaryAidDef MAP_SymbiosisCovenant_MilitaryAidConfig = null!;

        static SymbiosisCovenantMilitaryAidDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(SymbiosisCovenantMilitaryAidDefOf));
        }
    }
}
