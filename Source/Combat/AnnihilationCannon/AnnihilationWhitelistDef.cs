using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>只排除湮灭炮强制抹除；不免疫正常爆炸伤害。</summary>
    public sealed class AnnihilationWhitelistDef : Def
    {
        public List<ThingDef> thingDefs = new List<ThingDef>();
        public List<PawnKindDef> pawnKinds = new List<PawnKindDef>();
    }
}
