using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    [HarmonyPatch(typeof(Faction), nameof(Faction.SetRelationDirect))]
    public static class MechanoidMechanitorMechHiveRelation_SetRelationDirect_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Faction __instance, Faction other)
        {
            if (MechanoidMechanitorMechHiveRelationApplier.IsApplying)
            {
                return true;
            }

            if (!GameComponent_MechanoidMechanitorStoryState.TryGetLockedMechHiveRelation(
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
    public static class MechanoidMechanitorMechHiveRelation_SetRelation_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Faction __instance, FactionRelation relation)
        {
            if (MechanoidMechanitorMechHiveRelationApplier.IsApplying
                || MechanoidMechanitorOrdinaryFactionRelationApplier.IsApplying
                || MechanoidMechanitorInsectRelationApplier.IsApplying
                || relation?.other == null)
            {
                return;
            }

            if (!GameComponent_MechanoidMechanitorStoryState.TryGetLockedMechHiveRelation(
                    __instance,
                    relation.other,
                    out Faction mechHive,
                    out FactionRelationKind relationKind))
            {
                return;
            }

            MechanoidMechanitorMechHiveRelationApplier.ApplyExactMechHiveRelation(
                mechHive,
                relationKind,
                hostileOnHarmByPlayer: false);
        }
    }
}
