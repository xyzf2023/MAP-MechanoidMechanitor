using System.Reflection;
using HarmonyLib;
using MAP_MechanoidMechanitor;
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
                    $"controllerUsesShadow={MAPMechanitorNodeUtility.UsesShadowControlPath(controller)}, " +
                    $"subjectRequiresExternalOverseer={MAPMechanitorNodeUtility.RequiresExternalOverseer(otherPawn)}");
            }

            if (otherPawn != null
                && MAPMechanitorNodeUtility.HasNode(otherPawn)
                && !MAPMechanitorNodeUtility.RequiresExternalOverseer(otherPawn))
            {
                if (Prefs.DevMode)
                {
                    Log.Message(
                        $"[MMT] Blocked attempt to assign overseer to node: controller={controller?.LabelShort ?? "null"}, " +
                        $"node={otherPawn.LabelShort}");
                }

                return false;
            }

            if (controller != null
                && otherPawn != null
                && controller == otherPawn
                && MAPMechanitorNodeUtility.IsMechanitorNodeController(otherPawn))
            {
                return false;
            }

            if (controller != null
                && MAPMechanitorNodeUtility.UsesShadowControlPath(controller)
                && otherPawn != null
                && !MAPMechanitorNodeUtility.HasNode(otherPawn))
            {
                MMT_ShadowOverseerManager? manager = MMT_ShadowOverseerManager.EnsureInstance();
                if (manager == null)
                {
                    if (Prefs.DevMode)
                    {
                        Log.Warning(
                            "[MMT] Shadow overseer manager missing; blocked vanilla relation without shadow record.");
                    }
                }
                else
                {
                    manager.SetShadowOverseer(otherPawn, controller);

                    if (Prefs.DevMode)
                    {
                        Log.Message(
                            $"[MMT] Created shadow overseer relation: controller={controller.LabelShort}, " +
                            $"subject={otherPawn.LabelShort}");
                    }
                }

                if (Prefs.DevMode)
                {
                    Log.Message(
                        $"[MMT] Blocked vanilla overseer relation for shadow node controller; use shadow overseer instead: " +
                        $"controller={controller.LabelShort}, subject={otherPawn.LabelShort}");
                }

                return false;
            }

            if (controller != null
                && !MAPMechanitorNodeUtility.UsesShadowControlPath(controller)
                && otherPawn != null
                && !MAPMechanitorNodeUtility.HasNode(otherPawn))
            {
                MMT_ShadowOverseerManager? manager = MMT_ShadowOverseerManager.Current;
                Pawn? oldShadow = manager?.GetShadowOverseer(otherPawn);
                if (oldShadow != null && manager != null)
                {
                    manager.RemoveShadowOverseer(otherPawn);
                    if (Prefs.DevMode)
                    {
                        Log.Message(
                            $"[MMT] Removed shadow overseer because vanilla overseer took over: " +
                            $"oldShadow={oldShadow.LabelShort}, newOverseer={controller.LabelShort}, " +
                            $"subject={otherPawn.LabelShort}");
                    }
                }
            }

            return true;
        }

        [HarmonyPostfix]
        public static void Postfix(
            Pawn_RelationsTracker __instance,
            PawnRelationDef def,
            Pawn otherPawn,
            bool __runOriginal)
        {
            if (!__runOriginal || def != PawnRelationDefOf.Overseer)
            {
                return;
            }

            Pawn? controller = GetRelationsPawn(__instance);
            if (controller == null
                || !MAPMechanitorNodeUtility.UsesShadowControlPath(controller)
                || otherPawn == null
                || MAPMechanitorNodeUtility.HasNode(otherPawn))
            {
                return;
            }

            if (controller.relations == null
                || !controller.relations.DirectRelationExists(PawnRelationDefOf.Overseer, otherPawn))
            {
                return;
            }

            if (Prefs.DevMode)
            {
                Pawn? subjectOverseer = otherPawn.GetOverseer();
                Log.Message(
                    $"[MMT] Overseer relation added for shadow node controller: controller={controller.LabelShort}, " +
                    $"subject={otherPawn.LabelShort}, subjectOverseer={subjectOverseer?.LabelShort ?? "null"}");
            }
        }
    }
}
