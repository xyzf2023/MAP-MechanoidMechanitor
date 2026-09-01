using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    [HarmonyPatch(typeof(SettlementDefeatUtility), nameof(SettlementDefeatUtility.CheckDefeated))]
    public static class MechanoidMechanitorPurgeDirective_SettlementDefeatedPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Settlement factionBase, ref bool __state)
        {
            __state = factionBase != null
                && !factionBase.Destroyed
                && factionBase.Faction != null
                && !factionBase.Faction.IsPlayer;
        }

        [HarmonyPostfix]
        public static void Postfix(Settlement factionBase, bool __state)
        {
            if (!__state || factionBase == null || !factionBase.Destroyed)
            {
                return;
            }

            GameComponent_MechanoidMechanitorStoryState.TryAddPurgeDirectiveWorldTargetBaseReward(
                factionBase,
                MechanoidMechanitorPurgeDirectiveRuntimeState
                    .OtherFactionBaseDestroyedRewardPoints);
        }
    }

    [HarmonyPatch(typeof(Site), "CheckAllEnemiesDefeated")]
    public static class MechanoidMechanitorPurgeDirective_WorkSiteDefeatedPatch
    {
        private static readonly AccessTools.FieldRef<Site, bool> AllEnemiesDefeatedSignalSent =
            AccessTools.FieldRefAccess<Site, bool>("allEnemiesDefeatedSignalSent");

        [HarmonyPrefix]
        public static void Prefix(Site __instance, ref bool __state)
        {
            __state = __instance != null && !AllEnemiesDefeatedSignalSent(__instance);
        }

        [HarmonyPostfix]
        public static void Postfix(Site __instance, bool __state)
        {
            if (!__state
                || __instance == null
                || !AllEnemiesDefeatedSignalSent(__instance))
            {
                return;
            }

            if (__instance.Faction == null || __instance.Faction.IsPlayer)
            {
                return;
            }

            if (__instance.parts == null || __instance.parts.Count == 0)
            {
                return;
            }

            SitePartDef mainSitePartDef = __instance.MainSitePartDef;
            if (mainSitePartDef == null
                || mainSitePartDef.tags == null
                || !mainSitePartDef.tags.Contains("WorkSite"))
            {
                return;
            }

            GameComponent_MechanoidMechanitorStoryState.TryAddPurgeDirectiveWorldTargetBaseReward(
                __instance,
                MechanoidMechanitorPurgeDirectiveRuntimeState
                    .OtherFactionBaseDestroyedRewardPoints);
        }
    }
}
