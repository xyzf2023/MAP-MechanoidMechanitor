using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    [HarmonyPatch(typeof(Faction), nameof(Faction.SetRelationDirect))]
    public static class MechanoidMechanitorInsectRelation_SetRelationDirect_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Faction __instance, Faction other)
        {
            if (MechanoidMechanitorInsectRelationApplier.IsApplying)
            {
                return true;
            }

            if (!GameComponent_MechanoidMechanitorStoryState
                .TryGetLockedInsectRelation(
                    __instance,
                    other,
                    out _,
                    out _))
            {
                return true;
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(Faction), nameof(Faction.SetRelation))]
    public static class MechanoidMechanitorInsectRelation_SetRelation_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(
            Faction __instance,
            FactionRelation relation)
        {
            if (MechanoidMechanitorInsectRelationApplier.IsApplying
                || MechanoidMechanitorMechHiveRelationApplier.IsApplying
                || MechanoidMechanitorOrdinaryFactionRelationApplier.IsApplying
                || relation?.other == null)
            {
                return;
            }

            if (!GameComponent_MechanoidMechanitorStoryState
                .TryGetLockedInsectRelation(
                    __instance,
                    relation.other,
                    out Faction insectFaction,
                    out FactionRelationKind relationKind))
            {
                return;
            }

            MechanoidMechanitorInsectRelationApplier.ApplyExactInsectRelation(
                insectFaction,
                relationKind,
                hostileOnHarmByPlayer: false);
        }
    }

    [HarmonyPatch(typeof(FactionDef), nameof(FactionDef.PermanentlyHostileTo))]
    public static class MechanoidMechanitorInsectRelation_PermanentlyHostileTo_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(
            FactionDef __instance,
            FactionDef otherFactionDef,
            ref bool __result)
        {
            if (!__result)
            {
                return;
            }

            if (!GameComponent_MechanoidMechanitorStoryState
                .ShouldSuppressInsectPermanentHostility(
                    __instance,
                    otherFactionDef))
            {
                return;
            }

            __result = false;
        }
    }
}
