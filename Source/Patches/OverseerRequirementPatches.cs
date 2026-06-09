using HarmonyLib;
using MAP_MechanoidMechanitor;
using RimWorld;
using Verse;

namespace MMT
{
    [HarmonyPatch(typeof(MechanitorUtility), nameof(MechanitorUtility.IsColonyMechRequiringMechanitor))]
    public static class OverseerRequirementPatches
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn mech, ref bool __result)
        {
            if (mech == null || !ModsConfig.BiotechActive)
            {
                return;
            }

            if (mech.Faction == null || !mech.Faction.IsPlayerSafe())
            {
                return;
            }

            if (!MAPMechanitorNodeUtility.HasNode(mech))
            {
                return;
            }

            if (MAPMechanitorNodeUtility.RequiresExternalOverseer(mech))
            {
                return;
            }

            OverseerlessMechanitorUtility.EnsureBasicTrackers(mech);

            if (OverseerlessMechanitorUtility.ShouldClearOwnExternalOverseer(mech))
            {
                OverseerlessMechanitorUtility.ClearExternalOverseerIfNode(mech);
            }

            __result = false;
        }
    }
}
