using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetDisabledWorkTypes))]
    public static class Patch_Pawn_GetDisabledWorkTypes_AcquiredMechanitor
    {
        [HarmonyPostfix]
        public static void Postfix(
            Pawn __instance,
            bool permanentOnly,
            ref List<WorkTypeDef> __result)
        {
            if (__result == null)
            {
                return;
            }

            WorkTypeDef? warden = WardenWorkUtility.WardenWorkType;
            WorkTypeDef? handling = AnimalHandlingWorkUtility.HandlingWorkType;
            bool isAcquiredMechanitor =
                MechanoidMechanitorRoleUtility.IsAcquiredMechanoidMechanitor(__instance);

            for (int i = __result.Count - 1; i >= 0; i--)
            {
                WorkTypeDef workType = __result[i];
                bool authorizedWarden =
                    warden != null
                    && workType == warden
                    && WardenWorkUtility.IsAuthorized(__instance);
                bool authorizedHandling =
                    handling != null
                    && workType == handling
                    && AnimalHandlingWorkUtility.IsAuthorized(__instance);
                bool acquiredRoleWorkType =
                    isAcquiredMechanitor
                    && MechanoidMechanitorRoleUtility.IsRoleWorkType(workType);

                if (!authorizedWarden && !authorizedHandling && !acquiredRoleWorkType)
                {
                    continue;
                }

                if (!MechWorkTypeAuthorizationUtility.IsDisabledOutsideMechRaceProfile(
                        __instance,
                        workType,
                        permanentOnly))
                {
                    __result.RemoveAt(i);
                }
            }
        }
    }
}
