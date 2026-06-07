using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MMT
{
    [HarmonyPatch(typeof(Pawn_MechanitorTracker), nameof(Pawn_MechanitorTracker.Notify_BandwidthChanged))]
    public static class ShadowControlledPawnsDebugPatches
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn_MechanitorTracker __instance)
        {
            if (!Prefs.DevMode)
            {
                return;
            }

            Pawn controller = __instance.Pawn;
            if (controller == null || !OverseerlessMechanitorUtility.IsNode(controller))
            {
                return;
            }

            List<Pawn> shadowSubjects = MMT_ShadowOverseerManager.Current?.GetShadowSubjectsFor(controller)
                ?? new List<Pawn>();
            List<Pawn> controlledPawns = __instance.ControlledPawns;
            List<Pawn> overseenPawns = __instance.OverseenPawns;

            bool allShadowControlled = true;
            for (int i = 0; i < shadowSubjects.Count; i++)
            {
                Pawn subject = shadowSubjects[i];
                if (subject != null && !controlledPawns.Contains(subject))
                {
                    allShadowControlled = false;
                    break;
                }
            }

            Log.Message(
                $"[MMT] ControlledPawns check: controller={controller.LabelShort}, " +
                $"shadowCount={shadowSubjects.Count}, overseenCount={overseenPawns.Count}, " +
                $"controlledCount={controlledPawns.Count}, allShadowControlled={allShadowControlled}");
        }
    }
}
