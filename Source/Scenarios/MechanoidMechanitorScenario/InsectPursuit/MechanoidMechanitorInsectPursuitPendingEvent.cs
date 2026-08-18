namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 追杀系统当前挂起的专属事件类型。
    /// 整个追杀系统同时最多只允许一个挂起/进行中的事件（全局互斥）。
    /// </summary>
    public enum MechanoidMechanitorInsectPursuitPendingEvent : byte
    {
        None = 0,
        ExtraInfestation = 1,
        Hunt = 2
    }
}
