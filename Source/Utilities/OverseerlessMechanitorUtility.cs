using RimWorld;
using Verse;

namespace MMT
{
    public static class OverseerlessMechanitorUtility
    {
        public static bool IsNode(Pawn pawn)
        {
            return CompOverseerlessMechanitorNode.PawnHasNode(pawn);
        }

        public static void EnsureBasicTrackers(Pawn pawn)
        {
            if (!IsNode(pawn) || !ModsConfig.BiotechActive)
            {
                return;
            }

            if (pawn.relations == null)
            {
                pawn.relations = new Pawn_RelationsTracker(pawn);
            }

            if (pawn.mechanitor == null)
            {
                pawn.mechanitor = new Pawn_MechanitorTracker(pawn);
            }
        }

        public static void ClearExternalOverseerIfNode(Pawn pawn)
        {
            if (!IsNode(pawn))
            {
                return;
            }

            Pawn overseer = pawn.GetOverseer();
            if (overseer != null)
            {
                overseer.relations.RemoveDirectRelation(PawnRelationDefOf.Overseer, pawn);
            }
        }
    }
}
