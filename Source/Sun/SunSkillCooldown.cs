using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>使用原版射击后摇的移动限制，并记录所属 Job，清理时不影响其他姿态。</summary>
    public sealed class Stance_SunSkillCooldown : Stance_Cooldown
    {
        private int ownerJobId = -1;

        public Stance_SunSkillCooldown() { }

        internal Stance_SunSkillCooldown(int ticks, Pawn pawn, Job job)
            : base(ticks, pawn, null)
        {
            ownerJobId = job.loadID;
            neverAimWeapon = true;
        }

        internal bool BelongsTo(Job job) => ownerJobId == job.loadID;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref ownerJobId, "sunSkillOwnerJobId", -1);
        }
    }

    internal static class SunSkillCooldown
    {
        internal static bool IsActive(Pawn? pawn) =>
            pawn?.stances?.curStance is Stance_SunSkillCooldown stance && stance.ticksLeft > 0;

        internal static bool Owns(Pawn pawn, Job job) =>
            pawn.stances?.curStance is Stance_SunSkillCooldown stance && stance.BelongsTo(job);

        internal static void Begin(Pawn pawn, Job job, int duration)
        {
            if (duration > 0)
                pawn.stances.SetStance(new Stance_SunSkillCooldown(duration, pawn, job));
        }

        // 原版姿态是固定动作阶段的唯一计时来源；姿态到期后自动恢复为可移动状态。
        internal static int Elapsed(Pawn pawn, Job job, int duration) =>
            pawn.stances?.curStance is Stance_SunSkillCooldown stance && stance.BelongsTo(job)
                ? UnityEngine.Mathf.Clamp(duration - stance.ticksLeft, 0, duration) : duration;

        internal static void Finish(Pawn pawn, Job job)
        {
            if (Owns(pawn, job)) pawn.stances.SetStance(new Stance_Mobile());
        }
    }
}
