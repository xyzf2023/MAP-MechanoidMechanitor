using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class JusticePawnUtility
    {
        public const string JusticeDefName = "MAP_Mech_Justice";

        public const string JusticeBossDefName = "MAP_Mech_JusticeBOSS";

        public static PawnKindDef? JusticePawnKind =>
            DefDatabase<PawnKindDef>.GetNamedSilentFail(JusticeDefName);

        public static PawnKindDef? JusticeBossPawnKind =>
            DefDatabase<PawnKindDef>.GetNamedSilentFail(JusticeBossDefName);

        public static bool IsJustice(Pawn? pawn) =>
            pawn?.def?.defName == JusticeDefName;

        public static bool IsPlayerJustice(Pawn? pawn) => IsJustice(pawn);

        public static bool IsBossJustice(Pawn? pawn) =>
            pawn?.def?.defName == JusticeBossDefName;

        public static bool IsBossJustice(ThingDef? def) =>
            def?.defName == JusticeBossDefName;

        public static bool IsBossJustice(PawnKindDef? kind) =>
            kind?.defName == JusticeBossDefName;

        public static bool IsAnyJusticeVariant(Pawn? pawn) =>
            IsPlayerJustice(pawn) || IsBossJustice(pawn);
    }
}