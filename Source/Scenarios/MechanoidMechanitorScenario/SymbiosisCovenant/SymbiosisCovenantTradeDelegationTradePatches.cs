using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public static class SymbiosisCovenantTradeDelegationTradeUtility
    {
        public static bool IsDelegationExtraStockAvailable(ThingDef? def)
        {
            if (def == null || !TradeSession.Active || !(TradeSession.trader is Pawn trader))
            {
                return false;
            }

            Lord? lord = trader.GetLord();
            if (!(lord?.LordJob is LordJob_SymbiosisCovenantTradeDelegation delegation)
                || !delegation.IsExtraTradeThingDef(def))
            {
                return false;
            }

            return trader.Goods.Any(thing => thing.def == def);
        }

        public static bool LeadTraderKindNormallyTrades(ThingDef? def)
        {
            return def != null
                && TradeSession.Active
                && TradeSession.trader?.TraderKind != null
                && TradeSession.trader.TraderKind.WillTrade(def);
        }
    }

    [HarmonyPatch(typeof(Tradeable), nameof(Tradeable.TraderWillTrade), MethodType.Getter)]
    public static class SymbiosisCovenantDelegationTraderWillTradePatch
    {
        public static void Postfix(Tradeable __instance, ref bool __result)
        {
            if (__result || __instance == null)
            {
                return;
            }

            if (SymbiosisCovenantTradeDelegationTradeUtility
                .IsDelegationExtraStockAvailable(__instance.ThingDef))
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch(typeof(Tradeable), nameof(Tradeable.GetMinimumToTransfer))]
    public static class SymbiosisCovenantDelegationMinimumTransferPatch
    {
        public static void Postfix(Tradeable __instance, ref int __result)
        {
            if (__instance == null || __result >= 0)
            {
                return;
            }

            ThingDef? def = __instance.ThingDef;
            if (SymbiosisCovenantTradeDelegationTradeUtility.IsDelegationExtraStockAvailable(def)
                && !SymbiosisCovenantTradeDelegationTradeUtility.LeadTraderKindNormallyTrades(def))
            {
                __result = 0;
            }
        }
    }
}
