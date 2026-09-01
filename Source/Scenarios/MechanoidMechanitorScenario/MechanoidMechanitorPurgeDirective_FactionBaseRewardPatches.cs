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

            // 据点被摧毁后立即结算肃清任务（不依赖世界对象稍后被其他任务销毁）。
            PurgeDirectiveQuestPart.NotifyActiveQuestTargetDefeated(factionBase);
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
                || !AllEnemiesDefeatedSignalSent(__instance)
                || __instance.Faction == null
                || __instance.Faction.IsPlayer)
            {
                return;
            }

            // 只处理合法肃清目标（工作站 / 前哨）；玩家、机械巢或非法据点不结算。
            if (PurgeDirectiveQuestTargetUtility.ClassifyTargetType(__instance)
                == PurgeDirectiveTargetType.Invalid)
            {
                return;
            }

            GameComponent_MechanoidMechanitorStoryState.TryAddPurgeDirectiveWorldTargetBaseReward(
                __instance,
                MechanoidMechanitorPurgeDirectiveRuntimeState
                    .OtherFactionBaseDestroyedRewardPoints);

            // 玩家击败守军后立即结算工作站 / 前哨肃清任务（不依赖世界对象稍后被其他任务销毁）。
            PurgeDirectiveQuestPart.NotifyActiveQuestTargetDefeated(__instance);
        }
    }
}
