using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechanoidMechanitorPurgeTradePriceOverrideEntry
    {
        public PawnKindDef? mechPawnKind;

        public int price;
    }

    public sealed class MechanoidMechanitorPurgeTradePriceOverrideDef : Def
    {
        public List<MechanoidMechanitorPurgeTradePriceOverrideEntry> mechPriceOverrides =
            new List<MechanoidMechanitorPurgeTradePriceOverrideEntry>();
    }
}
