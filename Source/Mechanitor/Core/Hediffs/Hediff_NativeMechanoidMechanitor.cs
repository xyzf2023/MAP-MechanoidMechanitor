namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 先天机械族机械师身份健康状态。意识加成由动态阶段提供，便于日后按条件调整。
    /// </summary>
    public class Hediff_NativeMechanoidMechanitor : Hediff_DynamicConsciousnessBonusBase
    {
        protected override float CalculateConsciousnessOffset()
        {
            return 1f;
        }
    }
}
