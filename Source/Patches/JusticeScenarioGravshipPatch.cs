using System.Collections.Generic;
using HarmonyLib;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(ScenPart_PlayerPawnsArriveMethod), "DoGravship")]
    public static class JusticeScenarioGravshipPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Map map, List<Thing> startingItems)
        {
            if (!JusticeScenarioUtility.IsJusticeScenarioActive)
            {
                return;
            }

            GameInitData? initData = Find.GameInitData;
            if (initData == null || initData.startingAndOptionalPawns.Count != 0)
            {
                return;
            }

            JusticeScenarioArrivalUtility.TryPrepareGravshipArrival(startingItems);
        }
    }
}
