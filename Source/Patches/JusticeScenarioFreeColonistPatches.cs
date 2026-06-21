using System.Collections.Generic;
using HarmonyLib;
using MAP_MechanoidMechanitor.Scenarios;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(
        typeof(MapPawns),
        nameof(MapPawns.FreeColonists),
        MethodType.Getter)]
    public static class JusticeScenario_MapPawns_FreeColonists_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(
            MapPawns __instance,
            ref List<Pawn> __result)
        {
            if (__result == null)
            {
                return;
            }

            if (!GameComponent_JusticeScenarioState.IsEnabled)
            {
                return;
            }

            List<Pawn> allPawns = __instance.AllPawns;
            for (int i = 0; i < allPawns.Count; i++)
            {
                Pawn pawn = allPawns[i];
                if (!JusticeScenarioFreeColonistUtility.IsEligible(pawn, __instance, requireSpawned: false))
                {
                    continue;
                }

                if (!__result.Contains(pawn))
                {
                    __result.Add(pawn);
                }
            }
        }
    }

    [HarmonyPatch(
        typeof(MapPawns),
        nameof(MapPawns.FreeColonistsSpawned),
        MethodType.Getter)]
    public static class JusticeScenario_MapPawns_FreeColonistsSpawned_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(
            MapPawns __instance,
            ref List<Pawn> __result)
        {
            if (__result == null)
            {
                return;
            }

            if (!GameComponent_JusticeScenarioState.IsEnabled)
            {
                return;
            }

            IReadOnlyList<Pawn> allPawnsSpawned = __instance.AllPawnsSpawned;
            for (int i = 0; i < allPawnsSpawned.Count; i++)
            {
                Pawn pawn = allPawnsSpawned[i];
                if (!JusticeScenarioFreeColonistUtility.IsEligible(pawn, __instance, requireSpawned: true))
                {
                    continue;
                }

                if (!__result.Contains(pawn))
                {
                    __result.Add(pawn);
                }
            }
        }
    }
}
