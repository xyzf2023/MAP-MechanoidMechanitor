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

            if (pawn.mechanitor == null)
            {
                pawn.mechanitor = new Pawn_MechanitorTracker(pawn);
            }

            if (pawn.mechanitor.controlGroups == null)
            {
                pawn.mechanitor.controlGroups = new List<MechanitorControlGroup>();
            }
        }
    }
}
