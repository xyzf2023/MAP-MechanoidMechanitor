using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 共生盟约等级效果的统一只读入口。
    /// 后续商队、援军等等级奖励也应优先复用这里，避免在各补丁中重复等级表。
    /// </summary>
    public static class SymbiosisCovenantLevelEffectUtility
    {
        public static float GetTradePriceImprovementForLevel(int covenantLevel)
        {
            switch (covenantLevel)
            {
                case 2:
                    return 0.05f;
                case 3:
                    return 0.10f;
                case 4:
                    return 0.15f;
                case 5:
                    return 0.20f;
                default:
                    return 0f;
            }
        }

        public static SymbiosisCovenantDelegationLevelSettings? GetTradeDelegationSettingsForLevel(
            int covenantLevel)
        {
            SymbiosisCovenantTradeDelegationDef? config =
                SymbiosisCovenantTradeDelegationDefOf.MAP_SymbiosisCovenant_TradeDelegationConfig;
            return config?.GetLevelSettings(covenantLevel);
        }

        public static SymbiosisCovenantMilitaryAidLevelSettings? GetMilitaryAidSettingsForLevel(
            int covenantLevel)
        {
            SymbiosisCovenantMilitaryAidDef? config =
                SymbiosisCovenantMilitaryAidDefOf.MAP_SymbiosisCovenant_MilitaryAidConfig;
            return config?.GetLevelSettings(covenantLevel);
        }

        /// <summary>
        /// 获取当前实际贸易会话可享受的共生盟约交易价格改善。
        /// 此方法必须保持纯读取：不能同步状态、创建记录或修改任何外交数据。
        /// </summary>
        public static bool TryGetCurrentTradePriceImprovement(out float improvement)
        {
            improvement = 0f;

            if (!GameComponent_SymbiosisCovenantState.IsActive
                || !TradeSession.Active
                || TradeSession.giftMode
                || TradeSession.TradeCurrency != TradeCurrency.Silver)
            {
                return false;
            }

            ITrader? trader = TradeSession.trader;
            Faction? traderFaction = trader?.Faction;
            if (traderFaction == null)
            {
                return false;
            }

            GameComponent_SymbiosisCovenantState? state =
                GameComponent_SymbiosisCovenantState.CurrentComponent;
            if (state == null)
            {
                return false;
            }

            SymbiosisCovenantFactionRecord? record = state.GetRecord(traderFaction);
            if (record?.CovenantMember != true)
            {
                return false;
            }

            improvement = GetTradePriceImprovementForLevel(state.CovenantLevel);
            return improvement > 0f;
        }
    }
}
