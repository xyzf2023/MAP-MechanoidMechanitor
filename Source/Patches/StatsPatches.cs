using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(StatWorker), nameof(StatWorker.GetValueUnfinalized))]
    public static class StatsPatches
    {
        [HarmonyPostfix]
        public static void GetValueUnfinalized_Postfix(
            StatRequest req,
            StatDef ___stat,
            ref float __result)
        {
            if (!ModsConfig.BiotechActive)
            {
                return;
            }

            if (req.Thing is not Pawn pawn || pawn == null)
            {
                return;
            }

            if (!pawn.RaceProps.IsMechanoid)
            {
                return;
            }

            if (___stat == StatDefOf.MechBandwidth)
            {
                int extra = MAPMechanitorNodeUtility.GetExtraMechBandwidth(pawn);
                if (extra > 0)
                {
                    __result += extra;
                }

                return;
            }

            if (___stat == StatDefOf.MechControlGroups)
            {
                int extra = MAPMechanitorNodeUtility.GetExtraMechControlGroups(pawn);
                if (extra > 0)
                {
                    __result += extra;
                }
            }
        }
    }
}
