namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 后天机械族机械师身份健康状态。意识加成由动态阶段提供，便于日后按条件调整。
    /// </summary>
    public class Hediff_AcquiredMechanoidMechanitor : Hediff_DynamicConsciousnessBonusBase
    {
        protected override float CalculateConsciousnessOffset()
        {
            return 1f;
        }
    }
}
