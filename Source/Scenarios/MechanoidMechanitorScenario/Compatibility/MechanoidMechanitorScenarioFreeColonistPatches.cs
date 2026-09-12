using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 为自由殖民者兼容补丁构建由本 MOD 持有的结果列表。
    /// 不直接修改原版或性能 MOD 返回的临时 / 缓存列表，避免污染外部缓存。
    /// </summary>
    internal static class MechanoidMechanitorScenarioFreeColonistResultUtility
    {
        private sealed class MapResultBuffers
        {
            public readonly List<Pawn> FreeColonistsPrimary = new List<Pawn>();
            public readonly List<Pawn> FreeColonistsSecondary = new List<Pawn>();
            public readonly List<Pawn> FreeColonistsSpawnedPrimary = new List<Pawn>();
            public readonly List<Pawn> FreeColonistsSpawnedSecondary = new List<Pawn>();

            public List<Pawn> GetWritableBuffer(
                List<Pawn> source,
                bool requireSpawned)
            {
                List<Pawn> primary = requireSpawned
                    ? FreeColonistsSpawnedPrimary
                    : FreeColonistsPrimary;
                List<Pawn> secondary = requireSpawned
                    ? FreeColonistsSpawnedSecondary
                    : FreeColonistsSecondary;

                // 正常情况下 source 来自原版或性能 MOD，与本地缓冲区不同。
                // 保留备用缓冲区，防止其他缓存补丁在后续调用中返回了我们上次的结果。
                return ReferenceEquals(source, primary)
                    ? secondary
                    : primary;
            }
        }

        private static readonly ConditionalWeakTable<MapPawns, MapResultBuffers>
            MapBuffers =
                new ConditionalWeakTable<MapPawns, MapResultBuffers>();

        public static List<Pawn> BuildMapResult(
            MapPawns mapPawns,
            List<Pawn> source,
            bool requireSpawned)
        {
            if (!GameComponent_MechanoidMechanitorScenarioState.IsEnabled)
            {
                return source;
            }

            IReadOnlyList<Pawn> registeredMechanitors =
                GameComponent_MechanoidMechanitorRegistry.CurrentRegisteredMechanitors;
            List<Pawn>? augmentedResult = null;

            for (int i = 0; i < registeredMechanitors.Count; i++)
            {
                Pawn pawn = registeredMechanitors[i];
                if (!MechanoidMechanitorScenarioFreeColonistUtility
                        .IsEligibleForMapFreeColonistAppend(
                            pawn,
                            mapPawns,
                            requireSpawned))
                {
                    continue;
                }

                List<Pawn> currentResult = augmentedResult ?? source;
                if (currentResult.Contains(pawn))
                {
                    continue;
                }

                if (augmentedResult == null)
                {
                    MapResultBuffers buffers =
                        MapBuffers.GetValue(
                            mapPawns,
                            CreateMapResultBuffers);
                    augmentedResult =
                        buffers.GetWritableBuffer(source, requireSpawned);
                    augmentedResult.Clear();
                    augmentedResult.AddRange(source);
                }

                augmentedResult.Add(pawn);
            }

            return augmentedResult ?? source;
        }

        private static MapResultBuffers CreateMapResultBuffers(MapPawns _)
        {
            return new MapResultBuffers();
        }
    }

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

            __result =
                MechanoidMechanitorScenarioFreeColonistResultUtility
                    .BuildMapResult(
                        __instance,
                        __result,
                        requireSpawned: false);
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

            __result =
                MechanoidMechanitorScenarioFreeColonistResultUtility
                    .BuildMapResult(
                        __instance,
                        __result,
                        requireSpawned: true);
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

            List<Pawn> augmentedResult =
                new List<Pawn>(__result.Count + 1);
            augmentedResult.AddRange(__result);
            augmentedResult.Add(host);
            __result = augmentedResult;
        }
    }
}
