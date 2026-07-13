namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 先天机械族机械师身份健康状态。意识加成由集中计算 Utility 按科研与机械师数量提供。
    /// </summary>
    public class Hediff_NativeMechanoidMechanitor : Hediff_DynamicConsciousnessBonusBase
    {
        protected override float CalculateConsciousnessOffset()
        {
            return DynamicConsciousnessBonusCalculationUtility
                .GetMechanitorIdentityConsciousnessOffset();
        }

        protected override int GetStageVariantKey()
        {
            return DynamicConsciousnessBonusCalculationUtility
                .GetConsciousnessStageVariantKey();
        }
    }
}
