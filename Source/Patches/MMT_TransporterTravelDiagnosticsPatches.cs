using System.Collections.Generic;
using HarmonyLib;
using MAP_MechanoidMechanitor;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MMT
{
    [HarmonyPatch(typeof(TransportersArrivalAction_FormCaravan), nameof(TransportersArrivalAction_FormCaravan.CanFormCaravanAt))]
    public static class MMT_TransporterCanFormCaravanAtDiagnosticsPatch
    {
        [HarmonyPostfix]
        public static void Postfix(IEnumerable<IThingHolder> pods, PlanetTile tile, ref bool __result)
        {
            if (!Prefs.DevMode)
            {
                return;
            }

            bool hasMmtNode = false;
            int mmtCount = 0;
            foreach (IThingHolder pod in pods)
            {
                ThingOwner directlyHeldThings = pod.GetDirectlyHeldThings();
                for (int i = 0; i < directlyHeldThings.Count; i++)
                {
                    if (directlyHeldThings[i] is Pawn pawn && MAPMechanitorTravelUtility.CanLeadCaravan(pawn))
                    {
                        hasMmtNode = true;
                        mmtCount++;
                    }
                }
            }

            if (!hasMmtNode)
            {
                return;
            }

            Log.Message(
                $"[MMT] Transporter caravan form check: mmtCount={mmtCount}, result={__result}, tile={tile}");
        }
    }

    [HarmonyPatch(typeof(TransportersArrivalAction_FormCaravan), nameof(TransportersArrivalAction_FormCaravan.Arrived))]
    public static class MMT_TransporterArrivedDiagnosticsPatch
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

                    OverseerlessMechanitorUtility.EnsureBasicTrackers(pawn);

                    if (!Prefs.DevMode)
                    {
                        continue;
                    }

                    bool hasOverseer = pawn.GetOverseer() != null;
                    Log.Message(
                        $"[MMT] Transporter caravan arrived with node: pawn={pawn.LabelShort}, tile={searchTile}, " +
                        $"isWorldPawn={pawn.IsWorldPawn()}, isMechanitor={MechanitorUtility.IsMechanitor(pawn)}, " +
                        $"hasOverseer={hasOverseer}");

                    if (hasOverseer)
                    {
                        Log.Warning(
                            $"[MMT] Warning: travel node unexpectedly has overseer after transporter arrival: pawn={pawn.LabelShort}");
                    }
                }
            }
        }
    }
}
