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

        /// <summary>联合军事行动是否已解锁（盟约等级达到解锁等级）。</summary>
        public bool JointOperationUnlocked;

        /// <summary>
        /// 联合军事行动的援军规模倍率，直接来自 Def，
        /// UI 显示与 QuestPart 实际部署读取的是同一套数据。
        /// </summary>
        public float JointOperationSupportPointsFactor;
    }

    /// <summary>
    /// 共生盟约效果展示的统一读取入口。UI 只通过 GetSnapshot 获取当前/下一等级效果。
    /// </summary>
    public static class SymbiosisCovenantEffectDisplayUtility
    {
        public static SymbiosisCovenantEffectSnapshot GetSnapshot(int level)
        {
            SymbiosisCovenantJointOperationDef? jointOperation =
                SymbiosisCovenantJointOperationDefOf.MAP_SymbiosisCovenant_JointOperationConfig;

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
                MemberAllianceLock = level >= 5,
                JointOperationUnlocked =
                    level >= SymbiosisCovenantJointOperationDef.MinimumCovenantLevel,
                // 未解锁时仍读取 L2 倍率供 UI 提示“解锁后是多少”，
                // 但 UI 必须先用 JointOperationUnlocked 判断是否展示。
                JointOperationSupportPointsFactor = jointOperation != null
                    ? jointOperation.GetSupportPointsFactorForLevel(level)
                    : SymbiosisCovenantJointOperationDef.LegacySupportPointsFactor
            };
            return snapshot;
        }
    }
}
