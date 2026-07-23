namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 机械巢节点的生命周期阶段。同一个世界对象在两种阶段间原地切换，不重建。
    /// </summary>
    public enum MechanoidMechanitorMechHiveNodePhase : byte
    {
        /// <summary>建设中（机械巢节点（建设中））。</summary>
        Building = 0,

        /// <summary>建设完成（机械巢节点）。</summary>
        Completed = 1
    }
}
