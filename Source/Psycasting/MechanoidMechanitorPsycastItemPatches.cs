using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(
        typeof(ColonistLikeCompUsableUtility),
        nameof(ColonistLikeCompUsableUtility.IsAuthorizedMechanicalCompUsableUser))]
    public static class Patch_ColonistLikeCompUsableUtility_IsAuthorized_Psycasting
    {
        [HarmonyPostfix]
        public static void Postfix(
            Pawn? pawn,
            CompUsable? usable,
            ref bool __result)
        {
            if (!__result
                && usable != null
                && MechanoidMechanitorPsycastUtility.CanUsePsytrainer(
                    pawn,
                    usable.parent))
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch(
        typeof(CompUseEffect_GainAbility),
        nameof(CompUseEffect_GainAbility.CanBeUsedBy))]
    public static class Patch_CompUseEffect_GainAbility_CanBeUsedBy_Psycasting
    {
        [HarmonyPrefix]
        public static void Prefix(
            CompUseEffect_GainAbility __instance,
            Pawn p)
        {
            if (__instance.Props.ability?.IsPsycast == true)
            {
                MechanoidMechanitorPsycastUtility.EnsurePsycastInfrastructure(p);
            }
        }
    }

    [HarmonyPatch(
        typeof(CompUseEffect_GainAbility),
        nameof(CompUseEffect_GainAbility.DoEffect))]
    public static class Patch_CompUseEffect_GainAbility_DoEffect_Psycasting
    {
        [HarmonyPrefix]
        public static void Prefix(
            CompUseEffect_GainAbility __instance,
            Pawn user)
        {
            if (__instance.Props.ability?.IsPsycast == true)
            {
                MechanoidMechanitorPsycastUtility.EnsurePsycastInfrastructure(user);
            }
        }
    }
}
