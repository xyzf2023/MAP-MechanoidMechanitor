namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 本次合体实例的事务状态。Starting/Ending 分别表示合体与解除正在执行，
    /// PendingRecovery 表示合体已结束效果但真实源 Pawn 仍在等待安全容器恢复。
    /// </summary>
    public enum MechFusionSessionState
    {
        Starting,
        Active,
        Ending,
        PendingRecovery
    }
}
