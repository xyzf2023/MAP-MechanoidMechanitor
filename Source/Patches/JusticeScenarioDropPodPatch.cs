using System.Collections.Generic;
using HarmonyLib;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(ScenPart_PlayerPawnsArriveMethod), "DoDropPods")]
    public static class JusticeScenarioDropPodPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(
            PlayerPawnsArriveMethod ___method,
            Map map,
            List<Thing> startingItems)
        {
            if (!JusticeScenarioUtility.IsJusticeScenarioActive)
            {
                return true;
            }

            GameInitData? initData = Find.GameInitData;
            if (initData == null || initData.startingAndOptionalPawns.Count != 0)
            {
                return true;
            }

            if (___method != PlayerPawnsArriveMethod.DropPods
                && ___method != PlayerPawnsArriveMethod.Standing)
            {
                return true;
            }

            if (!JusticeScenarioArrivalUtility.TryPrepareDropPodsOrStandingArrival(
                    startingItems,
                    ___method,
                    out Pawn? scenarioMechanitor)
                || scenarioMechanitor == null)
            {
                return false;
            }

            startingItems.Remove(scenarioMechanitor);

            List<Thing> mechanitorGroup = new List<Thing> { scenarioMechanitor };
            foreach (Thing startingItem in startingItems)
            {
                if (startingItem.def.CanHaveFaction)
                {
                    startingItem.SetFactionDirect(Faction.OfPlayer);
                }

                mechanitorGroup.Add(startingItem);
            }

            startingItems.Clear();

            List<List<Thing>> dropGroups = new List<List<Thing>> { mechanitorGroup };
            bool openImmediately = initData.QuickStarted
                || ___method != PlayerPawnsArriveMethod.DropPods;

            DropPodUtility.DropThingGroupsNear(
                MapGenerator.PlayerStartSpot,
                map,
                dropGroups,
                110,
                openImmediately,
                leaveSlag: true,
                canRoofPunch: true,
                forbid: true,
                allowFogged: false);

            return false;
        }
    }
}
