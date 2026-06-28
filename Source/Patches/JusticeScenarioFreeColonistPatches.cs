using System.Collections.Generic;
using HarmonyLib;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
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
                if (!JusticeScenarioFreeColonistUtility.IsEligibleForMapFreeColonistAppend(
                        pawn,
                        __instance,
                        requireSpawned: false))
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
                if (!JusticeScenarioFreeColonistUtility.IsEligibleForMapFreeColonistAppend(
                        pawn,
                        __instance,
                        requireSpawned: true))
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

    /// <summary>
    /// 在机械族机械师专属剧本中，让唯一的剧本主角在需要"全局自由殖民者代表"的系统中
    /// 充当替代者，以兼容标准 GiveQuest 任务生成逻辑（如奥德赛机械族信号/逆重引擎任务）。
    /// </summary>
    [HarmonyPatch(
        typeof(PawnsFinder),
        nameof(PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists_NoSuspended),
        MethodType.Getter)]
    public static class JusticeScenario_PawnsFinder_AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists_NoSuspended_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ref List<Pawn> __result)
        {
            if (__result == null)
            {
                return;
            }

            if (!GameComponent_JusticeScenarioState.IsEnabled)
            {
                return;
            }

            Pawn? protagonist = JusticeScenarioUtility.ScenarioProtagonist;
            if (!JusticeScenarioFreeColonistUtility.IsEligibleGlobal(protagonist))
            {
                return;
            }

            if (protagonist!.Suspended)
            {
                return;
            }

            if (!JusticeScenarioFreeColonistUtility.IsPresentInMapsCaravansAndTransporters(protagonist))
            {
                return;
            }

            if (__result.Contains(protagonist))
            {
                return;
            }

            __result.Add(protagonist);
        }
    }
}
