using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 允许非机械族机械师的机械族 Pawn（如恋人）作为第三方课堂教育系统的教师候选。
    /// 作为能力层 ClassroomTeaching 的真实能力来源；
    /// 同时幂等补齐实际教学所需基础设施（skills / relations / interactions）。
    /// 不直接引用 ProgressionEducation 程序集，不检查 PackageId、StudyGroup、SkillClassLogic 等，
    /// 具体课程资格完全交给 Education 自己判断。
    /// </summary>
    public class CompProperties_ClassroomTeachingUser : CompProperties
    {
        public CompProperties_ClassroomTeachingUser()
        {
            compClass = typeof(CompClassroomTeachingUser);
        }
    }

    public class CompClassroomTeachingUser : ThingComp
    {
        public override void PostPostMake()
        {
            base.PostPostMake();
            EnsureTeachingInfrastructure();
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            EnsureTeachingInfrastructure();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EnsureTeachingInfrastructure();
            }
        }

        /// <summary>
        /// 幂等确保教学所需基础设施：
        /// - skills tracker（仅创建，绝不强制设置技能等级，最终资格由 Education 原 SkillRecord 判断）；
        /// - relations / interactions（复用现有 ColonistLikeSocialTrackerUtility.EnsureTrackers）。
        /// </summary>
        private void EnsureTeachingInfrastructure()
        {
            if (parent is not Pawn pawn)
            {
                return;
            }

            pawn.skills ??= new Pawn_SkillTracker(pawn);

            // 复用项目已有的统一社交 Tracker 入口，禁止另写第二份 relations / interactions 创建逻辑。
            ColonistLikeSocialTrackerUtility.EnsureTrackers(pawn);
        }
    }
}
