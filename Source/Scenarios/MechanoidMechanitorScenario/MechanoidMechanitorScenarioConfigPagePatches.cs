using System;
using HarmonyLib;
using MAP_MechanoidMechanitor;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    [HarmonyPatch(
        typeof(Scenario),
        nameof(Scenario.GetFirstConfigPage))]
    public static class
        MechanoidMechanitorScenario_Scenario_GetFirstConfigPage_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(
            Scenario __instance,
            ref Page __result)
        {
            bool containsScenarioMarker = MechanoidMechanitorScenarioUtility
                .ScenarioContainsMarker(__instance);
            bool allowGeneralScenarioStoryStyles =
                MAPMechanitorMod.Settings?.enableStoryStylesForGeneralScenarios == true;

            if (!containsScenarioMarker && !allowGeneralScenarioStoryStyles)
            {
                return;
            }

            if (__result == null)
            {
                __result = new Page_MechanoidMechanitorScenarioReady
                {
                    nextAct = PageUtility.InitGameStart
                };

                return;
            }

            Page lastPage = __result;

            while (lastPage.next != null)
            {
                if (lastPage is Page_MechanoidMechanitorScenarioReady)
                {
                    return;
                }

                lastPage = lastPage.next;
            }

            if (lastPage is Page_MechanoidMechanitorScenarioReady)
            {
                return;
            }

            Action? originalNextAct = lastPage.nextAct;

            Page_MechanoidMechanitorScenarioReady readyPage =
                new Page_MechanoidMechanitorScenarioReady
                {
                    prev = lastPage,
                    nextAct =
                        originalNextAct ?? PageUtility.InitGameStart
                };

            lastPage.nextAct = null;
            lastPage.next = readyPage;
        }
    }
}
