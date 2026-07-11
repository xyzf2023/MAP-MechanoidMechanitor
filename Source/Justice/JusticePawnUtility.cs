using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class JusticePawnUtility
    {
        public static PawnKindDef? JusticePawnKind =>
            DefDatabase<PawnKindDef>.GetNamedSilentFail("MAP_Mech_Justice");

        public static bool IsJustice(Pawn? pawn) =>
            pawn != null && pawn.kindDef == JusticePawnKind;
    }
}
