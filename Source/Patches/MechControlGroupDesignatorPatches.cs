using HarmonyLib;
using MAP_MechanoidMechanitor;
using RimWorld;
using Verse;

namespace MMT
{
    [HarmonyPatch(typeof(Designator_MechControlGroup), nameof(Designator_MechControlGroup.CanDesignateThing))]
    public static class Designator_MechControlGroup_CanDesignateThing_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Thing t, ref AcceptanceReport __result)
        {
            if (t is Pawn pawn
                && ModsConfig.BiotechActive
                && MAPOverseerlessNodeUtility.IsOverseerlessNodeSubject(pawn))
            {
                __result = false;
                return false;
            }

            return true;
        }
    }
}
