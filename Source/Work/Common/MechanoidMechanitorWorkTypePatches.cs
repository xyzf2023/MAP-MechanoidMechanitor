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
            WorkTypeDef? childcare = MechanicalChildcareUtility.ChildcareWorkType;

            for (int i = __result.Count - 1; i >= 0; i--)
            {
                WorkTypeDef workType = __result[i];

                bool authorizedWarden = false;
                if (warden != null && workType == warden)
                {
                    authorizedWarden = WardenWorkUtility.IsAuthorized(__instance);
                }

                bool authorizedHandling = false;
                if (handling != null && workType == handling)
                {
                    authorizedHandling = AnimalHandlingWorkUtility.IsAuthorized(__instance);
                }

                bool authorizedChildcare = false;
                if (childcare != null && workType == childcare)
                {
                    authorizedChildcare = MechanicalChildcareUtility.IsAuthorized(__instance);
                }

                // 通用类别在原版种族限制分支中开放，不在 Postfix 中删除其最终禁用结果。
                // 此处仅保留已有特殊工作授权，避免抹掉其他 MOD 对通用工作的禁用。
                if (!authorizedWarden
                    && !authorizedHandling
                    && !authorizedChildcare)
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
