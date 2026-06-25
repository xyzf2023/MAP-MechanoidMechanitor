using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class MAPMechanitorNodeLifecycleUtility
    {
        public static void EnsureBasicTrackers(Pawn pawn)
        {
            if (pawn == null || !ModsConfig.BiotechActive)
            {
                return;
            }

            if (!MAPMechanitorNodeUtility.IsMechanitorNodeController(pawn))
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

            Pawn_MechanitorTracker mechanitor = pawn.mechanitor;
            bool controlGroupsWereNull = mechanitor.controlGroups == null;
            List<MechanitorControlGroup> controlGroups = mechanitor.controlGroups
                ?? new List<MechanitorControlGroup>();
            if (controlGroupsWereNull)
            {
                mechanitor.controlGroups = controlGroups;
            }

            if (createdMechanitor
                || controlGroupsWereNull
                || controlGroups.Count == 0)
            {
                mechanitor.Notify_PawnSpawned(true);
            }
        }
    }
}
