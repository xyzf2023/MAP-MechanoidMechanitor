using HarmonyLib;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(Bill), nameof(Bill.PawnAllowedToStartAnew))]
    public static class MechanoidMechanitorScenarioBillWorkerRestrictionPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Bill __instance, Pawn p, ref bool __result)
        {
            if (!__result)
            {
                return;
            }

            if (!__instance.MechsOnly)
            {
                return;
            }

            if (!GameComponent_MechanoidMechanitorScenarioState.IsEnabled)
            {
                return;
            }

            if (!MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(p))
            {
                return;
            }

            __result = false;
        }
    }
}
