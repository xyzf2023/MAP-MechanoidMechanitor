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
    public static class MechanoidMechanitorScenario_MapPawns_FreeColonists_Patch
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

            if (!GameComponent_MechanoidMechanitorScenarioState.IsEnabled)
            {
                return;
            }

            IReadOnlyList<Pawn> registeredMechanitors =
                GameComponent_MechanoidMechanitorRegistry.CurrentRegisteredMechanitors;
            for (int i = 0; i < registeredMechanitors.Count; i++)
            {
                Pawn pawn = registeredMechanitors[i];
                if (!MechanoidMechanitorScenarioFreeColonistUtility.IsEligibleForMapFreeColonistAppend(
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
    public static class MechanoidMechanitorScenario_MapPawns_FreeColonistsSpawned_Patch
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

            if (!GameComponent_MechanoidMechanitorScenarioState.IsEnabled)
            {
                return;
            }

            IReadOnlyList<Pawn> registeredMechanitors =
                GameComponent_MechanoidMechanitorRegistry.CurrentRegisteredMechanitors;
            for (int i = 0; i < registeredMechanitors.Count; i++)
            {
                Pawn pawn = registeredMechanitors[i];
                if (!MechanoidMechanitorScenarioFreeColonistUtility.IsEligibleForMapFreeColonistAppend(
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
    /// 在机械族机械师专属玩法中，让唯一的机械意识宿主在需要"全局自由殖民者代表"的系统中
    /// 充当替代者，以兼容标准 GiveQuest 任务生成逻辑（如奥德赛机械族信号/逆重引擎任务）。
    /// </summary>
    [HarmonyPatch(
        typeof(PawnsFinder),
        nameof(PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists_NoSuspended),
        MethodType.Getter)]
    public static class MechanoidMechanitorScenario_PawnsFinder_AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists_NoSuspended_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ref List<Pawn> __result)
        {
            if (__result == null)
            {
                return;
            }

            if (!GameComponent_MechanoidMechanitorScenarioState.IsEnabled)
            {
                return;
            }

            Pawn? host =
                GameComponent_MechanoidMechanitorRegistry.CurrentMechanicalConsciousnessHost;
            if (!MechanoidMechanitorScenarioFreeColonistUtility.IsEligibleGlobal(host))
            {
                return;
            }

            if (host!.Suspended)
            {
                return;
            }

            if (!MechanoidMechanitorScenarioFreeColonistUtility
                    .IsPresentInMapsCaravansAndTransporters(host))
            {
                return;
            }

            if (__result.Contains(host))
            {
                return;
            }

            __result.Add(host);
        }
    }
}
