using HarmonyLib;
using RimWorld;
using Verse;

namespace MMT
{
    [HarmonyPatch(typeof(CompOverseerSubject), nameof(CompOverseerSubject.State), MethodType.Getter)]
    public static class ShadowOverseerSubjectPatches_State
    {
        [HarmonyPostfix]
        public static void Postfix(CompOverseerSubject __instance, ref OverseerSubjectState __result)
        {
            if (__result == OverseerSubjectState.Overseen)
            {
                return;
            }

            Pawn subject = __instance.Parent;
            if (subject == null)
            {
                return;
            }

            if (OverseerlessMechanitorUtility.IsNode(subject))
            {
                __result = OverseerSubjectState.Overseen;
                return;
            }

            Pawn? controller = MMT_ShadowOverseerManager.Current?.GetShadowOverseer(subject);
            if (controller != null && controller.mechanitor != null)
            {
                __result = OverseerSubjectState.Overseen;
            }
        }
    }

    [HarmonyPatch(typeof(CompOverseerSubject), nameof(CompOverseerSubject.CompInspectStringExtra))]
    public static class ShadowOverseerSubjectPatches_InspectString
    {
        [HarmonyPostfix]
        public static void Postfix(CompOverseerSubject __instance, ref string? __result)
        {
            Pawn subject = __instance.Parent;
            if (subject == null || subject.Faction != Faction.OfPlayer)
            {
                return;
            }

            if (OverseerlessMechanitorUtility.IsNode(subject))
            {
                __result = null;
                return;
            }

            Pawn? controller = MMT_ShadowOverseerManager.Current?.GetShadowOverseer(subject);
            if (controller == null)
            {
                return;
            }

            __result = "Overseer".Translate() + ": " + controller.LabelShort;
        }
    }
}
