using System.Collections.Generic;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>一次反重力会话只保存归属/队形；飞行阶段仍以飞行运行记录为准。</summary>
    public sealed class GroupFlightSession : IExposable
    {
        public Pawn? Provider;
        public Map? Map;
        public List<GroupFlightMember> Members = new();
        public bool Closing;
        public bool DeathHandled;
        public IntVec3 LastProviderPosition = IntVec3.Invalid;
        public int NextRangeCheckTick;

        public void ExposeData()
        {
            Scribe_References.Look(ref Provider, "provider");
            Scribe_References.Look(ref Map, "map");
            Scribe_Collections.Look(ref Members, "members", LookMode.Deep);
            Scribe_Values.Look(ref Closing, "closing");
            Scribe_Values.Look(ref DeathHandled, "deathHandled");
            Scribe_Values.Look(ref LastProviderPosition, "lastProviderPosition", IntVec3.Invalid);
            Scribe_Values.Look(ref NextRangeCheckTick, "nextRangeCheckTick");
            Members ??= new List<GroupFlightMember>();
        }
    }

    public sealed class GroupFlightMember : IExposable
    {
        public Pawn? Pawn;
        public IntVec3 Offset;
        public IntVec3 ExpectedPosition = IntVec3.Invalid;
        // 搜索范围固定在退出瞬间；重规划不得把圆心推向更远处。
        public IntVec3 SearchOrigin = IntVec3.Invalid;
        public IntVec3 SearchAnchor = IntVec3.Invalid;
        public Map? SearchMap;
        public bool AllowDownedLanding;
        internal GroupFlightSession? Session;

        public void ExposeData()
        {
            Scribe_References.Look(ref Pawn, "pawn");
            Scribe_Values.Look(ref Offset, "offset");
            Scribe_Values.Look(ref ExpectedPosition, "expectedPosition", IntVec3.Invalid);
            Scribe_Values.Look(ref SearchOrigin, "searchOrigin", IntVec3.Invalid);
            Scribe_Values.Look(ref SearchAnchor, "searchAnchor", IntVec3.Invalid);
            Scribe_References.Look(ref SearchMap, "searchMap");
            Scribe_Values.Look(ref AllowDownedLanding, "allowDownedLanding");
        }
    }
}
