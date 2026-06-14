using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(TransportersArrivalAction_FormCaravan), nameof(TransportersArrivalAction_FormCaravan.CanFormCaravanAt))]
    public static class MAPTransporterCanFormCaravanAtDiagnosticsPatch
    {
        [HarmonyPostfix]
        public static void Postfix(IEnumerable<IThingHolder> pods, PlanetTile tile, ref bool __result)
        {
            if (!Prefs.DevMode)
            {
                return;
            }

            bool hasMapNode = false;
            int mapNodeCount = 0;
            foreach (IThingHolder pod in pods)
            {
                ThingOwner directlyHeldThings = pod.GetDirectlyHeldThings();
                for (int i = 0; i < directlyHeldThings.Count; i++)
                {
                    if (directlyHeldThings[i] is Pawn pawn && MAPMechanitorTravelUtility.CanLeadCaravan(pawn))
                    {
                        hasMapNode = true;
                        mapNodeCount++;
                    }
                }
            }

            if (!hasMapNode)
            {
                return;
            }

            Log.Message(
                $"[MAP-MechanoidMechanitor] Transporter caravan form check: mapNodeCount={mapNodeCount}, result={__result}, tile={tile}");
        }
    }

    [HarmonyPatch(typeof(TransportersArrivalAction_FormCaravan), nameof(TransportersArrivalAction_FormCaravan.Arrived))]
    public static class MAPTransporterArrivedDiagnosticsPatch
    {
        [HarmonyPostfix]
        public static void Postfix(PlanetTile tile)
        {
            PlanetTile searchTile = tile;
            if (GenWorldClosest.TryFindClosestPassableTile(tile, out PlanetTile foundTile))
            {
                searchTile = foundTile;
            }

            List<Caravan> caravans = Find.WorldObjects.Caravans;
            for (int i = 0; i < caravans.Count; i++)
            {
                Caravan caravan = caravans[i];
                if (!caravan.IsPlayerControlled || caravan.Tile != searchTile)
                {
                    continue;
                }

                List<Pawn> pawns = caravan.PawnsListForReading;
                for (int j = 0; j < pawns.Count; j++)
                {
                    Pawn pawn = pawns[j];
                    if (!MAPMechanitorTravelUtility.ShouldRefreshTrackersOnTransporterArrival(pawn))
                    {
                        continue;
                    }

                    if (!Prefs.DevMode)
                    {
                        continue;
                    }

                    bool hasOverseer = pawn.GetOverseer() != null;
                    Log.Message(
                        $"[MAP-MechanoidMechanitor] Transporter caravan arrived with node: pawn={pawn.LabelShort}, tile={searchTile}, " +
                        $"isWorldPawn={pawn.IsWorldPawn()}, isMechanitor={MechanitorUtility.IsMechanitor(pawn)}, " +
                        $"hasOverseer={hasOverseer}");

                    if (hasOverseer)
                    {
                        Log.Warning(
                            $"[MAP-MechanoidMechanitor] Warning: travel node unexpectedly has overseer after transporter arrival: pawn={pawn.LabelShort}");
                    }
                }
            }
        }
    }
}
