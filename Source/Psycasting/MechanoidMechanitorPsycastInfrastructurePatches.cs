using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(
        typeof(PawnComponentsUtility),
        nameof(PawnComponentsUtility.CreateInitialComponents))]
    public static class Patch_PawnComponentsUtility_CreateInitialComponents_Psycasting
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn pawn)
        {
            MechanoidMechanitorPsycastUtility.EnsurePsycastInfrastructure(pawn);
        }
    }

    [HarmonyPatch(
        typeof(PawnComponentsUtility),
        nameof(PawnComponentsUtility.AddComponentsForSpawn))]
    public static class Patch_PawnComponentsUtility_AddComponentsForSpawn_Psycasting
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn pawn)
        {
            MechanoidMechanitorPsycastUtility.EnsurePsycastInfrastructure(pawn);
        }
    }

    [HarmonyPatch(
        typeof(MechanoidMechanitorRoleUtility),
        nameof(MechanoidMechanitorRoleUtility.EnsureRoleState))]
    public static class Patch_MechanoidMechanitorRoleUtility_EnsureRoleState_Psycasting
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn? pawn)
        {
            MechanoidMechanitorPsycastUtility.EnsurePsycastInfrastructure(pawn);
        }
    }

    [HarmonyPatch(typeof(Hediff_Psylink), nameof(Hediff_Psylink.PostAdd))]
    public static class Patch_Hediff_Psylink_PostAdd_MechanoidMechanitor
    {
        [HarmonyPrefix]
        public static void Prefix(Hediff_Psylink __instance)
        {
            MechanoidMechanitorPsycastUtility.EnsurePsycastInfrastructure(
                __instance.pawn);
        }
    }
}
