using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MMT
{
    public static class ControlMechPatches
    {
        private static readonly FieldInfo RelationsPawnField =
            AccessTools.Field(typeof(Pawn_RelationsTracker), "pawn");

        internal static Pawn? GetRelationsPawn(Pawn_RelationsTracker relations)
        {
            if (relations == null || RelationsPawnField == null)
            {
                return null;
            }

            return RelationsPawnField.GetValue(relations) as Pawn;
        }

        internal static bool ShouldHandleNodeController(Pawn? controller, Pawn otherPawn)
        {
            if (controller == null || otherPawn == null || otherPawn == controller)
            {
                return false;
            }

            if (!ModsConfig.BiotechActive)
            {
                return false;
            }

            if (controller.Faction == null || !controller.Faction.IsPlayerSafe())
            {
                return false;
            }

            return OverseerlessMechanitorUtility.IsNode(controller);
        }
    }

    [HarmonyPatch(typeof(MechanitorUtility), nameof(MechanitorUtility.CanControlMech))]
    public static class ControlMechPatches_CanControlMech
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn pawn, Pawn mech, ref AcceptanceReport __result)
        {
            if (pawn != null && mech != null && pawn == mech && OverseerlessMechanitorUtility.IsNode(pawn))
            {
                __result = false;
                if (Prefs.DevMode)
                {
                    Log.Message($"[MMT] Prevented node self-control: pawn={pawn.LabelShort}");
                }

                return false;
            }

            if (pawn == null || mech == null)
            {
                return true;
            }

            if (!ModsConfig.BiotechActive)
            {
                return true;
            }

            if (pawn.Faction == null || !pawn.Faction.IsPlayerSafe())
            {
                return true;
            }

            if (!OverseerlessMechanitorUtility.IsNode(pawn))
            {
                return true;
            }

            OverseerlessMechanitorUtility.EnsureBasicTrackers(pawn);
            OverseerlessMechanitorUtility.ClearExternalOverseerIfNode(pawn);
            return true;
        }
    }

    [HarmonyPatch(typeof(Pawn_RelationsTracker), nameof(Pawn_RelationsTracker.AddDirectRelation))]
    public static class ControlMechPatches_AddDirectRelation
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn_RelationsTracker __instance, PawnRelationDef def, Pawn otherPawn)
        {
            if (def != PawnRelationDefOf.Overseer)
            {
                return;
            }

            Pawn? controller = ControlMechPatches.GetRelationsPawn(__instance);
            if (controller == null || !ControlMechPatches.ShouldHandleNodeController(controller, otherPawn))
            {
                return;
            }

            OverseerlessMechanitorUtility.EnsureBasicTrackers(controller);
            OverseerlessMechanitorUtility.ClearExternalOverseerIfNode(controller);

            if (controller.mechanitor == null)
            {
                return;
            }

            if (controller.mechanitor.GetControlGroup(otherPawn) == null)
            {
                controller.mechanitor.AssignPawnControlGroup(otherPawn);
            }

            controller.mechanitor.Notify_BandwidthChanged();
            controller.mechanitor.Notify_PawnSpawned(true);

            if (Prefs.DevMode)
            {
                Log.Message(
                    $"[MMT] Node controlled mech: controller={controller.LabelShort}, mech={otherPawn.LabelShort}, " +
                    $"overseerOk={(otherPawn.GetOverseer() == controller)}, " +
                    $"controlledCount={controller.mechanitor.ControlledPawns.Count}, " +
                    $"controlGroups={controller.mechanitor.controlGroups.Count}");
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_RelationsTracker), nameof(Pawn_RelationsTracker.TryRemoveDirectRelation))]
    public static class ControlMechPatches_TryRemoveDirectRelation
    {
        [HarmonyPostfix]
        public static void Postfix(
            Pawn_RelationsTracker __instance,
            PawnRelationDef def,
            Pawn otherPawn,
            bool __result)
        {
            if (!__result || def != PawnRelationDefOf.Overseer)
            {
                return;
            }

            Pawn? controller = ControlMechPatches.GetRelationsPawn(__instance);
            if (controller == null || !ControlMechPatches.ShouldHandleNodeController(controller, otherPawn))
            {
                return;
            }

            controller.mechanitor?.UnassignPawnFromAnyControlGroup(otherPawn);
            controller.mechanitor?.Notify_BandwidthChanged();
            controller.mechanitor?.Notify_PawnSpawned(true);

            if (Prefs.DevMode)
            {
                Log.Message(
                    $"[MMT] Node disconnected mech: controller={controller.LabelShort}, mech={otherPawn.LabelShort}");
            }
        }
    }
}
