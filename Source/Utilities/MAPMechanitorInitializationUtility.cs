using System.Collections.Generic;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class MAPMechanitorInitializationUtility
    {
        public static void FinalizeNow(Pawn? pawn)
        {
            if (pawn == null || pawn.Destroyed || !ModsConfig.BiotechActive)
            {
                return;
            }

            if (!MAPMechanitorNodeUtility.IsMechanitorNodeController(pawn)
                && !MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
            {
                return;
            }

            if (pawn.Faction == null || !pawn.Faction.IsPlayerSafe())
            {
                return;
            }

            MAPMechanitorNodeLifecycleUtility.EnsureBasicTrackers(pawn);

            Pawn_MechanitorTracker? mechanitor = pawn.mechanitor;
            if (mechanitor == null)
            {
                return;
            }

            mechanitor.controlGroups ??= new List<MechanitorControlGroup>();
            mechanitor.Notify_PawnSpawned(true);
        }
    }
}
