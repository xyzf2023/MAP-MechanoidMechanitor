using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechanoidMechanitorPurgeTradeBlacklistDef : Def
    {
        public List<ThingDef> thingDefs = new List<ThingDef>();

        public List<PawnKindDef> mechPawnKinds = new List<PawnKindDef>();
    }
}
