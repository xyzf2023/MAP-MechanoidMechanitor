using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MMT
{
    [HarmonyPatch(typeof(Pawn_MechanitorTracker), nameof(Pawn_MechanitorTracker.UsedBandwidthFromSubjects), MethodType.Getter)]
    public static class ShadowBandwidthPatches
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn_MechanitorTracker __instance, ref int __result)
        {
            Pawn controller = __instance.Pawn;
            if (controller == null || !OverseerlessMechanitorUtility.IsNode(controller))
            {
                return;
            }

            MMT_ShadowOverseerManager? manager = MMT_ShadowOverseerManager.Current;
            if (manager == null)
            {
                return;
            }

            List<Pawn> shadowSubjects = manager.GetShadowSubjectsFor(controller);
            int shadowBandwidth = 0;
            for (int i = 0; i < shadowSubjects.Count; i++)
            {
                Pawn subject = shadowSubjects[i];
                if (subject == null || subject.IsGestating())
                {
                    continue;
                }

                shadowBandwidth += (int)subject.GetStatValue(StatDefOf.BandwidthCost);
            }

            __result += shadowBandwidth;
        }
    }
}
