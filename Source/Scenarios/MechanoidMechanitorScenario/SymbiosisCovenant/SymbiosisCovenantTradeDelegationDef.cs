using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class SymbiosisCovenantDelegationMinimumStock
    {
        public ThingDef? thingDef;
        public int count;
    }

    public sealed class SymbiosisCovenantDelegationLevelSettings
    {
        public int level;
        public FloatRange intervalDays;
        public IntRange participantFactionCount = IntRange.One;
        public IntRange memberPawnCount = IntRange.One;
        public float guardPointMultiplier = 1f;
        public float silverMultiplier = 1f;
        public IntRange silverClamp = IntRange.Zero;
        public int logisticsSlots;
        public int rareSlots;
        public List<SymbiosisCovenantDelegationMinimumStock> minimumStock =
            new List<SymbiosisCovenantDelegationMinimumStock>();
        public IntRange contributionItemCount = IntRange.Zero;
        public FloatRange contributionMarketValue = FloatRange.Zero;
    }

    public sealed class SymbiosisCovenantDelegationFrequencyTier
    {
        public IntRange memberCount = IntRange.One;
        public float speedMultiplier = 1f;
    }

    public sealed class SymbiosisCovenantDelegationStockPoolEntry
    {
        public int minLevel = 2;
        public int maxLevel = 5;
        public float weight = 1f;
        public List<StockGenerator> generators = new List<StockGenerator>();

        public bool AvailableAt(int level)
        {
            return level >= minLevel && level <= maxLevel && weight > 0f && generators.Count > 0;
        }
    }

    public sealed class SymbiosisCovenantTradeDelegationDef : Def
    {
        public List<SymbiosisCovenantDelegationLevelSettings> levels =
            new List<SymbiosisCovenantDelegationLevelSettings>();
        public List<SymbiosisCovenantDelegationFrequencyTier> memberFrequencyTiers =
            new List<SymbiosisCovenantDelegationFrequencyTier>();
        public List<SymbiosisCovenantDelegationStockPoolEntry> logisticsPool =
            new List<SymbiosisCovenantDelegationStockPoolEntry>();
        public List<SymbiosisCovenantDelegationStockPoolEntry> rarePool =
            new List<SymbiosisCovenantDelegationStockPoolEntry>();
        public List<string> excludedContributionDefNames = new List<string>();
        public List<string> alwaysAllowedContributionDefNames = new List<string>();
        public int retryDelayTicks = 60000;
        public int maxShortRetries = 4;
        public int carrierThingDivisor = 8;
        public int maxCarriers = 8;
        public float repeatedLeadWeight = 0.5f;
        public float minimumIntervalDays = 5f;

        private TraderKindDef? generatorContext;

        public SymbiosisCovenantDelegationLevelSettings? GetLevelSettings(int level)
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

        public float GetMemberFrequencyMultiplier(int memberCount)
        {
            for (int i = 0; i < memberFrequencyTiers.Count; i++)
            {
                SymbiosisCovenantDelegationFrequencyTier tier = memberFrequencyTiers[i];
                if (memberCount >= tier.memberCount.min && memberCount <= tier.memberCount.max)
                {
                    return Math.Max(0.01f, tier.speedMultiplier);
                }
            }

            return 1f;
        }

        public bool IsContributionExplicitlyExcluded(ThingDef def)
        {
            return excludedContributionDefNames.Contains(def.defName);
        }

        public bool IsContributionExplicitlyAllowed(ThingDef def)
        {
            return alwaysAllowedContributionDefNames.Contains(def.defName);
        }

        public override void ResolveReferences()
        {
            base.ResolveReferences();
            generatorContext = new TraderKindDef
            {
                defName = defName + "_GeneratorContext",
                label = label.NullOrEmpty() ? defName : label,
                tradeCurrency = TradeCurrency.Silver
            };

            ResolvePool(logisticsPool);
            ResolvePool(rarePool);
        }

        private void ResolvePool(List<SymbiosisCovenantDelegationStockPoolEntry> pool)
        {
            for (int i = 0; i < pool.Count; i++)
            {
                List<StockGenerator> generators = pool[i].generators;
                for (int j = 0; j < generators.Count; j++)
                {
                    generators[j].ResolveReferences(generatorContext!);
                }
            }
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
                SymbiosisCovenantDelegationLevelSettings settings = levels[i];
                if (settings.level < 2 || settings.level > 5)
                {
                    yield return $"{defName}: delegation level must be in 2..5, got {settings.level}.";
                }
                if (!seenLevels.Add(settings.level))
                {
                    yield return $"{defName}: duplicate delegation level {settings.level}.";
                }
                if (settings.intervalDays.min <= 0f || settings.intervalDays.max < settings.intervalDays.min)
                {
                    yield return $"{defName}: invalid intervalDays for level {settings.level}.";
                }
                if (settings.participantFactionCount.min < 1
                    || settings.participantFactionCount.max < settings.participantFactionCount.min
                    || settings.memberPawnCount.min < 1
                    || settings.memberPawnCount.max < settings.memberPawnCount.min)
                {
                    yield return $"{defName}: invalid participant/member pawn range for level {settings.level}.";
                }
                if (settings.guardPointMultiplier <= 0f || settings.silverMultiplier <= 0f)
                {
                    yield return $"{defName}: non-positive multiplier for level {settings.level}.";
                }
                if (settings.silverClamp.min < 0 || settings.silverClamp.max < settings.silverClamp.min)
                {
                    yield return $"{defName}: invalid silverClamp for level {settings.level}.";
                }
                if (settings.logisticsSlots < 0
                    || settings.rareSlots < 0
                    || settings.contributionItemCount.min < 0
                    || settings.contributionItemCount.max < settings.contributionItemCount.min
                    || settings.contributionMarketValue.min < 0f
                    || settings.contributionMarketValue.max < settings.contributionMarketValue.min)
                {
                    yield return $"{defName}: invalid stock slot/contribution range for level {settings.level}.";
                }
                for (int j = 0; j < settings.minimumStock.Count; j++)
                {
                    SymbiosisCovenantDelegationMinimumStock minimum = settings.minimumStock[j];
                    if (minimum.thingDef == null || minimum.count < 0)
                    {
                        yield return $"{defName}: invalid minimumStock entry for level {settings.level}.";
                    }
                    else if (!minimum.thingDef.tradeability.TraderCanSell())
                    {
                        yield return $"{defName}: minimumStock {minimum.thingDef.defName} cannot be sold by traders.";
                    }
                }
            }

            for (int level = 2; level <= 5; level++)
            {
                if (!seenLevels.Contains(level))
                {
                    yield return $"{defName}: missing delegation settings for level {level}.";
                }
            }

            for (int i = 0; i < memberFrequencyTiers.Count; i++)
            {
                SymbiosisCovenantDelegationFrequencyTier tier = memberFrequencyTiers[i];
                if (tier.memberCount.min < 1
                    || tier.memberCount.max < tier.memberCount.min
                    || tier.speedMultiplier <= 0f)
                {
                    yield return $"{defName}: memberFrequencyTiers[{i}] is invalid.";
                }
                for (int j = i + 1; j < memberFrequencyTiers.Count; j++)
                {
                    SymbiosisCovenantDelegationFrequencyTier other = memberFrequencyTiers[j];
                    if (tier.memberCount.min <= other.memberCount.max
                        && other.memberCount.min <= tier.memberCount.max)
                    {
                        yield return $"{defName}: memberFrequencyTiers[{i}] overlaps tier {j}.";
                    }
                }
            }

            foreach (string error in ConfigErrorsForPool(logisticsPool, "logisticsPool"))
            {
                yield return error;
            }
            foreach (string error in ConfigErrorsForPool(rarePool, "rarePool"))
            {
                yield return error;
            }

            HashSet<string> excluded = new HashSet<string>();
            foreach (string name in excludedContributionDefNames)
            {
                if (name.NullOrEmpty())
                {
                    yield return $"{defName}: excluded contribution defName cannot be empty.";
                }
                else if (!excluded.Add(name))
                {
                    yield return $"{defName}: duplicate excluded contribution defName '{name}'.";
                }
            }

            HashSet<string> allowedSet = new HashSet<string>();
            foreach (string allowed in alwaysAllowedContributionDefNames)
            {
                if (allowed.NullOrEmpty())
                {
                    yield return $"{defName}: allowed contribution defName cannot be empty.";
                }
                else if (!allowedSet.Add(allowed))
                {
                    yield return $"{defName}: duplicate allowed contribution defName '{allowed}'.";
                }
                if (excluded.Contains(allowed))
                {
                    yield return $"{defName}: contribution defName '{allowed}' is in both allow and exclude lists.";
                }
            }

            if (retryDelayTicks <= 0 || maxShortRetries < 0 || carrierThingDivisor <= 0 || maxCarriers <= 0)
            {
                yield return $"{defName}: retry/carrier settings must be positive.";
            }
            if (repeatedLeadWeight < 0f || minimumIntervalDays <= 0f)
            {
                yield return $"{defName}: repeatedLeadWeight/minimumIntervalDays is invalid.";
            }
        }

        private IEnumerable<string> ConfigErrorsForPool(
            List<SymbiosisCovenantDelegationStockPoolEntry> pool,
            string poolName)
        {
            for (int i = 0; i < pool.Count; i++)
            {
                SymbiosisCovenantDelegationStockPoolEntry entry = pool[i];
                if (entry.minLevel < 2 || entry.maxLevel > 5 || entry.minLevel > entry.maxLevel)
                {
                    yield return $"{defName}: {poolName}[{i}] has invalid level range.";
                }
                if (entry.weight <= 0f)
                {
                    yield return $"{defName}: {poolName}[{i}] has non-positive weight.";
                }
                if (entry.generators.Count == 0)
                {
                    yield return $"{defName}: {poolName}[{i}] has no StockGenerator.";
                    continue;
                }

                for (int j = 0; j < entry.generators.Count; j++)
                {
                    StockGenerator generator = entry.generators[j];
                    if (generator == null || generatorContext == null)
                    {
                        continue;
                    }
                    foreach (string error in generator.ConfigErrors(generatorContext))
                    {
                        yield return $"{defName}: {poolName}[{i}]: {error}";
                    }
                }
            }
        }
    }

    [DefOf]
    public static class SymbiosisCovenantTradeDelegationDefOf
    {
        public static SymbiosisCovenantTradeDelegationDef MAP_SymbiosisCovenant_TradeDelegationConfig = null!;
        public static IncidentDef MAP_SymbiosisCovenant_TradeDelegation = null!;

        static SymbiosisCovenantTradeDelegationDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(SymbiosisCovenantTradeDelegationDefOf));
        }
    }
}
