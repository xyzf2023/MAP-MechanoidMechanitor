namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 受统一能力层管理的能力授予规则。
    /// </summary>
    public enum ManagedAbilityGrantPolicy
    {
        ResearchConsciousness,
        Capability,
        ResearchAndCapability
    }

    /// <summary>
    /// 机械意识迁移时能力应采用的处理方式。
    /// </summary>
    public enum ManagedAbilityTransferPolicy
    {
        TransferWithConsciousness,
        ReevaluateTargetChassis,
        DoNotTransfer
    }
}
