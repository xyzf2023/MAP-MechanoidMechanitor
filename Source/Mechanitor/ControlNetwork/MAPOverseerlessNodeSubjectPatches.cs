using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(CompOverseerSubject), nameof(CompOverseerSubject.State), MethodType.Getter)]
    public static class MAPOverseerlessNodeSubjectPatches_State
    {
        [HarmonyPostfix]
        public static void Postfix(CompOverseerSubject __instance, ref OverseerSubjectState __result)
        {
            if (__result == OverseerSubjectState.Overseen)
            {
                return;
            }

            Pawn subject = __instance.Parent;
            if (MAPOverseerlessNodeUtility.IsOverseerlessNodeSubject(subject))
            {
                __result = OverseerSubjectState.Overseen;
            }
        }
    }

    [HarmonyPatch(typeof(CompOverseerSubject), nameof(CompOverseerSubject.CompInspectStringExtra))]
    public static class MAPOverseerlessNodeSubjectPatches_InspectString
    {
        [HarmonyPostfix]
        public static void Postfix(CompOverseerSubject __instance, ref string? __result)
        {
            Pawn subject = __instance.Parent;
            if (subject == null || subject.Faction != Faction.OfPlayer)
            {
                return;
            }

            if (MAPOverseerlessNodeUtility.IsOverseerlessNodeSubject(subject))
            {
                __result = null;
            }
        }
    }
}
