using HarmonyLib;
using MAP_MechanoidMechanitor;
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

            if (!MAPOverseerlessNodeUtility.IsOverseerlessNodeSubject(pawn))
            {
                return;
            }

            __result = true;
        }
    }

    [HarmonyPatch(typeof(MechanitorUtility), nameof(MechanitorUtility.CanDraftMech))]
    public static class DraftingPatches_CanDraftMech
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn mech, ref AcceptanceReport __result)
        {
            if (mech == null || !ModsConfig.BiotechActive)
            {
                return true;
            }

            if (mech.Faction == null || !mech.Faction.IsPlayerSafe())
            {
                return true;
            }

            if (!MAPOverseerlessNodeUtility.IsOverseerlessNodeSubject(mech))
            {
                return true;
            }

            if (mech.needs?.energy != null && mech.needs.energy.IsLowEnergySelfShutdown)
            {
                __result = "IsLowEnergySelfShutdown".Translate(mech.Named("PAWN"));
                return false;
            }

            __result = true;
            return false;
        }
    }
}
