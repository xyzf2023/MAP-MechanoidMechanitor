using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(CompShuttle), "get_HasPilot")]
    public static class ShuttlePilot_CompShuttle_HasPilot_Patch
    {
        public static void Postfix(CompShuttle __instance, ref bool __result)
        {
            if (__result)
            {
                return;
            }

            if (!ModsConfig.OdysseyActive)
            {
                return;
            }

            if (__instance == null || !__instance.IsPlayerShuttle)
            {
                return;
            }

            ThingOwner? innerContainer = __instance.Transporter?.innerContainer;
            if (innerContainer == null)
            {
                return;
            }

            for (int i = 0; i < innerContainer.Count; i++)
            {
                if (innerContainer[i] is Pawn pawn
                    && MAPShuttlePilotUtility.CanServeAsShuttlePilot(pawn))
                {
                    __result = true;
                    return;
                }
            }
        }
    }
}
