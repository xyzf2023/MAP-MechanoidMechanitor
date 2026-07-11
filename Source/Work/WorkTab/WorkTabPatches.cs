using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(MainTabWindow_Work), "get_Pawns")]
    public static class WorkTabPatches
    {
        [HarmonyPostfix]
        public static void Postfix(ref IEnumerable<Pawn> __result)
        {
            if (__result == null)
            {
                return;
            }

            MAPMechanitorModSettings? settings = MAPMechanitorMod.Settings;
            if (settings == null || !settings.addMechanoidMechanitorsToWorkTab)
            {
                return;
            }

            Map? map = Find.CurrentMap;
            if (map == null)
            {
                return;
            }

            List<Pawn> pawns = __result.ToList();
            HashSet<Pawn> existing = new HashSet<Pawn>(pawns);

            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (!CompWorkTabVisibleUser.PawnCanShowInWorkTab(pawn))
                {
                    continue;
                }

                CompWorkTabVisibleUser.EnsureWorkSettingsForWorkTab(pawn);

                if (existing.Add(pawn))
                {
                    pawns.Add(pawn);
                }
            }

            __result = pawns;
        }
    }
}
