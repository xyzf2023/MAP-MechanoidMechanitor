using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// EMP 冲击波延迟命中记录。目标在视觉冲击波传播到其距离时才受到效果。
    /// 每条记录同时保存本次 EMP 释放时锁定的半径与基础持续时间，避免战斗中修改设置
    /// 反向改变已经释放的冲击波。该记录由主脑组件的 pendingEmpHits 列表以 LookMode.Deep 存档。
    /// </summary>
    public sealed class PendingCerebrexEmpHit : IExposable
    {
        public Thing? target;

        public int impactTick;

        public float radiusAtRelease;

        public int baseDurationTicksAtRelease;

        public PendingCerebrexEmpHit()
        {
        }

        public PendingCerebrexEmpHit(
            Thing target,
            int impactTick,
            float radiusAtRelease,
            int baseDurationTicksAtRelease)
        {
            this.target = target;
            this.impactTick = impactTick;
            this.radiusAtRelease = radiusAtRelease;
            this.baseDurationTicksAtRelease = baseDurationTicksAtRelease;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref target, "target");
            Scribe_Values.Look(ref impactTick, "impactTick", 0);
            Scribe_Values.Look(ref radiusAtRelease, "radiusAtRelease", 0f);
            Scribe_Values.Look(ref baseDurationTicksAtRelease, "baseDurationTicksAtRelease", 0);
        }
    }
}
