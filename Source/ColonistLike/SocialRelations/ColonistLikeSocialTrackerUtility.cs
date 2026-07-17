using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 殖民者式社交面板所需 Tracker 的统一初始化入口。
    /// 仅补齐 relations / interactions，不创建其他 Tracker。
    /// </summary>
    public static class ColonistLikeSocialTrackerUtility
    {
        public static void EnsureTrackers(Pawn? pawn)
        {
            if (pawn == null)
            {
                return;
            }

            pawn.relations ??= new Pawn_RelationsTracker(pawn);
            pawn.interactions ??= new Pawn_InteractionsTracker(pawn);
        }
    }
}
