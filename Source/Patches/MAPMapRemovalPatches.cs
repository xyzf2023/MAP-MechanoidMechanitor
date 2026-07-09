using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(MapPawns), nameof(MapPawns.AnyPawnBlockingMapRemoval), MethodType.Getter)]
    public static class MAPMapPawnsAnyPawnBlockingMapRemovalPatch
    {
        [HarmonyPostfix]
        public static void Postfix(MapPawns __instance, ref bool __result)
        {
            if (__result)
            {
                return;
            }

            if (__instance == null)
            {
                return;
            }

            foreach (Pawn pawn in __instance.AllPawnsSpawned)
            {
                if (!MAPMechanitorTravelUtility.ShouldBlockMapRemoval(pawn))
                {
                    continue;
                }

                __result = true;
                return;
            }
        }
    }
}
