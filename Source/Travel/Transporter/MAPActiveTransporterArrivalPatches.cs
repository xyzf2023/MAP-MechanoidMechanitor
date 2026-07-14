using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class MAPActiveTransporterArrivalPatchState
    {
        public Map? ExpectedMap;
        public List<Pawn> Pawns { get; } = new List<Pawn>();
    }

    [HarmonyPatch(typeof(ActiveTransporter), "PodOpen")]
    public static class MAPActiveTransporterPodOpenPatch
    {
        private const int RefreshTrackerFailureLogKeyBase = 0x4D415041; // "MAPA"

        [HarmonyPrefix]
        public static void Prefix(
            ActiveTransporter __instance,
            out MAPActiveTransporterArrivalPatchState __state)
        {
            __state = new MAPActiveTransporterArrivalPatchState();

            if (__instance == null || !__instance.Spawned)
            {
                return;
            }

            Map? map = __instance.Map;
            if (map == null)
            {
                return;
            }

            __state.ExpectedMap = map;

            ActiveTransporterInfo? contents = __instance.Contents;
            ThingOwner? container = contents?.innerContainer;
            if (container == null)
            {
                return;
            }

            HashSet<Pawn> seen = new HashSet<Pawn>();
            for (int i = 0; i < container.Count; i++)
            {
                if (container[i] is not Pawn pawn || !seen.Add(pawn))
                {
                    continue;
                }

                __state.Pawns.Add(pawn);
            }
        }

        [HarmonyFinalizer]
        public static Exception? Finalizer(
            Exception? __exception,
            MAPActiveTransporterArrivalPatchState? __state)
        {
            try
            {
                RefreshArrivedPawns(__state);
            }
            catch (Exception ex)
            {
                // 刷新属于补充能力：不得覆盖或吞掉 PodOpen 原始异常。
                Log.Error(
                    "[MAP-机械族机械师] 运输舱到达 Tracker 刷新外层异常：" + ex);
            }

            return __exception;
        }

        private static void RefreshArrivedPawns(MAPActiveTransporterArrivalPatchState? state)
        {
            if (state?.ExpectedMap == null
                || state.Pawns == null
                || state.Pawns.Count == 0)
            {
                return;
            }

            Map expectedMap = state.ExpectedMap;
            for (int i = 0; i < state.Pawns.Count; i++)
            {
                Pawn? pawn = state.Pawns[i];
                if (pawn == null
                    || pawn.Destroyed
                    || !pawn.Spawned
                    || !ReferenceEquals(pawn.Map, expectedMap))
                {
                    continue;
                }

                try
                {
                    if (!MAPMechanitorTravelUtility.ShouldRefreshTrackersOnTransporterArrival(pawn))
                    {
                        continue;
                    }

                    MAPMechanitorInitializationUtility.FinalizeNow(pawn);
                }
                catch (Exception ex)
                {
                    Log.ErrorOnce(
                        "[MAP-机械族机械师] 运输舱到达后刷新 Tracker 失败：" +
                        $"pawn={pawn.LabelShort}（{pawn.ThingID}），" +
                        $"expectedMap={expectedMap}，" +
                        $"currentMap={pawn.Map?.ToString() ?? "null"}：{ex}",
                        unchecked(RefreshTrackerFailureLogKeyBase + pawn.thingIDNumber));
                }
            }
        }
    }
}
