namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 动态分配运行时的目标状态（不是特化枚举）。
    /// 固定判定优先级：CloseMelee &gt; DraftedMelee &gt; DraftedRanged &gt; Working &gt; UndraftedFallback &gt; Default。
    /// </summary>
    public enum DataProcessingDynamicState
    {
        Idle,
        Working,
        DraftedRanged,
        DraftedMelee,
        CloseMelee
    }
}
