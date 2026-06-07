using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MMT
{
    [HarmonyPatch(typeof(Pawn_MechanitorTracker), nameof(Pawn_MechanitorTracker.OverseenPawns), MethodType.Getter)]
    public static class ShadowOverseenPawnsPatches
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn_MechanitorTracker __instance, ref List<Pawn> __result)
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
            if (shadowSubjects.Count == 0)
            {
                return;
            }

            __result ??= new List<Pawn>();

            for (int i = 0; i < shadowSubjects.Count; i++)
            {
                Pawn subject = shadowSubjects[i];
                if (subject == null
                    || OverseerlessMechanitorUtility.IsNode(subject)
                    || subject.relations?.GetFirstDirectRelationPawn(PawnRelationDefOf.Overseer) != null
                    || __result.Contains(subject))
                {
                    continue;
                }

                __result.Add(subject);
            }
        }
    }
}
