using HarmonyLib;
using MAP_MechanoidMechanitor;
using RimWorld.Planet;
using Verse;

namespace MMT
{
    [HarmonyPatch(typeof(Camp), nameof(Camp.ShouldRemoveMapNow))]
    public static class MMT_CampShouldRemoveMapNowPatch
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
                        $"[MMT] Camp map removal blocked by MAP mechanitor travel node: " +
                        $"pawn={pawn.LabelShort}, map={map}, camp={__instance}");
                }

                return;
            }
        }
    }
}
