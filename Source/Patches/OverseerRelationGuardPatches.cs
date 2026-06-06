using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MMT
{
    [HarmonyPatch(typeof(Pawn_RelationsTracker), nameof(Pawn_RelationsTracker.AddDirectRelation))]
    public static class OverseerRelationGuardPatches
    {
        private static readonly FieldInfo RelationsPawnField =
            AccessTools.Field(typeof(Pawn_RelationsTracker), "pawn");

        private static Pawn? GetRelationsPawn(Pawn_RelationsTracker relations)
        {
            if (relations == null || RelationsPawnField == null)
            {
                return null;
            }

            return RelationsPawnField.GetValue(relations) as Pawn;
        }

        [HarmonyPrefix]
        public static bool Prefix(Pawn_RelationsTracker __instance, PawnRelationDef def, Pawn otherPawn)
        {
            if (def != PawnRelationDefOf.Overseer)
            {
                return true;
            }

            Pawn? controller = GetRelationsPawn(__instance);

            if (Prefs.DevMode)
            {
                Log.Message(
                    $"[MMT] Overseer relation request: controller={controller?.LabelShort ?? "null"}, " +
                    $"subject={otherPawn?.LabelShort ?? "null"}, " +
                    $"controllerIsNode={OverseerlessMechanitorUtility.IsNode(controller)}, " +
                    $"subjectIsNode={OverseerlessMechanitorUtility.IsNode(otherPawn)}");
            }

            if (otherPawn != null && OverseerlessMechanitorUtility.IsNode(otherPawn))
            {
                if (Prefs.DevMode)
                {
                    Log.Message(
                        $"[MMT] Blocked attempt to assign overseer to node: controller={controller?.LabelShort ?? "null"}, " +
                        $"node={otherPawn.LabelShort}");
                }

                return false;
            }

            if (controller != null && otherPawn != null && controller == otherPawn && OverseerlessMechanitorUtility.IsNode(otherPawn))
            {
                return false;
            }

            return true;
        }

        [HarmonyPostfix]
        public static void Postfix(Pawn_RelationsTracker __instance, PawnRelationDef def, Pawn otherPawn)
        {
            if (def != PawnRelationDefOf.Overseer)
            {
                return;
            }

            Pawn? controller = GetRelationsPawn(__instance);
            if (controller == null
                || !OverseerlessMechanitorUtility.IsNode(controller)
                || otherPawn == null
                || OverseerlessMechanitorUtility.IsNode(otherPawn))
            {
                return;
            }

            if (Prefs.DevMode)
            {
                Pawn? subjectOverseer = otherPawn.GetOverseer();
                Log.Message(
                    $"[MMT] Overseer relation added for node controller: controller={controller.LabelShort}, " +
                    $"subject={otherPawn.LabelShort}, subjectOverseer={subjectOverseer?.LabelShort ?? "null"}");
            }
        }
    }
}
