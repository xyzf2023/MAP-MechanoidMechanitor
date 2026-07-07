using System;
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

            try
            {
                JusticeScenarioArrivalUtility.TryPrepareGravshipArrival(startingItems);
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 特殊剧本逆重飞船初始化发生异常，将继续执行原版逆重飞船生成：" +
                    ex);
            }
        }
    }
}
