namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 机械意识宿主健康状态。意识加成由集中计算 Utility 按科研与机械师数量提供。
    /// </summary>
    public class Hediff_MechanicalConsciousness : Hediff_DynamicConsciousnessBonusBase
    {
        protected override float CalculateConsciousnessOffset()
        {
            return DynamicConsciousnessBonusCalculationUtility
                .GetMechanicalConsciousnessOffset();
        }

        protected override int GetStageVariantKey()
        {
            return DynamicConsciousnessBonusCalculationUtility
                .GetConsciousnessStageVariantKey();
        }
    }
}
