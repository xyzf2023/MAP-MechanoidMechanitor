using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 每个存档一份经济账本。按 ThingDef 共享额度与采购压力；不随路线切换或接管重置。
    /// 报价不消费库存，投送事务单独预留与回滚。所有日数均为游戏日。
    /// </summary>
    public sealed class GameComponent_OvermindEconomy : GameComponent
    {
        private List<OvermindEconomyRecord> records = new List<OvermindEconomyRecord>();
        private readonly Dictionary<ThingDef, OvermindEconomyRecord> index =
            new Dictionary<ThingDef, OvermindEconomyRecord>();
        private OvermindEconomySettings applied = OvermindEconomySettings.Current.Copy();
        private bool hasDemand;
        private double globalPresence;
        private int globalTick = -1;
        private int lastScan = -1;

        public GameComponent_OvermindEconomy(Game game) { }
        public static GameComponent_OvermindEconomy? Current =>
            CurrentGameComponentCache<GameComponent_OvermindEconomy>.Get();
        public static bool Enabled => OvermindEconomySettings.Current.Enabled
            && (GameComponent_MechanoidMechanitorStoryState.IsPurgeDirectiveActive
                || GameComponent_CerebrexTakeoverState.IsActive);
        public const string SupplyError = "MAP_OvermindEconomy.InsufficientSupply";

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % 250 == 0
                && (Enabled || records.Count > 0)) Prepare();
        }

        private void PrepareQuote()
        {
            // 通讯窗口不会暂停游戏。报价每游戏刻至多扫描一次，成交再强制核对。
            Prepare(lastScan != Find.TickManager.TicksGame);
        }

        private static int Tier(ThingDef def)
        {
            OvermindEconomyExtension? extension = def.GetModExtension<OvermindEconomyExtension>();
            if (extension != null && extension.tier >= 0 && extension.tier <= 2) return extension.tier;
            MechanoidOvermindThingCategory category = MechanoidOvermindCatalogService.ClassifyThing(def);
            if (category == MechanoidOvermindThingCategory.Food) return 2;
            // 零部件等工业原料未必是 Stuff，复用原料类别作为补充。
            if (category == MechanoidOvermindThingCategory.Material
                || (def.thingCategories != null && def.thingCategories.Contains(ThingCategoryDefOf.Manufactured)))
                return 0;
            return 1;
        }

        private static double Capacity(ThingDef def, OvermindEconomySettings settings)
        {
            int tier = Tier(def);
            double baseValue = tier == 0 ? 10000d : tier == 2 ? 1000d : 3000d;
            OvermindEconomyExtension? extension = def.GetModExtension<OvermindEconomyExtension>();
            if (extension != null && extension.supplyValue > 0f
                && !float.IsInfinity(extension.supplyValue)) baseValue = extension.supplyValue;
            else if (MechanoidOvermindCatalogService.TryFindThingCatalogEntry(def, out var entry))
                baseValue = Math.Max(baseValue, entry.DefaultReferenceMarketValue);
            return baseValue * settings.Factor(tier == 0 ? OvermindEconomyOption.MaterialSupply
                : tier == 2 ? OvermindEconomyOption.ScarceSupply : OvermindEconomyOption.OrdinarySupply);
        }

        private static double BasePrice(ThingDef def, OvermindEconomySettings settings)
        {
            int tier = Tier(def);
            return settings.Factor(tier == 0 ? OvermindEconomyOption.MaterialPrice
                : tier == 2 ? OvermindEconomyOption.ScarcePrice : OvermindEconomyOption.OrdinaryPrice) / 5d;
        }

        private void AdvanceGlobal(int tick, OvermindEconomySettings settings)
        {
            if (!hasDemand && globalTick >= 0)
                globalPresence = Math.Max(0d, globalPresence
                    - Math.Max(0d, (double)tick - globalTick) / (60000d * settings.Days(OvermindEconomyOption.DemandDays)));
            globalTick = tick;
        }

        public void Prepare(bool forceDemandScan = false)
        {
            if (Find.TickManager == null || Find.WorldObjects == null) return;
            int tick = Find.TickManager.TicksGame;
            OvermindEconomySettings settings = OvermindEconomySettings.Current;
            if (!applied.SameAs(settings))
            {
                AdvanceGlobal(tick, applied);
                foreach (OvermindEconomyRecord record in records)
                    record.Advance(tick, applied, Capacity(record.Def, applied));
                applied = settings.Copy();
                foreach (OvermindEconomyRecord record in records)
                    record.Supply = Math.Min(record.Supply, Capacity(record.Def, applied));
                forceDemandScan = true;
            }
            AdvanceGlobal(tick, applied);
            if (!forceDemandScan && lastScan >= 0 && tick >= lastScan && tick - lastScan < 250) return;
            lastScan = tick;
            Dictionary<ThingDef, double> demands = new Dictionary<ThingDef, double>();
            List<WorldObject> objects = Find.WorldObjects.AllWorldObjects;
            bool any = false;
            for (int i = 0; i < objects.Count; i++)
            {
                if (!(objects[i] is MAPMechHiveNode node) || !node.Spawned
                    || !node.AllowsMaterialDelivery || node.DemandMaterialDef == null) continue;
                any = true;
                ThingDef def = node.DemandMaterialDef;
                if (!MechanoidOvermindPricingService.TryGetThingUnitMarketValue(
                    MechanoidOvermindThingSpec.CreateDefault(def), out float value)) continue;
                demands.TryGetValue(def, out double previous);
                demands[def] = previous + value * (double)node.CurrentDemandCount;
            }
            foreach (KeyValuePair<ThingDef, double> pair in demands) GetRecord(pair.Key);
            foreach (OvermindEconomyRecord record in records)
            {
                record.Advance(tick, applied, Capacity(record.Def, applied));
                demands.TryGetValue(record.Def, out double value);
                record.SetDemand(value);
            }
            hasDemand = any;
            if (any) globalPresence = 1d;
        }

        private OvermindEconomyRecord GetRecord(ThingDef def)
        {
            if (!index.TryGetValue(def, out OvermindEconomyRecord record))
            {
                record = new OvermindEconomyRecord
                {
                    Def = def, Supply = Capacity(def, applied), LastTick = Find.TickManager.TicksGame
                };
                index.Add(def, record);
                records.Add(record);
            }
            record.Advance(Find.TickManager.TicksGame, applied, Capacity(def, applied));
            return record;
        }

        /// <summary>同 Def 所有品质、材质先合计基础价值，消除换规格刷新库存及调整行顺序影响报价。</summary>
        public static bool TryValues(MechanoidOvermindOrder order, out Dictionary<ThingDef, double> values)
        {
            values = new Dictionary<ThingDef, double>();
            long stacks = 0L;
            foreach (MechanoidOvermindOrderLine_Thing line in order.ThingLines)
            {
                if (line?.Spec?.Def == null || line.Count <= 0 || line.Count > MechanoidOvermindOrder.ThingMaxCount
                    || !MechanoidOvermindCatalogService.TryFindThingCatalogEntry(line.Spec.Def, out _)
                    || !MechanoidOvermindPricingService.TryGetThingUnitMarketValue(line.Spec, out float value)) return false;
                int stackLimit = line.Spec.Def.Minifiable ? 1 : Math.Max(1, line.Spec.Def.stackLimit);
                stacks += ((long)line.Count + stackLimit - 1) / stackLimit;
                if (stacks > MechanoidOvermindOrder.MaxThingStacks) return false;
                values.TryGetValue(line.Spec.Def, out double oldValue);
                double next = oldValue + value * (double)line.Count;
                if (double.IsNaN(next) || double.IsInfinity(next)) return false;
                values[line.Spec.Def] = next;
            }
            return true;
        }

        private double StartingPrice(OvermindEconomyRecord record)
        {
            double capacity = Capacity(record.Def, applied);
            double benefit = Math.Min(applied.Factor(OvermindEconomyOption.BenefitCap),
                OvermindEconomyRecord.Sum(record.Benefits));
            double basePrice = BasePrice(record.Def, applied) * (1d - benefit);
            // 在折扣前反推下限，统一评级折扣应用后仍不少于交付回报。
            double retention = Math.Max(0.000001d, 1d - PurgeDirectiveRatingUtility.GetDiscountRate());
            double protection = applied.Factor(OvermindEconomyOption.Protection) / retention;
            double ordinaryFloor = MechHiveNodeDeliveryUtility.ExcessQuotaMultiplier * protection;
            double demandFloor = MechHiveNodeDeliveryUtility.DeliveryQuotaMultiplier * protection;
            double ordinaryUplift = Math.Max(0d, ordinaryFloor - basePrice) * globalPresence;
            double specificUplift = Math.Max(0d, demandFloor - Math.Max(basePrice, ordinaryFloor))
                * record.DemandPresence;
            double extra = Math.Min(applied.Factor(OvermindEconomyOption.DemandCap),
                (record.DemandValue + OvermindEconomyRecord.Sum(record.DemandTails)) / capacity
                    * applied.Factor(OvermindEconomyOption.DemandStrength));
            return basePrice + ordinaryUplift + specificUplift + demandFloor * extra;
        }

        private double PressureIntegral(OvermindEconomyRecord record, double value)
        {
            double slope = applied.Factor(OvermindEconomyOption.PressureStrength) / Capacity(record.Def, applied);
            double cap = applied.Factor(OvermindEconomyOption.PressureCap);
            if (slope <= 0d || cap <= 0d) return value;
            double start = Math.Min(cap, record.PurchasedValue * slope);
            double ramp = Math.Min(value, Math.Max(0d, (cap - start) / slope));
            return ramp * (1d + start) + 0.5d * slope * ramp * ramp
                + (value - ramp) * (1d + cap);
        }

        public bool TryCost(MechanoidOvermindOrder order, out int cost)
        {
            cost = 0;
            PrepareQuote();
            if (!TryValues(order, out var values)) return false;
            double total = 0d;
            foreach (KeyValuePair<ThingDef, double> pair in values)
            {
                OvermindEconomyRecord record = GetRecord(pair.Key);
                total += StartingPrice(record) * PressureIntegral(record, pair.Value);
            }
            if (double.IsNaN(total) || double.IsInfinity(total) || total > int.MaxValue) return false;
            cost = values.Count == 0 ? 0 : Math.Max(1, (int)Math.Ceiling(total));
            return true;
        }

        public bool TryEstimateLine(MechanoidOvermindThingSpec spec, int count,
            MechanoidOvermindOrder order, out int cost)
        {
            cost = 0;
            PrepareQuote();
            if (!MechanoidOvermindPricingService.TryGetThingUnitMarketValue(spec, out float unitValue)) return false;
            double lineValue = unitValue * (double)Math.Max(1, count);
            double totalValue = lineValue;
            foreach (var line in order.ThingLines)
                if (line.Spec.Def == spec.Def && !line.Spec.Equals(spec)
                    && MechanoidOvermindPricingService.TryGetThingUnitMarketValue(line.Spec, out float other))
                    totalValue += other * (double)line.Count;
            OvermindEconomyRecord record = GetRecord(spec.Def);
            // 同类规格按基础价值分摊合并报价；整数取整由最终订单统一完成。
            double estimated = StartingPrice(record) * PressureIntegral(record, totalValue)
                * lineValue / totalValue * Math.Max(0d, 1d - PurgeDirectiveRatingUtility.GetDiscountRate());
            if (double.IsNaN(estimated) || double.IsInfinity(estimated) || estimated > int.MaxValue) return false;
            cost = Math.Max(1, (int)Math.Ceiling(estimated));
            return true;
        }

        public static bool HasSupply(MechanoidOvermindOrder order)
        {
            if (!Enabled || order.ThingLines.Count == 0) return true;
            GameComponent_OvermindEconomy? economy = Current;
            if (economy == null || !TryValues(order, out var values)) return false;
            economy.PrepareQuote();
            foreach (KeyValuePair<ThingDef, double> pair in values)
                if (pair.Value > economy.GetRecord(pair.Key).Supply + 0.000001d) return false;
            return true;
        }

        public int AvailableCount(MechanoidOvermindThingSpec spec, MechanoidOvermindOrder order)
        {
            PrepareQuote();
            if (!MechanoidOvermindPricingService.TryGetThingUnitMarketValue(spec, out float unitValue)) return 0;
            double remaining = GetRecord(spec.Def).Supply;
            foreach (var line in order.ThingLines)
                if (line.Spec.Def == spec.Def && !line.Spec.Equals(spec)
                    && MechanoidOvermindPricingService.TryGetThingUnitMarketValue(line.Spec, out float otherValue))
                    remaining -= otherValue * (double)line.Count;
            int stackLimit = spec.Def.Minifiable ? 1 : Math.Max(1, spec.Def.stackLimit);
            return (int)Math.Min(Math.Min(MechanoidOvermindOrder.ThingMaxCount,
                    (long)stackLimit * MechanoidOvermindOrder.MaxThingStacks),
                Math.Max(0d, Math.Floor((remaining + 0.000001d) / unitValue)));
        }

        public string Describe(MechanoidOvermindThingSpec spec, MechanoidOvermindOrder order)
        {
            PrepareQuote();
            OvermindEconomyRecord record = GetRecord(spec.Def);
            double capacity = Capacity(spec.Def, applied);
            double daily = capacity / applied.Days(OvermindEconomyOption.RefillDays);
            MechanoidOvermindPricingService.TryGetThingUnitMarketValue(spec, out float unitValue);
            double days = Math.Max(0d, capacity - record.Supply) / daily;
            double pressure = Math.Min(applied.Factor(OvermindEconomyOption.PressureCap),
                record.PurchasedValue / capacity * applied.Factor(OvermindEconomyOption.PressureStrength));
            double benefit = Math.Min(applied.Factor(OvermindEconomyOption.BenefitCap), OvermindEconomyRecord.Sum(record.Benefits));
            string demand = record.DemandValue > 0d ? "MAP_OvermindEconomy.Demand.Active".Translate().ToString()
                : record.DemandPresence > 0d || record.DemandTails.Count > 0
                    ? "MAP_OvermindEconomy.Demand.Fading".Translate().ToString()
                    : globalPresence > 0d ? "MAP_OvermindEconomy.Demand.Global".Translate().ToString()
                    : "MAP_OvermindEconomy.Demand.None".Translate().ToString();
            return "MAP_OvermindEconomy.Stock".Translate(AvailableCount(spec, order),
                    (daily / Math.Max(0.000001d, unitValue)).ToString("0.#"), days.ToString("0.#"))
                + "\n" + "MAP_OvermindEconomy.Factors".Translate(
                    (BasePrice(spec.Def, applied) * 5d).ToString("0.##"),
                    (pressure * 100d).ToString("0.#"), (benefit * 100d).ToString("0.#"), demand);
        }

        /// <summary>在消费载荷及结束需求前捕获，按全局当前需求过滤整种物资（包括超额部分）。</summary>
        public Dictionary<ThingDef, double> CaptureBenefits(List<ActiveTransporterInfo> transporters)
        {
            Prepare(true);
            Dictionary<ThingDef, double> result = new Dictionary<ThingDef, double>();
            if (!Enabled) return result;
            foreach (var transporter in transporters)
            {
                foreach (Thing thing in transporter.innerContainer)
                {
                    if (thing is Pawn) continue;
                    Thing item = thing is MinifiedThing minified ? minified.InnerThing : thing;
                    if (item == null || !MechanoidOvermindCatalogService.TryFindThingCatalogEntry(item.def, out _)
                        || GetRecord(item.def).DemandValue > 0d) continue;
                    double value = OvermindEconomyRecord.NonNegative(thing.MarketValue * (double)thing.stackCount);
                    result.TryGetValue(item.def, out double previous);
                    result[item.def] = previous + value;
                }
            }
            return result;
        }

        public void ApplyBenefits(Dictionary<ThingDef, double> values)
        {
            if (!Enabled) return;
            Prepare();
            foreach (var pair in values)
            {
                OvermindEconomyRecord record = GetRecord(pair.Key);
                double remaining = Math.Max(0d, applied.Factor(OvermindEconomyOption.BenefitCap)
                    - OvermindEconomyRecord.Sum(record.Benefits));
                double amount = Math.Min(remaining, pair.Value / Capacity(pair.Key, applied)
                    * applied.Factor(OvermindEconomyOption.BenefitStrength));
                OvermindEconomyRecord.AddFade(record.Benefits, amount);
            }
        }

        public bool TryReserve(MechanoidOvermindOrder order, out OvermindEconomyReservation? reservation)
        {
            reservation = null;
            Prepare(true);
            if (!TryValues(order, out var values)) return false;
            foreach (var pair in values)
                if (GetRecord(pair.Key).Supply + 0.000001d < pair.Value) return false;
            reservation = new OvermindEconomyReservation(this, values);
            foreach (var pair in values)
            {
                OvermindEconomyRecord record = GetRecord(pair.Key);
                record.Supply = Math.Max(0d, record.Supply - pair.Value);
                record.PurchasedValue += pair.Value;
            }
            return true;
        }

        internal void Rollback(Dictionary<ThingDef, double> values)
        {
            foreach (var pair in values)
            {
                OvermindEconomyRecord record = GetRecord(pair.Key);
                record.Supply = Math.Min(Capacity(pair.Key, applied), record.Supply + pair.Value);
                record.PurchasedValue = Math.Max(0d, record.PurchasedValue - pair.Value);
            }
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref records, "MAP_overmindEconomyRecords", LookMode.Deep);
            Scribe_Deep.Look(ref applied, "MAP_overmindEconomyRates");
            Scribe_Values.Look(ref hasDemand, "MAP_overmindHasDemand", false);
            Scribe_Values.Look(ref globalPresence, "MAP_overmindDemandPresence", 0d);
            Scribe_Values.Look(ref globalTick, "MAP_overmindEconomyTick", -1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                applied = applied ?? OvermindEconomySettings.Current.Copy();
                records = records ?? new List<OvermindEconomyRecord>();
                index.Clear();
                for (int i = records.Count - 1; i >= 0; i--)
                {
                    OvermindEconomyRecord record = records[i];
                    if (record?.Def == null || index.ContainsKey(record.Def)) records.RemoveAt(i);
                    else index.Add(record.Def, record);
                }
                globalPresence = Math.Min(1d, OvermindEconomyRecord.NonNegative(globalPresence));
                lastScan = -1;
            }
        }
    }

    /// <summary>预留只在同步投送调用内存活；异常且空投已提交时仍 Commit，其余路径 Dispose 回滚。</summary>
    public sealed class OvermindEconomyReservation : IDisposable
    {
        private readonly GameComponent_OvermindEconomy economy;
        private readonly Dictionary<ThingDef, double> values;
        private bool finished;
        internal OvermindEconomyReservation(GameComponent_OvermindEconomy economy, Dictionary<ThingDef, double> values)
        { this.economy = economy; this.values = values; }
        public void Commit() { finished = true; }
        public void Dispose()
        {
            if (finished) return;
            finished = true;
            economy.Rollback(values);
        }
    }
}
