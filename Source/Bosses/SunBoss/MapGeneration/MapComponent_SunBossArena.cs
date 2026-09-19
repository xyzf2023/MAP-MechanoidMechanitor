using System.Collections.Generic;
using Verse;

namespace MAP_MechanoidMechanitor
{
    // 场景记录，无 Tick、战斗状态或激活组件。后续逻辑可直接引用本地图的场地与建筑。
    public sealed class MapComponent_SunBossArena : MapComponent
    {
        public bool Generated;
        public CellRect FacilityBounds;
        public CellRect ArenaBounds;
        public Building? Core;
        public List<Building> Stabilizers = new List<Building>();

        public MapComponent_SunBossArena(Map map) : base(map) { }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref Generated, "sunFacilityGenerated");
            Scribe_Values.Look(ref FacilityBounds, "sunFacilityBounds");
            Scribe_Values.Look(ref ArenaBounds, "sunArenaBounds");
            Scribe_References.Look(ref Core, "sunArenaCore");
            Scribe_Collections.Look(ref Stabilizers, "sunArenaStabilizers", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && Stabilizers == null)
                Stabilizers = new List<Building>();
        }
    }
}
