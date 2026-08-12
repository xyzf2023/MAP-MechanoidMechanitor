using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 共生盟约“盟约效果”页的只读数据快照。
    /// 所有数值均来自真实机制配置（Def / LevelEffectUtility），UI 不得自行硬编码。
    /// </summary>
    public sealed class SymbiosisCovenantEffectSnapshot
    {
        public int Level;

        public float TradePriceImprovement;

        public SymbiosisCovenantDelegationLevelSettings? TradeDelegation;

        public SymbiosisCovenantMilitaryAidLevelSettings? MilitaryAid;

        public bool MemberNeutralFloor;
        public bool MemberAllianceLock;
    }

    /// <summary>
    /// 共生盟约效果展示的统一读取入口。UI 只通过 GetSnapshot 获取当前/下一等级效果。
    /// </summary>
    public static class SymbiosisCovenantEffectDisplayUtility
    {
        public static SymbiosisCovenantEffectSnapshot GetSnapshot(int level)
        {
            SymbiosisCovenantEffectSnapshot snapshot = new SymbiosisCovenantEffectSnapshot
            {
                Level = level,
                TradePriceImprovement =
                    SymbiosisCovenantLevelEffectUtility.GetTradePriceImprovementForLevel(level),
                TradeDelegation =
                    SymbiosisCovenantLevelEffectUtility.GetTradeDelegationSettingsForLevel(level),
                MilitaryAid =
                    SymbiosisCovenantLevelEffectUtility.GetMilitaryAidSettingsForLevel(level),
                // L1~L4：成员关系最低维持中立；L5：持续盟友。L0 两者均为 false。
                MemberNeutralFloor = level >= 1,
                MemberAllianceLock = level >= 5
            };
            return snapshot;
        }
    }
}
