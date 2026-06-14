using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(Camp), nameof(Camp.ShouldRemoveMapNow))]
    public static class MAPCampShouldRemoveMapNowPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Camp __instance, ref bool __result, ref bool alsoRemoveWorldObject)
        {
            if (!__result)
            {
                return;
            }

            if (__instance == null || !__instance.HasMap)
            {
                return;
            }

            Map map = __instance.Map;
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (!MAPMechanitorTravelUtility.ShouldBlockCampMapRemoval(pawn))
                {
                    continue;
                }

                __result = false;
                alsoRemoveWorldObject = false;

                if (Prefs.DevMode)
                {
                    Log.Message(
                        $"[MAP-MechanoidMechanitor] Camp map removal blocked by MAP mechanitor travel node: " +
                        $"pawn={pawn.LabelShort}, map={map}, camp={__instance}");
                }

                return;
            }
        }
    }
}
