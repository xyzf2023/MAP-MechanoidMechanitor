using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    [HarmonyPatch(typeof(Faction), nameof(Faction.CanChangeGoodwillFor))]
    public static class MechanoidMechanitorOrdinaryFactionRelation_CanChangeGoodwillFor_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Faction __instance, Faction other, ref bool __result)
        {
            if (!__result
                || MechanoidMechanitorOrdinaryFactionRelationApplier.IsApplying
                || !GameComponent_MechanoidMechanitorStoryState
                    .HasAppliedInitialOrdinaryFactionRelations
                || !GameComponent_MechanoidMechanitorScenarioState.IsEnabled)
            {
                return;
            }

            if (!GameComponent_MechanoidMechanitorStoryState
                    .TryGetEffectiveOrdinaryFactionRelationOption(
                        __instance,
                        other,
                        out MechanoidMechanitorFactionRelationOption option))
            {
                return;
            }

            if (MechanoidMechanitorOrdinaryFactionRelationPolicy.IsLockedOption(option))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(Faction), nameof(Faction.GoodwillWith))]
    public static class MechanoidMechanitorOrdinaryFactionRelation_GoodwillWith_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Faction __instance, Faction other, ref int __result)
        {
            if (MechanoidMechanitorOrdinaryFactionRelationApplier.IsApplying
                || !GameComponent_MechanoidMechanitorStoryState
                    .HasAppliedInitialOrdinaryFactionRelations
                || !GameComponent_MechanoidMechanitorScenarioState.IsEnabled)
            {
                return;
            }

            if (!GameComponent_MechanoidMechanitorStoryState.TryGetLockedOrdinaryFactionRelation(
                    __instance,
                    other,
                    out int goodwill,
                    out _))
            {
                return;
            }

            __result = goodwill;
        }
    }

    [HarmonyPatch(typeof(Faction), nameof(Faction.SetRelation))]
    public static class MechanoidMechanitorOrdinaryFactionRelation_SetRelation_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Faction __instance, FactionRelation relation)
        {
            if (MechanoidMechanitorOrdinaryFactionRelationApplier.IsApplying
                || relation?.other == null
                || !GameComponent_MechanoidMechanitorStoryState
                    .HasAppliedInitialOrdinaryFactionRelations
                || !GameComponent_MechanoidMechanitorScenarioState.IsEnabled)
            {
                return;
            }

            if (!GameComponent_MechanoidMechanitorStoryState.TryGetLockedOrdinaryFactionRelation(
                    __instance,
                    relation.other,
                    out int goodwill,
                    out FactionRelationKind relationKind))
            {
                return;
            }

            if (!MechanoidMechanitorOrdinaryFactionUtility.TryGetPlayerAndOrdinary(
                    __instance,
                    relation.other,
                    out _,
                    out Faction ordinary))
            {
                return;
            }

            MechanoidMechanitorOrdinaryFactionRelationApplier.ApplyExactPlayerRelation(
                ordinary,
                goodwill,
                relationKind);
        }
    }

    [HarmonyPatch(typeof(FactionManager), nameof(FactionManager.Add))]
    public static class MechanoidMechanitorOrdinaryFactionRelation_FactionManagerAdd_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Faction faction)
        {
            if (MechanoidMechanitorOrdinaryFactionRelationApplier.IsApplying)
            {
                return;
            }

            MechanoidMechanitorOrdinaryFactionRelationApplier
                .ApplyPolicyToNewOrdinaryFaction(faction);
        }
    }
}
