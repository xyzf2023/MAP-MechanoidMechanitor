using HarmonyLib;
using RimWorld;
using Verse;

namespace MMT
{
    [HarmonyPatch(typeof(Pawn_DraftController), nameof(Pawn_DraftController.ShowDraftGizmo), MethodType.Getter)]
    public static class DraftingPatches_ShowDraftGizmo
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn_DraftController __instance, ref bool __result)
        {
            Pawn pawn = __instance.pawn;
            if (pawn == null || !ModsConfig.BiotechActive)
            {
                return;
            }

            if (pawn.Faction == null || !pawn.Faction.IsPlayerSafe())
            {
                return;
            }

            if (!OverseerlessMechanitorUtility.IsMAPMechanitorNodeController(pawn))
            {
                return;
            }

            OverseerlessMechanitorUtility.EnsureBasicTrackers(pawn);

            if (OverseerlessMechanitorUtility.ShouldClearOwnExternalOverseer(pawn))
            {
                OverseerlessMechanitorUtility.ClearExternalOverseerIfNode(pawn);
            }

            __result = true;
        }
    }

    [HarmonyPatch(typeof(MechanitorUtility), nameof(MechanitorUtility.CanDraftMech))]
    public static class DraftingPatches_CanDraftMech
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn mech, ref AcceptanceReport __result)
        {
            if (mech == null || !ModsConfig.BiotechActive)
            {
                return;
            }

            if (mech.Faction == null || !mech.Faction.IsPlayerSafe())
            {
                return;
            }

            if (!OverseerlessMechanitorUtility.IsMAPMechanitorNodeController(mech))
            {
                return;
            }

            OverseerlessMechanitorUtility.EnsureBasicTrackers(mech);

            if (OverseerlessMechanitorUtility.ShouldClearOwnExternalOverseer(mech))
            {
                OverseerlessMechanitorUtility.ClearExternalOverseerIfNode(mech);
            }

            if (mech.needs?.energy != null && mech.needs.energy.IsLowEnergySelfShutdown)
            {
                __result = "IsLowEnergySelfShutdown".Translate(mech.Named("PAWN"));
                return;
            }

            __result = true;
        }
    }
}
