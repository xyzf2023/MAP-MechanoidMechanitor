using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(MechanitorUtility), nameof(MechanitorUtility.IsColonyMechRequiringMechanitor))]
    public static class MAPOverseerlessNodeRequirementPatches
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

            if (!MAPOverseerlessNodeUtility.IsOverseerlessNodeSubject(mech))
            {
                return;
            }

            __result = false;
        }
    }
}
