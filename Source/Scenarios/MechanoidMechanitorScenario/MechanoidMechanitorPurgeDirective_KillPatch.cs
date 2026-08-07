using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class MechanoidMechanitorPurgeDirective_KillPatch
    {
        public struct KillState
        {
            public bool wasAlive;
            public bool wasHumanlike;
            public bool wasPlayerFaction;
        }

        [HarmonyPrefix]
        public static void Prefix(Pawn __instance, out KillState __state)
        {
            __state = default;
            if (__instance == null)
            {
                return;
            }

            __state.wasAlive = !__instance.Dead
                && __instance.health != null
                && !__instance.health.isBeingKilled;
            __state.wasHumanlike =
                __instance.RaceProps != null && __instance.RaceProps.Humanlike;
            __state.wasPlayerFaction = __instance.Faction?.IsPlayer == true;
        }

        [HarmonyPostfix]
        public static void Postfix(Pawn __instance, KillState __state)
        {
            if (!__state.wasAlive || __instance == null || !__instance.Dead)
            {
                return;
            }

            if (!GameComponent_MechanoidMechanitorStoryState.IsPurgeDirectiveActive
                || !__state.wasHumanlike)
            {
                return;
            }

            // 普通剧本允许选择肃清指令，但玩家自己的人类殖民者死亡不应成为肃清额度来源。
            // 专用机械族机械师剧本保持历史行为不变。
            if (GameComponent_MechanoidMechanitorStoryState
                    .IsGeneralScenarioStoryConfiguration
                && __state.wasPlayerFaction)
            {
                return;
            }

            GameComponent_MechanoidMechanitorStoryState.TryAddPurgeDirectiveRewardPoints(
                MechanoidMechanitorPurgeDirectiveRuntimeState.HumanlikeDeathRewardPoints);
        }
    }
}
