using System.Collections.Generic;
using HarmonyLib;
using MAP_MechanoidMechanitor;
using RimWorld;
using Verse;

namespace MMT
{
    [HarmonyPatch(typeof(MainButtonWorker_ToggleMechTab), nameof(MainButtonWorker_ToggleMechTab.Disabled), MethodType.Getter)]
    public static class MMT_MechMainButtonPatches
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

            if (MapHasVisibleMmtNode(currentMap.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
                || MapHasVisibleMmtNode(currentMap.mapPawns.PawnsInFaction(Faction.OfPlayer)))
            {
                __result = false;
            }
        }

        private static bool MapHasVisibleMmtNode(List<Pawn> pawns)
        {
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn != null
                    && !pawn.Destroyed
                    && !pawn.Dead
                    && MAPMechanitorNodeUtility.IsMechanitorNodeController(pawn))
                {
                    if (Prefs.DevMode)
                    {
                        Log.Message($"[MMT] Mechs main button enabled by MMT node: pawn={pawn.LabelShort}");
                    }

                    return true;
                }
            }

            return false;
        }
    }
}
