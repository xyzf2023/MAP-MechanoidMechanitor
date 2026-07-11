using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(
        typeof(Pawn_MechanitorTracker),
        nameof(Pawn_MechanitorTracker.Notify_PawnSpawned))]
    public static class Pawn_MechanitorTracker_NotifyPawnSpawned_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn ___pawn, bool respawningAfterLoad)
        {
            if (!respawningAfterLoad)
            {
                return true;
            }

            if (___pawn == null
                || ___pawn.Destroyed
                || ___pawn.Dead
                || !___pawn.Spawned)
            {
                return true;
            }

            if (!LongEventHandler.AnyEventNowOrWaiting)
            {
                return true;
            }

            if (!MAPMechanitorNodeUtility.IsMechanitorNodeController(___pawn))
            {
                return true;
            }

            GameComponent_MechanoidMechanitorRegistry.QueuePostSpawnInitialization(___pawn);
            return false;
        }
    }
}
