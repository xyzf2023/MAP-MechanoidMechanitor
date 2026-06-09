using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(StatWorker), nameof(StatWorker.GetValueUnfinalized))]
    public static class StatsPatches
    {
        private static readonly FieldInfo StatField = AccessTools.Field(typeof(StatWorker), "stat");

        [HarmonyPostfix]
        public static void GetValueUnfinalized_Postfix(StatWorker __instance, StatRequest req, ref float __result)
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

            if (!MAPMechanitorNodeUtility.HasNode(pawn))
            {
                return;
            }

            if (StatField == null || StatField.GetValue(__instance) is not StatDef stat)
            {
                return;
            }

            if (stat == StatDefOf.MechBandwidth)
            {
                int extra = MAPMechanitorNodeUtility.GetExtraMechBandwidth(pawn);
                if (extra > 0)
                {
                    __result += extra;
                }

                return;
            }

            if (stat == StatDefOf.MechControlGroups)
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
