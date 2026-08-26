using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 一轮带宽干扰中的单个目标记录。保存目标与其在当时所属的监管者，
    /// 以便整轮结束时只对本技能造成的状态进行统一清理与通知。
    /// </summary>
    public sealed class CerebrexBandwidthTargetRecord : IExposable
    {
        public Pawn? target;

        public Pawn? originalOverseer;

        public CerebrexBandwidthTargetRecord()
        {
        }

        public CerebrexBandwidthTargetRecord(Pawn? target, Pawn? originalOverseer)
        {
            this.target = target;
            this.originalOverseer = originalOverseer;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref target, "target");
            Scribe_References.Look(ref originalOverseer, "originalOverseer");
        }
    }

    /// <summary>
    /// 一轮带宽干扰中因超载而狂暴的单位记录。保存该单位当时所属的监管者，
    /// 以便整轮结束时只恢复由本技能隐藏标记保护的狂暴状态。
    /// </summary>
    public sealed class CerebrexBandwidthBerserkRecord : IExposable
    {
        public Pawn? pawn;

        public Pawn? originalOverseer;

        public CerebrexBandwidthBerserkRecord()
        {
        }

        public CerebrexBandwidthBerserkRecord(Pawn? pawn, Pawn? originalOverseer)
        {
            this.pawn = pawn;
            this.originalOverseer = originalOverseer;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_References.Look(ref originalOverseer, "originalOverseer");
        }
    }
}
