using MAP_MechanoidMechanitor;
using RimWorld;
using Verse;

namespace MMT
{
    // MMT-era entry point; delegates to MAP_MechanoidMechanitor node identity.
    public static class OverseerlessMechanitorUtility
    {
        public static bool IsMAPMechanitorNodeController(Pawn? pawn)
        {
            if (MAPMechanitorNodeUtility.IsMechanitorNodeController(pawn))
            {
                return true;
            }

            return CompOverseerlessMechanitorNode.PawnHasNode(pawn);
        }

        public static bool IsNode(Pawn? pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            if (MAPMechanitorNodeUtility.UsesShadowControlPath(pawn)
                && pawn.RaceProps.IsMechanoid
                && pawn.Faction != null
                && pawn.Faction.IsPlayerSafe())
            {
                return true;
            }

            return CompOverseerlessMechanitorNode.PawnHasNode(pawn);
        }

        public static bool ShouldClearOwnExternalOverseer(Pawn? pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            if (CompOverseerlessMechanitorNode.PawnHasNode(pawn))
            {
                return true;
            }

            if (!MAPMechanitorNodeUtility.HasNode(pawn))
            {
                return false;
            }

            return !MAPMechanitorNodeUtility.RequiresExternalOverseer(pawn);
        }

        public static void EnsureBasicTrackers(Pawn pawn)
        {
            if (pawn == null || !ModsConfig.BiotechActive)
            {
                return;
            }

            if (!IsMAPMechanitorNodeController(pawn))
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
            if (pawn == null || !ModsConfig.BiotechActive)
            {
                return;
            }

            if (!IsMAPMechanitorNodeController(pawn) && !IsNode(pawn))
            {
                return;
            }

            EnsureBasicTrackers(pawn);
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

            overseer.relations.RemoveDirectRelation(PawnRelationDefOf.Overseer, pawn);
        }
    }
}
