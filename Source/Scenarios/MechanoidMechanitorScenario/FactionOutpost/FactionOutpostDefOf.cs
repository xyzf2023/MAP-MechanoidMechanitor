using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    [DefOf]
    public static class FactionOutpostDefOf
    {
        public static WorldObjectDef MAP_FactionOutpost = null!;

        public static SitePartDef MAP_FactionOutpost_Building = null!;

        public static SitePartDef MAP_FactionOutpost_Completed = null!;

        // 分档布局 SitePartDef：由创建前哨时保存的完成态守军预算选择，Baseline 复用上方两个旧 Def。
        public static SitePartDef MAP_FactionOutpost_Building_Expanded = null!;
        public static SitePartDef MAP_FactionOutpost_Building_Large = null!;
        public static SitePartDef MAP_FactionOutpost_Building_Fortress = null!;

        public static SitePartDef MAP_FactionOutpost_Completed_Expanded = null!;
        public static SitePartDef MAP_FactionOutpost_Completed_Large = null!;
        public static SitePartDef MAP_FactionOutpost_Completed_Fortress = null!;

        static FactionOutpostDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(FactionOutpostDefOf));
        }
    }
}
