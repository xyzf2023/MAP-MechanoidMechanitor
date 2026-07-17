using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(
        typeof(TransportersArrivalActionUtility),
        nameof(TransportersArrivalActionUtility.AnyNonDownedColonist))]
    public static class MAPTransportersArrivalActionUtilityAnyNonDownedColonistPatch
    {
        [HarmonyPostfix]
        public static void Postfix(IEnumerable<IThingHolder> pods, ref bool __result)
        {
            if (__result)
            {
                return;
            }

            if (!ModsConfig.BiotechActive || pods == null)
            {
                return;
            }

            try
            {
                foreach (IThingHolder? holder in pods)
                {
                    if (holder == null)
                    {
                        continue;
                    }

                    ThingOwner? directlyHeldThings;
                    try
                    {
                        directlyHeldThings = holder.GetDirectlyHeldThings();
                    }
                    catch
                    {
                        continue;
                    }

                    if (directlyHeldThings == null)
                    {
                        continue;
                    }

                    for (int i = 0; i < directlyHeldThings.Count; i++)
                    {
                        // 到达资格要求完整 TravelLeadCaravan，不能用 ShuttlePilot 替代。
                        if (directlyHeldThings[i] is not Pawn pawn
                            || pawn.Destroyed
                            || !MAPMechanitorTravelUtility.CanLeadCaravan(pawn))
                        {
                            continue;
                        }

                        __result = true;
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                // 不得因兼容补丁打断原版目标选择流程。
                Log.Error(
                    "[MAP-机械族机械师] 扩展运输到达资格判断异常：" + ex);
            }
        }
    }
}
