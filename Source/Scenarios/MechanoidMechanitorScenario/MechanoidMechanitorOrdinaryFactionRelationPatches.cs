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
                || MechanoidMechanitorOrdinaryFactionRelationApplier.IsApplying)
            {
                return;
            }

            if (!GameComponent_MechanoidMechanitorStoryState.TryGetLockedOrdinaryFactionRelation(
                    __instance,
                    other,
                    out _,
                    out _))
            {
                return;
            }

            __result = false;
        }
    }

    [HarmonyPatch(typeof(Faction), nameof(Faction.GoodwillWith))]
    public static class MechanoidMechanitorOrdinaryFactionRelation_GoodwillWith_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Faction __instance, Faction other, ref int __result)
        {
            if (MechanoidMechanitorOrdinaryFactionRelationApplier.IsApplying)
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
                || MechanoidMechanitorMechHiveRelationApplier.IsApplying
                || MechanoidMechanitorInsectRelationApplier.IsApplying
                || relation?.other == null)
            {
                return;
            }

            if (!GameComponent_MechanoidMechanitorStoryState.TryGetLockedOrdinaryFactionRelation(
                    __instance,
                    relation.other,
                    out Faction ordinary,
                    out int goodwill,
                    out FactionRelationKind relationKind))
            {
                return;
            }

            MechanoidMechanitorOrdinaryFactionRelationApplier.ApplyExactPlayerRelation(
                ordinary,
                goodwill,
                relationKind);
        }
    }

    [HarmonyPatch(typeof(FactionManager), "Remove")]
    public static class MechanoidMechanitorOrdinaryFactionRelation_FactionManagerRemove_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(FactionManager __instance, Faction faction)
        {
            // 原版只允许删除临时派系；失败时不刷新，成功后仅重建派生缓存，不重施关系。
            if (faction != null && !__instance.AllFactionsListForReading.Contains(faction))
            {
                Current.Game?.GetComponent<GameComponent_MechanoidMechanitorStoryState>()
                    ?.RebuildRuntimeCaches();
            }
        }
    }

    [HarmonyPatch(typeof(FactionManager), nameof(FactionManager.Add))]
    public static class MechanoidMechanitorOrdinaryFactionRelation_FactionManagerAdd_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Faction faction)
        {
            if (faction == null
                || MechanoidMechanitorOrdinaryFactionRelationApplier.IsApplying
                || MechanoidMechanitorMechHiveRelationApplier.IsApplying
                || MechanoidMechanitorInsectRelationApplier.IsApplying
                || Current.Game == null)
            {
                return;
            }

            GameComponent_MechanoidMechanitorStoryState? storyState =
                Current.Game.GetComponent<GameComponent_MechanoidMechanitorStoryState>();
            if (storyState == null)
            {
                return;
            }

            storyState.NotifyFactionAdded(faction);
            MechanoidMechanitorOrdinaryFactionRelationApplier
                .ApplyPolicyToNewOrdinaryFaction(faction);
            MechanoidMechanitorMechHiveRelationApplier.ApplyPolicyToNewMechHive(faction);
            MechanoidMechanitorInsectRelationApplier.ApplyPolicyToNewInsectFaction(faction);
        }
    }
}

