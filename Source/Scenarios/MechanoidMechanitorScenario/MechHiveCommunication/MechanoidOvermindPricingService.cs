using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public static class MechanoidOvermindPricingService
    {
        private const int LightBaseCost = 0;

        private const int MediumBaseCost = 45;

        private const int HeavyBaseCost = 105;

        private const int UltraHeavyBaseCost = 210;

        private const int PriceRoundMultiple = 15;

        private static readonly Dictionary<MechanoidOvermindThingSpec, float> thingMarketValueCache =
            new Dictionary<MechanoidOvermindThingSpec, float>();

        private static readonly HashSet<MechanoidOvermindThingSpec> thingMarketValueFailureCache =
            new HashSet<MechanoidOvermindThingSpec>();

        private static Dictionary<PawnKindDef, int>? mechPriceOverrideCache;

        public static void ClearThingMarketValueCache()
        {
            thingMarketValueCache.Clear();
            thingMarketValueFailureCache.Clear();
        }

        public static void ClearMechPriceOverrideCache()
        {
            mechPriceOverrideCache = null;
        }

        public static int GetMechWeightBaseCost(MechWeightClassDef? weightClass)
        {
            if (weightClass == null)
            {
                return 0;
            }

            if (weightClass == MechWeightClassDefOf.Light)
            {
                return LightBaseCost;
            }

            if (weightClass == MechWeightClassDefOf.Medium)
            {
                return MediumBaseCost;
            }

            if (weightClass == MechWeightClassDefOf.Heavy)
            {
                return HeavyBaseCost;
            }

            if (weightClass == MechWeightClassDefOf.UltraHeavy)
            {
                return UltraHeavyBaseCost;
            }

            return 0;
        }

        public static bool TryGetMechBandwidthCost(PawnKindDef kind, out float bandwidth)
        {
            bandwidth = 0f;
            ThingDef? race = kind?.race;
            if (race?.race == null)
            {
                return false;
            }

            bandwidth = race.GetStatValueAbstract(StatDefOf.BandwidthCost);
            if (float.IsNaN(bandwidth) || float.IsInfinity(bandwidth) || bandwidth < 0f)
            {
                return false;
            }

            return true;
        }

        public static bool TryGetMechMarketValue(PawnKindDef kind, out float marketValue)
        {
            marketValue = 0f;
            ThingDef? race = kind?.race;
            if (race?.race == null)
            {
                return false;
            }

            marketValue = race.GetStatValueAbstract(StatDefOf.MarketValue);
            if (marketValue <= 0f)
            {
                marketValue = race.BaseMarketValue;
            }

            if (float.IsNaN(marketValue) || float.IsInfinity(marketValue) || marketValue < 0f)
            {
                return false;
            }

            return true;
        }

        public static bool TryGetMechUnitPrice(PawnKindDef kind, out int price)
        {
            price = 0;
            if (kind?.race?.race == null)
            {
                return false;
            }

            if (MechanoidOvermindCatalogService.IsMechPawnKindBlacklisted(kind))
            {
                return false;
            }

            if (TryGetMechPriceOverride(kind, out price))
            {
                return true;
            }

            if (!TryGetMechBandwidthCost(kind, out float bandwidth))
            {
                return false;
            }

            if (!TryGetMechMarketValue(kind, out float marketValue))
            {
                return false;
            }

            int baseCost = GetMechWeightBaseCost(kind.race.race.mechWeightClass);
            double raw = baseCost + (25.0 * bandwidth) + (2.0 * Math.Sqrt(marketValue));
            if (double.IsNaN(raw) || double.IsInfinity(raw) || raw < 0d)
            {
                return false;
            }

            return TryCeilToMultiple(raw, PriceRoundMultiple, out price);
        }

        public static bool TryGetThingUnitMarketValue(
            MechanoidOvermindThingSpec spec,
            out float marketValue)
        {
            marketValue = 0f;
            if (spec?.Def == null)
            {
                return false;
            }

            if (thingMarketValueCache.TryGetValue(spec, out float cached))
            {
                marketValue = cached;
                return IsFinitePositive(marketValue);
            }

            if (thingMarketValueFailureCache.Contains(spec))
            {
                return false;
            }

            if (!TryEvaluateThingMarketValue(spec, out marketValue))
            {
                thingMarketValueFailureCache.Add(spec);
                return false;
            }

            thingMarketValueCache[spec] = marketValue;
            return true;
        }

        public static bool TryCalculateOrderCosts(
            MechanoidOvermindOrder order,
            out int mechCost,
            out int thingCost,
            out int totalCost)
        {
            mechCost = 0;
            thingCost = 0;
            totalCost = 0;
            if (order == null)
            {
                return false;
            }

            if (!TryCalculateMechOrderCost(order, out mechCost))
            {
                return false;
            }

            if (!TryCalculateThingOrderCost(order, out thingCost))
            {
                return false;
            }

            long total = (long)mechCost + thingCost;
            if (total > int.MaxValue || total < 0)
            {
                return false;
            }

            totalCost = (int)total;
            return true;
        }

        public static bool TryCalculateMechOrderCost(MechanoidOvermindOrder order, out int mechCost)
        {
            mechCost = 0;
            if (order == null)
            {
                return false;
            }

            long sum = 0L;
            IReadOnlyList<MechanoidOvermindOrderLine_Mech> lines = order.MechLines;
            for (int i = 0; i < lines.Count; i++)
            {
                MechanoidOvermindOrderLine_Mech line = lines[i];
                if (line?.Kind == null
                    || line.Count <= 0
                    || line.Count > MechanoidOvermindOrder.MaxCount)
                {
                    return false;
                }

                if (!TryGetMechUnitPrice(line.Kind, out int unitPrice))
                {
                    return false;
                }

                long lineTotal = (long)unitPrice * line.Count;
                if (lineTotal < 0L || lineTotal > int.MaxValue)
                {
                    return false;
                }

                sum += lineTotal;
                if (sum > int.MaxValue)
                {
                    return false;
                }
            }

            mechCost = (int)sum;
            return true;
        }

        public static bool TryCalculateThingOrderCost(MechanoidOvermindOrder order, out int thingCost)
        {
            thingCost = 0;
            if (order == null)
            {
                return false;
            }

            if (GameComponent_OvermindEconomy.Enabled && order.ThingLines.Count > 0)
            {
                GameComponent_OvermindEconomy? economy = GameComponent_OvermindEconomy.Current;
                return economy != null && economy.TryCost(order, out thingCost);
            }

            IReadOnlyList<MechanoidOvermindOrderLine_Thing> lines = order.ThingLines;
            if (lines.Count == 0)
            {
                return true;
            }

            double marketValueSum = 0d;
            for (int i = 0; i < lines.Count; i++)
            {
                MechanoidOvermindOrderLine_Thing line = lines[i];
                if (line?.Spec?.Def == null
                    || line.Count <= 0
                    || line.Count > MechanoidOvermindOrder.MaxCount)
                {
                    return false;
                }

                if (!TryGetThingUnitMarketValue(line.Spec, out float unitValue))
                {
                    return false;
                }

                if (!IsFiniteNonNegative(unitValue))
                {
                    return false;
                }

                marketValueSum += (double)unitValue * line.Count;
                if (double.IsNaN(marketValueSum)
                    || double.IsInfinity(marketValueSum)
                    || marketValueSum < 0d)
                {
                    return false;
                }
            }

            double ceiled = Math.Ceiling(marketValueSum / 5d);
            if (double.IsNaN(ceiled) || double.IsInfinity(ceiled) || ceiled < 0d)
            {
                return false;
            }

            if (ceiled > int.MaxValue)
            {
                return false;
            }

            thingCost = (int)ceiled;
            if (thingCost < 1)
            {
                thingCost = 1;
            }

            return true;
        }

        private static bool TryGetMechPriceOverride(PawnKindDef kind, out int price)
        {
            price = 0;
            EnsureMechPriceOverrideCache();
            return mechPriceOverrideCache!.TryGetValue(kind, out price);
        }

        private static void EnsureMechPriceOverrideCache()
        {
            if (mechPriceOverrideCache != null)
            {
                return;
            }

            Dictionary<PawnKindDef, int> cache = new Dictionary<PawnKindDef, int>();
            List<MechanoidMechanitorPurgeTradePriceOverrideDef> defs =
                DefDatabase<MechanoidMechanitorPurgeTradePriceOverrideDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                MechanoidMechanitorPurgeTradePriceOverrideDef? overrideDef = defs[i];
                if (overrideDef == null)
                {
                    continue;
                }

                List<MechanoidMechanitorPurgeTradePriceOverrideEntry>? entries =
                    overrideDef.mechPriceOverrides;
                if (entries == null)
                {
                    continue;
                }

                for (int j = 0; j < entries.Count; j++)
                {
                    MechanoidMechanitorPurgeTradePriceOverrideEntry? entry = entries[j];
                    PawnKindDef? kind = entry?.mechPawnKind;
                    if (kind == null)
                    {
                        Log.Warning(
                            "[MAP] 肃清指令机械族固定价格配置包含空 PawnKindDef，已忽略。"
                            + " Def="
                            + overrideDef.defName
                            + "。条目索引="
                            + j
                            + "。");
                        continue;
                    }

                    if (entry!.price <= 0)
                    {
                        Log.Warning(
                            "[MAP] 肃清指令机械族固定价格必须大于 0，已忽略。"
                            + " Def="
                            + overrideDef.defName
                            + ", PawnKindDef="
                            + kind.defName
                            + ", Price="
                            + entry.price
                            + "。");
                        continue;
                    }

                    if (cache.ContainsKey(kind))
                    {
                        Log.Warning(
                            "[MAP] 肃清指令机械族固定价格存在重复配置，后续条目已忽略。"
                            + " Def="
                            + overrideDef.defName
                            + ", PawnKindDef="
                            + kind.defName
                            + ", Price="
                            + entry.price
                            + "。");
                        continue;
                    }

                    cache.Add(kind, entry.price);
                }
            }

            mechPriceOverrideCache = cache;
        }

        private static bool TryEvaluateThingMarketValue(
            MechanoidOvermindThingSpec spec,
            out float marketValue)
        {
            marketValue = 0f;
            try
            {
                ThingDef def = spec.Def;
                ThingDef? stuff = spec.Stuff;

                if (def.MadeFromStuff)
                {
                    if (stuff == null || !IsAllowedStuff(def, stuff))
                    {
                        return false;
                    }
                }
                else if (stuff != null)
                {
                    return false;
                }

                bool defHasQuality = def.HasComp(typeof(CompQuality));
                if (spec.HasQuality != defHasQuality)
                {
                    return false;
                }

                QualityCategory quality = QualityCategory.Normal;
                if (spec.HasQuality)
                {
                    if (!Enum.IsDefined(typeof(QualityCategory), spec.Quality))
                    {
                        return false;
                    }

                    quality = spec.Quality;
                }

                if (def.thingClass != null
                    && typeof(IFixedBaseMarketValue).IsAssignableFrom(def.thingClass)
                    && !def.StatBaseDefined(StatDefOf.MarketValue))
                {
                    LogThingMarketValueFailure(
                        spec,
                        "该物品的基础市场价值依赖实体实例，无法安全进行静态定价。",
                        null);
                    return false;
                }

                StatRequest request = StatRequest.For(def, stuff, quality);
                marketValue = StatDefOf.MarketValue.Worker.GetValue(request);
                if (!IsFinitePositive(marketValue))
                {
                    LogThingMarketValueFailure(
                        spec,
                        "原版静态市场价值计算返回了无效或非正数结果。",
                        null);
                    marketValue = 0f;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                LogThingMarketValueFailure(
                    spec,
                    "原版静态市场价值计算抛出异常。",
                    ex);
                marketValue = 0f;
                return false;
            }
        }

        private static void LogThingMarketValueFailure(
            MechanoidOvermindThingSpec spec,
            string reason,
            Exception? exception)
        {
            string stuff = spec.Stuff != null ? spec.Stuff.defName : "null";
            string quality = spec.HasQuality ? spec.Quality.ToString() : "无品质";
            string message =
                "[MAP] MechanoidOvermindPricingService 无法静态计算商品市场价值。"
                + " ThingDef="
                + spec.Def.defName
                + ", Stuff="
                + stuff
                + ", Quality="
                + quality
                + "。原因："
                + reason;

            if (exception != null)
            {
                message += " " + exception;
            }

            Log.Warning(message);
        }

        private static bool IsAllowedStuff(ThingDef def, ThingDef stuff)
        {
            if (stuff?.stuffProps == null)
            {
                return false;
            }

            foreach (ThingDef allowed in GenStuff.AllowedStuffsFor(def))
            {
                if (allowed == stuff)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryCeilToMultiple(double value, int multiple, out int result)
        {
            result = 0;
            if (multiple <= 0
                || double.IsNaN(value)
                || double.IsInfinity(value)
                || value < 0d)
            {
                return false;
            }

            double ceiled = Math.Ceiling(value / multiple) * multiple;
            if (double.IsNaN(ceiled) || double.IsInfinity(ceiled) || ceiled > int.MaxValue)
            {
                return false;
            }

            result = (int)ceiled;
            return result >= 0;
        }

        private static bool IsFinitePositive(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;
        }

        private static bool IsFiniteNonNegative(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
        }
    }
}
