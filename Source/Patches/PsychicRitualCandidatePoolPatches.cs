using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(PsychicRitualDef), nameof(PsychicRitualDef.FindCandidatePool))]
    public static class PsychicRitualDef_FindCandidatePool_Patch
    {
        public static void Postfix(ref PsychicRitualCandidatePool __result)
        {
            if (!ModsConfig.AnomalyActive || __result == null)
            {
                return;
            }

            Map? map = Find.CurrentMap;
            if (map == null)
            {
                return;
            }

            List<Pawn> allCandidatePawns = __result.AllCandidatePawns;
            if (allCandidatePawns == null)
            {
                return;
            }

            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (!MAPPsychicRitualUtility.IsAllowedPsychicRitualParticipant(pawn))
                {
                    continue;
                }

                allCandidatePawns.AddUnique(pawn);
            }
        }
    }
}
