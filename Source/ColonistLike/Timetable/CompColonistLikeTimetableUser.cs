using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 允许非机械族机械师的机械族 Pawn（如恋人）拥有并使用原版 Pawn_TimetableTracker 数据层。
    /// 作为能力层 ColonistLikeTimetable 的真实能力来源；
    /// 同时承担该 Pawn 的 timetable 生命周期（PostPostMake / PostSpawnSetup / PostLoadInit）。
    /// 界面显示独立配置，不影响后台作息能力与生命周期。
    /// </summary>
    public class CompProperties_ColonistLikeTimetableUser : CompProperties
    {
        // 仅控制组件来源的 Schedule UI 追加；正式机械族机械师仍由身份注册表显示。
        public bool showInSchedule = true;

        public CompProperties_ColonistLikeTimetableUser()
        {
            compClass = typeof(CompColonistLikeTimetableUser);
        }
    }

    public class CompColonistLikeTimetableUser : ThingComp
    {
        public bool ShowInSchedule =>
            (props as CompProperties_ColonistLikeTimetableUser)?.showInSchedule ?? true;

        public override void PostPostMake()
        {
            base.PostPostMake();
            EnsureTimetableState();
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            EnsureTimetableState();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EnsureTimetableState();
            }
        }

        private void EnsureTimetableState()
        {
            if (parent is Pawn pawn)
            {
                ColonistLikeMechTimetableUtility.EnsureTimetableState(pawn);
            }
        }
    }
}
