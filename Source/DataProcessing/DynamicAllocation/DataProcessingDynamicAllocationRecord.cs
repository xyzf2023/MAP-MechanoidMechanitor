using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 轻量动态分配开关记录，仅保存 overseer 与是否开启动态分配。
    /// 由 GameComponent_DataProcessingAllocationRegistry 负责持久化与清理。
    /// </summary>
    public sealed class DataProcessingDynamicAllocationRecord : IExposable
    {
        public Pawn? overseer;
        public bool enabled;

        public DataProcessingDynamicAllocationRecord()
        {
        }

        public DataProcessingDynamicAllocationRecord(Pawn? overseer, bool enabled)
        {
            this.overseer = overseer;
            this.enabled = enabled;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref overseer, "overseer");
            Scribe_Values.Look(ref enabled, "enabled", false);
        }
    }
}
