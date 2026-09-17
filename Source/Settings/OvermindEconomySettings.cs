using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public enum OvermindEconomyOption
    {
        RefillDays,
        PressureDays,
        DemandDays,
        BenefitDays,
        MaterialPrice,
        OrdinaryPrice,
        ScarcePrice,
        MaterialSupply,
        OrdinarySupply,
        ScarceSupply,
        Protection,
        DemandStrength,
        DemandCap,
        PressureStrength,
        PressureCap,
        BenefitStrength,
        BenefitCap,
    }

    /// <summary>全局设置与存档中的速率快照共用；百分比用整数保存，避免滑条浮点漂移。</summary>
    public sealed class OvermindEconomySettings : IExposable
    {
        public bool Enabled = true;
        public int[] Values = Defaults();
        public static readonly int[] Minimum = { 1, 1, 1, 1, 10, 10, 10, 10, 10, 10, 100, 0, 0, 0, 0, 0, 0 };
        public static readonly int[] Maximum = { 10, 10, 10, 10, 1000, 1000, 1000, 1000, 1000, 1000, 500, 500, 500, 500, 500, 90, 90 };
        private static int[] Defaults() => new[] { 5, 2, 3, 3, 100, 100, 300, 100, 100, 100, 105, 25, 100, 100, 200, 20, 20 };
        public static readonly OvermindEconomySettings Fallback = new OvermindEconomySettings();
        public static OvermindEconomySettings Current => MAPMechanitorMod.Settings?.overmindEconomy ?? Fallback;

        public int Days(OvermindEconomyOption option) => Values[(int)option];
        public double Factor(OvermindEconomyOption option) => Values[(int)option] / 100d;
        public OvermindEconomySettings Copy() =>
            new OvermindEconomySettings { Enabled = Enabled, Values = (int[])Values.Clone() };

        public bool SameAs(OvermindEconomySettings other)
        {
            if (Enabled != other.Enabled) return false;
            for (int i = 0; i < Values.Length; i++)
                if (Values[i] != other.Values[i]) return false;
            return true;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref Enabled, "enabled", true);
            int[] defaults = Defaults();
            for (int i = 0; i < defaults.Length; i++)
            {
                Scribe_Values.Look(ref Values[i], ((OvermindEconomyOption)i).ToString(), defaults[i]);
                Values[i] = Mathf.Clamp(Values[i], Minimum[i], Maximum[i]);
            }
        }

        public static void Draw(Listing_Standard listing, OvermindEconomySettings settings)
        {
            listing.GapLine();
            listing.Label("MAP_OvermindEconomy.Settings.Title".Translate());
            // 修改前按旧快照结算，修改后再应用新速率。
            GameComponent_OvermindEconomy.Current?.Prepare();
            listing.CheckboxLabeled("MAP_OvermindEconomy.Settings.Enabled".Translate(),
                ref settings.Enabled, "MAP_OvermindEconomy.Settings.Enabled.Desc".Translate());
            bool oldEnabled = GUI.enabled;
            GUI.enabled = oldEnabled && settings.Enabled;
            for (int i = 0; i < settings.Values.Length; i++)
            {
                string? section = i == 0 ? "Time" : i == 4 ? "Supply"
                    : i == 10 ? "Construction" : i == 13 ? "Purchase" : i == 15 ? "Delivery" : null;
                if (section != null)
                {
                    listing.Gap();
                    listing.Label(("MAP_OvermindEconomy.Settings.Section." + section).Translate());
                }
                string key = "MAP_OvermindEconomy.Settings." + (OvermindEconomyOption)i;
                string value = i < 4
                    ? "MAP_OvermindEconomy.Days".Translate(settings.Values[i]).ToString()
                    : i <= 10 ? "×" + (settings.Values[i] / 100f).ToString("0.##")
                    : settings.Values[i] + "%";
                Rect labelRect = listing.Label(key.Translate() + "：" + value);
                TooltipHandler.TipRegion(labelRect, (key + ".Desc").Translate());
                float raw = listing.Slider(settings.Values[i], Minimum[i], Maximum[i]);
                settings.Values[i] = Mathf.Clamp(Mathf.RoundToInt(raw), Minimum[i], Maximum[i]);
            }
            GUI.enabled = oldEnabled;
            if (listing.ButtonText("MAP_OvermindEconomy.Settings.Reset".Translate()))
            {
                settings.Enabled = true;
                settings.Values = Defaults();
            }
            GameComponent_OvermindEconomy.Current?.Prepare();
        }
    }
}
