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

            if (otherPawn != null
                && MAPMechanitorNodeUtility.HasNode(otherPawn)
                && !MAPMechanitorNodeUtility.RequiresExternalOverseer(otherPawn))
            {
                if (Prefs.DevMode)
                {
                    Log.Warning(
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
                }
            }

            return true;
        }
    }
}
