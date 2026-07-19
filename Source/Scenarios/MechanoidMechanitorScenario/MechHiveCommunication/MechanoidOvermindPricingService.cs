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

        public static void ClearThingMarketValueCache()
        {
            thingMarketValueCache.Clear();
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
                return IsFiniteNonNegative(marketValue);
            }

            if (!TryEvaluateThingMarketValue(spec, out marketValue))
            {
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

        private static bool TryEvaluateThingMarketValue(
            MechanoidOvermindThingSpec spec,
            out float marketValue)
        {
            marketValue = 0f;
            Thing? temp = null;
            try
            {
                if (spec.Def.MadeFromStuff)
                {
                    if (spec.Stuff == null || !IsAllowedStuff(spec.Def, spec.Stuff))
                    {
                        return false;
                    }

                    temp = ThingMaker.MakeThing(spec.Def, spec.Stuff);
                }
                else
                {
                    if (spec.Stuff != null)
                    {
                        return false;
                    }

                    temp = ThingMaker.MakeThing(spec.Def);
                }

                if (temp == null)
                {
                    return false;
                }

                if (spec.HasQuality)
                {
                    CompQuality? qualityComp = temp.TryGetComp<CompQuality>();
                    if (qualityComp == null)
                    {
                        return false;
                    }

                    qualityComp.SetQuality(spec.Quality, null);
                }

                if (temp.def.useHitPoints)
                {
                    temp.HitPoints = temp.MaxHitPoints;
                }

                marketValue = temp.GetStatValue(StatDefOf.MarketValue);
                return IsFiniteNonNegative(marketValue) && marketValue > 0f;
            }
            catch (Exception ex)
            {
                Log.Warning(
                    "[MAP] MechanoidOvermindPricingService failed to evaluate market value for "
                    + spec.Def.defName
                    + ": "
                    + ex);
                marketValue = 0f;
                return false;
            }
            finally
            {
                if (temp != null && !temp.Destroyed)
                {
                    temp.Destroy(DestroyMode.Vanish);
                }
            }
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

        private static bool IsFiniteNonNegative(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
        }
    }
}
