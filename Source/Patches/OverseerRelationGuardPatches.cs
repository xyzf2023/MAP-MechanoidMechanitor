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

        private static bool IsProtectedMapNode(Pawn pawn)
        {
            return MAPMechanitorNodeUtility.HasNode(pawn)
                && !MAPMechanitorNodeUtility.RequiresExternalOverseer(pawn);
        }

        private static bool AnyControlGroupContains(Pawn controller, Pawn subject)
        {
            if (controller.mechanitor?.controlGroups == null)
            {
                return false;
            }

            for (int i = 0; i < controller.mechanitor.controlGroups.Count; i++)
            {
                if (controller.mechanitor.controlGroups[i].MechsForReading.Contains(subject))
                {
                    return true;
                }
            }

            return false;
        }

        [HarmonyPostfix]
        public static void Postfix(
            Pawn_RelationsTracker __instance,
            PawnRelationDef def,
            Pawn otherPawn,
            bool __runOriginal)
        {
            if (def != PawnRelationDefOf.Overseer)
            {
                return;
            }

            Pawn? controller = GetRelationsPawn(__instance);

            if (Prefs.DevMode
                && ModsConfig.BiotechActive
                && controller != null
                && otherPawn != null
                && MAPMechanitorNodeUtility.UsesVanillaControlPath(controller)
                && !IsProtectedMapNode(otherPawn))
            {
                bool controllerDirectRelationExists =
                    controller.relations != null
                    && controller.relations.DirectRelationExists(PawnRelationDefOf.Overseer, otherPawn);

                bool subjectDirectRelationExists =
                    otherPawn.relations != null
                    && otherPawn.relations.DirectRelationExists(PawnRelationDefOf.Overseer, controller);

                Pawn? subjectOverseer = otherPawn.GetOverseer();

                bool controllerControlledContainsSubject =
                    controller.mechanitor?.ControlledPawns != null
                    && controller.mechanitor.ControlledPawns.Contains(otherPawn);

                bool controllerAnyControlGroupContainsSubject =
                    AnyControlGroupContains(controller, otherPawn);

                Log.Message(
                    "[MMT] Vanilla overseer post-add diagnostic: " +
                    $"runOriginal={__runOriginal}, " +
                    $"controller={controller.LabelShort}, " +
                    $"subject={otherPawn.LabelShort}, " +
                    $"controllerUsesVanilla={MAPMechanitorNodeUtility.UsesVanillaControlPath(controller)}, " +
                    $"controllerUsesShadow={MAPMechanitorNodeUtility.UsesShadowControlPath(controller)}, " +
                    $"controllerHasMechanitor={(controller.mechanitor != null)}, " +
                    $"controllerHasRelations={(controller.relations != null)}, " +
                    $"subjectHasRelations={(otherPawn.relations != null)}, " +
                    $"controllerDirectRelationExists={controllerDirectRelationExists}, " +
                    $"subjectDirectRelationExists={subjectDirectRelationExists}, " +
                    $"subjectGetOverseer={(subjectOverseer?.LabelShort ?? "null")}, " +
                    $"controllerControlledContainsSubject={controllerControlledContainsSubject}, " +
                    $"controllerAnyControlGroupContainsSubject={controllerAnyControlGroupContainsSubject}, " +
                    $"controllerControlGroupsCount={controller.mechanitor?.controlGroups?.Count}, " +
                    $"controllerControlledPawnsCount={controller.mechanitor?.ControlledPawns?.Count}");
            }

            if (!__runOriginal)
            {
                return;
            }

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
