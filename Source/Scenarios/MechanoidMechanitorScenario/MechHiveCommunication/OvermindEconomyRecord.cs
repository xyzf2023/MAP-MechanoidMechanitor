using System;
using System.Collections.Generic;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>独立衰减的贡献，新增交付不会延长旧优惠；Progress 不依赖当前设置天数。</summary>
    public sealed class OvermindEconomyFade : IExposable
    {
        public double Amount;
        public double Progress;
        public double Value => Amount * Math.Max(0d, 1d - Progress);
        public void ExposeData()
        {
            Scribe_Values.Look(ref Amount, "amount", 0d);
            Scribe_Values.Look(ref Progress, "progress", 0d);
            Amount = OvermindEconomyRecord.NonNegative(Amount);
            Progress = Math.Min(1d, OvermindEconomyRecord.NonNegative(Progress));
        }
    }

    public sealed class OvermindEconomyRecord : IExposable
    {
        public ThingDef Def = null!;
        public double Supply;
        public double PurchasedValue;
        public double DemandValue;
        public double DemandPresence;
        public int LastTick = -1;
        public List<OvermindEconomyFade> DemandTails = new List<OvermindEconomyFade>();
        public List<OvermindEconomyFade> Benefits = new List<OvermindEconomyFade>();

        public static double NonNegative(double value) =>
            double.IsNaN(value) || double.IsInfinity(value) ? 0d : Math.Max(0d, value);

        public static double Sum(List<OvermindEconomyFade> values)
        {
            double sum = 0d;
            for (int i = 0; i < values.Count; i++) sum += values[i].Value;
            return sum;
        }

        public static void AddFade(List<OvermindEconomyFade> values, double amount)
        {
            if (amount <= 0d) return;
            // 同一时刻的贡献合并，避免大批堆叠制造重复记录。
            if (values.Count > 0 && values[values.Count - 1].Progress == 0d)
                values[values.Count - 1].Amount += amount;
            else values.Add(new OvermindEconomyFade { Amount = amount });
        }

        private static void AdvanceFades(List<OvermindEconomyFade> values, double step)
        {
            for (int i = values.Count - 1; i >= 0; i--)
            {
                if (values[i] == null) { values.RemoveAt(i); continue; }
                values[i].Progress += step;
                if (values[i].Progress >= 1d) values.RemoveAt(i);
            }
        }

        public void Advance(int tick, OvermindEconomySettings settings, double capacity)
        {
            double days = LastTick < 0 ? 0d : Math.Max(0d, (double)tick - LastTick) / 60000d;
            LastTick = tick;
            Supply = Math.Min(capacity, NonNegative(Supply) + days * capacity / settings.Days(OvermindEconomyOption.RefillDays));
            PurchasedValue = NonNegative(PurchasedValue) * Math.Pow(0.5d, days / settings.Days(OvermindEconomyOption.PressureDays));
            if (PurchasedValue < 0.000001d) PurchasedValue = 0d;
            double demandStep = days / settings.Days(OvermindEconomyOption.DemandDays);
            if (DemandValue <= 0d) DemandPresence = Math.Max(0d, DemandPresence - demandStep);
            AdvanceFades(DemandTails, demandStep);
            AdvanceFades(Benefits, days / settings.Days(OvermindEconomyOption.BenefitDays));
        }

        public void SetDemand(double value)
        {
            if (value < DemandValue) AddFade(DemandTails, DemandValue - value);
            DemandValue = value;
            if (value > 0d) DemandPresence = 1d;
        }

        public void ExposeData()
        {
            Scribe_Defs.Look(ref Def, "def");
            Scribe_Values.Look(ref Supply, "supply", 0d);
            Scribe_Values.Look(ref PurchasedValue, "purchasedValue", 0d);
            Scribe_Values.Look(ref DemandValue, "demandValue", 0d);
            Scribe_Values.Look(ref DemandPresence, "demandPresence", 0d);
            Scribe_Values.Look(ref LastTick, "lastTick", -1);
            Scribe_Collections.Look(ref DemandTails, "demandTails", LookMode.Deep);
            Scribe_Collections.Look(ref Benefits, "benefits", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                DemandTails = DemandTails ?? new List<OvermindEconomyFade>();
                Benefits = Benefits ?? new List<OvermindEconomyFade>();
                DemandTails.RemoveAll(x => x == null);
                Benefits.RemoveAll(x => x == null);
                Supply = NonNegative(Supply);
                PurchasedValue = NonNegative(PurchasedValue);
                DemandValue = NonNegative(DemandValue);
                DemandPresence = Math.Min(1d, NonNegative(DemandPresence));
            }
        }
    }

    /// <summary>可选的商品档位及高价值物资配置入口，供 Def 补丁覆盖自动分类。</summary>
    public sealed class OvermindEconomyExtension : DefModExtension
    {
        // -1 自动；0 工业材料；1 普通；2 长期稀缺。
        public int tier = -1;
        public float supplyValue = -1f;
    }
}
