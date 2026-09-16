using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class Hediff_GravityDisorder : HediffWithComps
    {
        // 不保存第二份持续时间或原任务快照；读档/重新生成后从 Hediff 重建控制。
        internal bool NeedsEntryCleanup = true;
        internal bool UpdatingControl;

        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);
            GravityDisorderUtility.EnsureControl(this);
        }

        public override void Notify_Spawned()
        {
            base.Notify_Spawned();
            NeedsEntryCleanup = true;
        }

        public override void PostTick()
        {
            base.PostTick();
            GravityDisorderUtility.EnsureControl(this);
        }

        public override void PostRemoved()
        {
            base.PostRemoved();
            GravityDisorderUtility.ReleaseControl(pawn);
        }
    }
}
