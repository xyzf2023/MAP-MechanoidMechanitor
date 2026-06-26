using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class HumanApparelGearTabPatches
    {
        private static readonly PropertyInfo? SelPawnForGearProperty =
            AccessTools.Property(typeof(ITab_Pawn_Gear), "SelPawnForGear");

        [HarmonyPatch(typeof(ITab_Pawn_Gear), "CanControlColonist", MethodType.Getter)]
        public static class Patch_ITab_Pawn_Gear_CanControlColonist
        {
            [HarmonyPostfix]
            public static void Postfix(ITab_Pawn_Gear __instance, ref bool __result)
            {
                if (__result)
                {
                    return;
                }

                Pawn? pawn = SelPawnForGearProperty?.GetValue(__instance) as Pawn;

                if (!GearTabAllowsColonistControl(pawn))
                {
                    return;
                }

                __result = true;
            }
        }

        private static bool GearTabAllowsColonistControl(Pawn? pawn)
        {
            if (pawn == null
                || !HumanApparelUtility.TryGetApparelComp(pawn, out CompHumanApparelUser? comp)
                || !comp!.AllowRemoveApparel)
            {
                return false;
            }

            if (pawn.Faction != Faction.OfPlayer)
            {
                return false;
            }

            if (pawn.Dead || pawn.Downed || pawn.InMentalState)
            {
                return false;
            }

            if (pawn.ParentHolder is Pawn_CarryTracker)
            {
                return false;
            }

            return pawn.apparel != null;
        }
    }
}
