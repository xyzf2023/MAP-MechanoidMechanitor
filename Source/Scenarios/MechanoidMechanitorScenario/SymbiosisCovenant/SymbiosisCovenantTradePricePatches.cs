using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    [HarmonyPatch(typeof(TradeUtility), nameof(TradeUtility.GetPricePlayerBuy))]
    public static class SymbiosisCovenant_TradePricePlayerBuy_Patch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        public static void Prefix(ref float priceGain_FactionBase)
        {
            if (SymbiosisCovenantLevelEffectUtility
                .TryGetCurrentTradePriceImprovement(out float improvement))
            {
                // 复用原版 TradePriceImprovement 语义，与聚落2%等Faction侧改善直接相加。
                priceGain_FactionBase += improvement;
            }
        }
    }

    [HarmonyPatch(typeof(TradeUtility), nameof(TradeUtility.GetPricePlayerSell))]
    public static class SymbiosisCovenant_TradePricePlayerSell_Patch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        public static void Prefix(ref float priceGain_FactionBase)
        {
            if (SymbiosisCovenantLevelEffectUtility
                .TryGetCurrentTradePriceImprovement(out float improvement))
            {
                // 与买入使用同一改善值：原版公式会自动表现为买入降价、卖出增价。
                priceGain_FactionBase += improvement;
            }
        }
    }

    [HarmonyPatch(typeof(Tradeable), nameof(Tradeable.GetPriceTooltip))]
    public static class SymbiosisCovenant_TradePriceTooltip_Patch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Tradeable __instance, ref string __result)
        {
            // 银币等货币Tradeable本身不经过普通商品买卖价格公式，避免显示误导性加成。
            if (__instance == null
                || __result.NullOrEmpty()
                || __instance.IsCurrency
                || !SymbiosisCovenantLevelEffectUtility
                    .TryGetCurrentTradePriceImprovement(out float improvement))
            {
                return;
            }

            __result += "\n"
                + "MAP_MechanoidMechanitor.Symbiosis.TradePriceImprovement"
                    .Translate(improvement.ToStringPercent());
        }
    }
}
