using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class MAPOverseerlessNodeUtility
    {
        public static bool IsOverseerlessNodeSubject(Pawn? pawn)
        {
            return pawn != null
                && ModsConfig.BiotechActive
                && MAPMechanitorNodeUtility.IsMechanitorNodeController(pawn)
                && !MAPMechanitorNodeUtility.RequiresExternalOverseer(pawn);
        }

        public static bool ShouldClearOwnExternalOverseer(Pawn? pawn)
        {
            return IsOverseerlessNodeSubject(pawn);
        }

        public static void ClearExternalOverseerIfNode(Pawn pawn)
        {
            if (!ShouldClearOwnExternalOverseer(pawn))
            {
                return;
            }

            Pawn overseer = pawn.GetOverseer();
            if (overseer == null || overseer.relations == null)
            {
                return;
            }

            if (pawn.mechanitor?.ControlledPawns.Contains(overseer) == true)
            {
                return;
            }

            overseer.relations.RemoveDirectRelation(PawnRelationDefOf.Overseer, pawn);
        }
    }
}
