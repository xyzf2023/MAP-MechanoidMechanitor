using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 数据处理分配窗口的顶置状态，与分配档位记录相互独立，
    /// 以便分配降到 0% 后仍能保留顶置。
    /// </summary>
    public sealed class DataProcessingAllocationPinRecord : IExposable
    {
        public Pawn? overseer;
        public Pawn? target;
        public int pinOrder;

        public DataProcessingAllocationPinRecord()
        {
        }

        public DataProcessingAllocationPinRecord(Pawn overseer, Pawn target, int pinOrder)
        {
            this.overseer = overseer;
            this.target = target;
            this.pinOrder = pinOrder;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref overseer, "overseer");
            Scribe_References.Look(ref target, "target");
            Scribe_Values.Look(ref pinOrder, "pinOrder", 0);
        }
    }
}
