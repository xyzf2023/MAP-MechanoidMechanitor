using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class JusticePawnUtility
    {
        public const string JusticeDefName = "MAP_Mech_Justice";

        public static PawnKindDef? JusticePawnKind =>
            DefDatabase<PawnKindDef>.GetNamedSilentFail(JusticeDefName);

        public static bool IsJustice(Pawn? pawn) =>
            pawn?.def?.defName == JusticeDefName;
    }
}
