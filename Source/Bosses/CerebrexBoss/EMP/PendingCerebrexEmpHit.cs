using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// EMP 冲击波延迟命中记录。目标在视觉冲击波传播到其距离时才受到效果。
    /// 该记录由主脑组件的 pendingEmpHits 列表以 LookMode.Deep 存档。
    /// </summary>
    public sealed class PendingCerebrexEmpHit : IExposable
    {
        public Thing? target;

        public int impactTick;

        public PendingCerebrexEmpHit()
        {
        }

        public PendingCerebrexEmpHit(Thing target, int impactTick)
        {
            this.target = target;
            this.impactTick = impactTick;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref target, "target");
            Scribe_Values.Look(ref impactTick, "impactTick", 0);
        }
    }
}
