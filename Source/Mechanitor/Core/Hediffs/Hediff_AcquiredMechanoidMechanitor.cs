namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 后天机械族机械师身份健康状态。意识加成由集中计算 Utility 按科研与机械师数量提供。
    /// </summary>
    public class Hediff_AcquiredMechanoidMechanitor : Hediff_DynamicConsciousnessBonusBase
    {
        protected override float CalculateConsciousnessOffset()
        {
            return DynamicConsciousnessBonusCalculationUtility
                       .GetMechanitorIdentityConsciousnessOffset()
                   + ParallelThoughtArrayUtility.GetTotalActiveConsciousnessOffset(pawn);
        }

        protected override int GetStageVariantKey()
        {
            int originalKey = DynamicConsciousnessBonusCalculationUtility
                .GetConsciousnessStageVariantKey();
            int totalBoostPercent = ParallelThoughtArrayUtility.GetTotalActiveBoostPercent(pawn);
            return unchecked(originalKey * 397 ^ totalBoostPercent);
        }
    }
}
