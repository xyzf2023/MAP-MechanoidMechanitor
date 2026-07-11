using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(MechanitorUtility), nameof(MechanitorUtility.ShouldBeMechanitor))]
    public static class VanillaRelayMechanitor_ShouldBeMechanitor_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn pawn, ref bool __result)
        {
            if (__result || pawn == null)
            {
                return;
            }

            if (!VanillaRelayMechanitorUtility.IsVanillaRelayMechanitor(pawn))
            {
                return;
            }

            VanillaRelayMechanitorUtility.EnsureVanillaRelayMechanitorState(pawn);
            __result = true;
        }
    }

    [HarmonyPatch(typeof(MechanitorUtility), nameof(MechanitorUtility.IsMechanitor))]
    public static class VanillaRelayMechanitor_IsMechanitor_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn pawn, ref bool __result)
        {
            if (__result || pawn == null)
            {
                return;
            }

            if (!VanillaRelayMechanitorUtility.IsVanillaRelayMechanitor(pawn))
            {
                return;
            }

            VanillaRelayMechanitorUtility.EnsureVanillaRelayMechanitorState(pawn);
            __result = pawn.mechanitor != null;
        }
    }

    [HarmonyPatch(typeof(PawnComponentsUtility), nameof(PawnComponentsUtility.AddAndRemoveDynamicComponents))]
    public static class VanillaRelayMechanitor_AddAndRemoveDynamicComponents_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            VanillaRelayMechanitorUtility.EnsureVanillaRelayMechanitorState(pawn);
        }
    }
}
