namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 统一退出原因。数值越小优先级越高；同一 Tick 出现多个退出原因时，
    /// 由最先进入统一解除流程的原因主导最终结算，其余回调只发现解除已开始。
    /// </summary>
    public enum MechFusionExitReason
    {
        StabilityDepleted,
        FlightCrash,
        HumanDeathOrDowned,
        EnergyDepleted,
        ApparelLost,
        Manual,
        LoadRepair
    }

    public static class MechFusionExitReasonUtility
    {
        public static int GetPriority(this MechFusionExitReason reason)
        {
            return (int)reason;
        }
    }
}
