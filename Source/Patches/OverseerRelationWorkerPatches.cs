using HarmonyLib;
using RimWorld;
using Verse;

namespace MMT
{
    [HarmonyPatch(typeof(PawnRelationWorker_Overseer), nameof(PawnRelationWorker.OnRelationCreated))]
    public static class OverseerRelationWorkerPatches
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn firstPawn, Pawn secondPawn)
        {
            if (!ModsConfig.BiotechActive)
            {
                return true;
            }

            if (!OverseerlessMechanitorUtility.IsNode(firstPawn) || OverseerlessMechanitorUtility.IsNode(secondPawn))
            {
                return true;
            }

            if (Prefs.DevMode)
            {
                Log.Message(
                    $"[MMT] Skipped vanilla Overseer OnRelationCreated for node controller: " +
                    $"controller={firstPawn.LabelShort}, subject={secondPawn.LabelShort}");
            }

            return false;
        }
    }
}
