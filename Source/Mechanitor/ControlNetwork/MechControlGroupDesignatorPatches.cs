using HarmonyLib;
using MAP_MechanoidMechanitor;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(Designator_MechControlGroup), nameof(Designator_MechControlGroup.CanDesignateThing))]
    public static class Designator_MechControlGroup_CanDesignateThing_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Thing t, ref AcceptanceReport __result)
        {
            if (t is Pawn pawn
                && ModsConfig.BiotechActive
                && AutonomousMechUtility.IsAutonomousMech(pawn))
            {
                __result = false;
                return false;
            }

            return true;
        }
    }
}
