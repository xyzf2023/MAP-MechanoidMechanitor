using MAP_MechanoidMechanitor;
using RimWorld;
using Verse;

namespace MMT
{
    // MMT-era entry point; delegates to MAP_MechanoidMechanitor node identity for all Shadow nodes.
    public static class OverseerlessMechanitorUtility
    {
        public static bool IsNode(Pawn? pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            if (MAPMechanitorNodeUtility.IsOverseerlessShadowNode(pawn))
            {
                return true;
            }

            return CompOverseerlessMechanitorNode.PawnHasNode(pawn);
        }

        public static void EnsureBasicTrackers(Pawn pawn)
        {
            if (pawn == null || !IsNode(pawn) || !ModsConfig.BiotechActive)
            {
                return;
            }

            if (pawn.relations == null)
            {
                pawn.relations = new Pawn_RelationsTracker(pawn);
            }

            bool createdMechanitor = false;
            if (pawn.mechanitor == null)
            {
                pawn.mechanitor = new Pawn_MechanitorTracker(pawn);
                createdMechanitor = true;
            }

            if (createdMechanitor
                || pawn.mechanitor.controlGroups == null
                || pawn.mechanitor.controlGroups.Count == 0)
            {
                pawn.mechanitor.Notify_PawnSpawned(true);
            }
        }

        public static void RefreshMechanitorStateIfNode(Pawn pawn)
        {
            if (pawn == null || !IsNode(pawn) || !ModsConfig.BiotechActive)
            {
                return;
            }

            EnsureBasicTrackers(pawn);

            if (pawn.mechanitor != null)
            {
                pawn.mechanitor.Notify_PawnSpawned(true);
            }
        }

        public static void ClearExternalOverseerIfNode(Pawn pawn)
        {
            if (pawn == null || !IsNode(pawn))
            {
                return;
            }

            Pawn overseer = pawn.GetOverseer();
            if (overseer == null || overseer.relations == null)
            {
                return;
            }

            overseer.relations.RemoveDirectRelation(PawnRelationDefOf.Overseer, pawn);
        }
    }
}
