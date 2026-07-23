using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    [DefOf]
    public static class MechHiveNodeDefOf
    {
        // 世界对象与地点部件（本 MOD 自定义）。
        public static WorldObjectDef MAP_MechHiveNode = null!;

        public static SitePartDef MAP_MechHiveNode_Building = null!;

        public static SitePartDef MAP_MechHiveNode_Completed = null!;

        // 机械族专用炮塔（Core，始终存在）。
        public static ThingDef Turret_AutoChargeBlaster = null!;

        public static ThingDef Turret_AutoInferno = null!;

        // 高低角护盾（Royalty，缺失时保持为 null，不得直接访问）。
        [MayRequire("Ludeon.RimWorld.Royalty")]
        public static ThingDef? ShieldGeneratorBullets = null;

        [MayRequire("Ludeon.RimWorld.Royalty")]
        public static ThingDef? ShieldGeneratorMortar = null;

        static MechHiveNodeDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(MechHiveNodeDefOf));
        }
    }
}
