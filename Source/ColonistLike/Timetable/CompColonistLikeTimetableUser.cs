using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 允许非机械族机械师的机械族 Pawn（如恋人）拥有并使用原版 Pawn_TimetableTracker 数据层。
    /// 作为能力层 ColonistLikeTimetable 的真实能力来源；
    /// 同时承担该 Pawn 的 timetable 生命周期（PostPostMake / PostSpawnSetup / PostLoadInit）。
    /// 不增加无实际用途的复杂设置字段，第一版保持 Marker + lifecycle。
    /// </summary>
    public class CompProperties_ColonistLikeTimetableUser : CompProperties
    {
        public CompProperties_ColonistLikeTimetableUser()
        {
            compClass = typeof(CompColonistLikeTimetableUser);
        }
    }

    public class CompColonistLikeTimetableUser : ThingComp
    {
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
