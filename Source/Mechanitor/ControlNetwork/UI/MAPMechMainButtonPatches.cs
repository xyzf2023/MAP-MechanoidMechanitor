using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(MainButtonWorker_ToggleMechTab), nameof(MainButtonWorker_ToggleMechTab.Disabled), MethodType.Getter)]
    public static class MAPMechMainButtonPatches
    {
        [HarmonyPostfix]
        public static void Postfix(ref bool __result)
        {
            if (!__result || !ModsConfig.BiotechActive)
            {
                return;
            }

            Map? currentMap = Find.CurrentMap;
            if (currentMap == null)
            {
                return;
            }

            if (MapHasVisibleMapNode(currentMap.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
                || MapHasVisibleMapNode(currentMap.mapPawns.PawnsInFaction(Faction.OfPlayer)))
            {
                __result = false;
            }
        }

        private static bool MapHasVisibleMapNode(List<Pawn> pawns)
        {
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn != null
                    && !pawn.Destroyed
                    && !pawn.Dead
                    && MAPMechanitorNodeUtility.IsMechanitorNodeController(pawn))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
