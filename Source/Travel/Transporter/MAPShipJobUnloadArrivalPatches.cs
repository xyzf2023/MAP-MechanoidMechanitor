using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(
        typeof(ShipJob_Unload),
        nameof(ShipJob_Unload.UnloadThingFromShuttle))]
    public static class MAPShipJobUnloadThingFromShuttlePatch
    {
        private const int RefreshTrackerFailureLogKeyBase = 0x4D415053; // "MAPS"

        [HarmonyPostfix]
        public static void Postfix(Thing thingToDrop)
        {
            if (thingToDrop is not Pawn pawn)
            {
                return;
            }

            if (!pawn.Spawned
                || pawn.Map == null
                || pawn.Dead
                || pawn.Destroyed)
            {
                return;
            }

            try
            {
                if (!MAPMechanitorTravelUtility.ShouldRefreshTrackersOnTransporterArrival(pawn))
                {
                    return;
                }

                MAPMechanitorInitializationUtility.FinalizeNow(pawn);
            }
            catch (Exception ex)
            {
                // 刷新属于补充行为：不得中断原版穿梭机卸载流程。
                Log.ErrorOnce(
                    "[MAP-机械族机械师] 穿梭机卸载后刷新 Tracker 失败：" +
                    $"pawn={pawn.LabelShort}（{pawn.ThingID}），" +
                    $"map={pawn.Map}：{ex}",
                    unchecked(RefreshTrackerFailureLogKeyBase + pawn.thingIDNumber));
            }
        }
    }
}
