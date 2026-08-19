using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public static class MechanoidMechanitorPurgeDirective_JoinIncidentPatches
    {
        /// <summary>
        /// WandererJoin 事件生成入口拦截。
        /// 只拦截 pawnKind 为血肉 Humanlike 的事件，机器人加入类事件仍允许。
        /// </summary>
        // CanFireNowSub / TryExecuteWorker 为 protected 重写方法，
        // 不能用 nameof 跨类绑定，改用字符串方法名。
        [HarmonyPatch(typeof(IncidentWorker_WandererJoin), "CanFireNowSub")]
        public static class
            MechanoidMechanitorPurgeDirective_WandererJoin_CanFireNowSub_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(
                IncidentWorker_WandererJoin __instance,
                ref bool __result)
            {
                if (MechanoidMechanitorPurgeDirectivePopulationPolicy.RestrictionActive
                    && MechanoidMechanitorPurgeDirectivePopulationPolicy
                        .IsFleshHumanlike(__instance.def.pawnKind))
                {
                    __result = false;
                    return false;
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(IncidentWorker_WandererJoin), "TryExecuteWorker")]
        public static class
            MechanoidMechanitorPurgeDirective_WandererJoin_TryExecuteWorker_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(
                IncidentWorker_WandererJoin __instance,
                ref bool __result)
            {
                if (MechanoidMechanitorPurgeDirectivePopulationPolicy.RestrictionActive
                    && MechanoidMechanitorPurgeDirectivePopulationPolicy
                        .IsFleshHumanlike(__instance.def.pawnKind))
                {
                    __result = false;
                    return false;
                }

                return true;
            }
        }

        /// <summary>
        /// Game Ended Wanderers 保险：仅在限制生效且起始/可选 Pawn 中
        /// 存在至少一个血肉 Humanlike 时阻止。全是非血肉 Pawn 不阻止。
        /// </summary>
        [HarmonyPatch(typeof(IncidentWorker_GameEndedWanderersJoin), "CanFireNowSub")]
        public static class
            MechanoidMechanitorPurgeDirective_GameEndedWanderers_CanFireNowSub_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(ref bool __result)
            {
                if (MechanoidMechanitorPurgeDirectivePopulationPolicy.RestrictionActive
                    && GameInitDataHasForbiddenFlesh())
                {
                    __result = false;
                    return false;
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(IncidentWorker_GameEndedWanderersJoin), "TryExecuteWorker")]
        public static class
            MechanoidMechanitorPurgeDirective_GameEndedWanderers_TryExecuteWorker_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(ref bool __result)
            {
                if (MechanoidMechanitorPurgeDirectivePopulationPolicy.RestrictionActive
                    && GameInitDataHasForbiddenFlesh())
                {
                    __result = false;
                    return false;
                }

                return true;
            }
        }

        private static bool GameInitDataHasForbiddenFlesh()
        {
            GameInitData? initData = Find.GameInitData;
            if (initData == null)
            {
                return false;
            }

            List<Pawn> pawns = initData.startingAndOptionalPawns;
            if (pawns == null)
            {
                return false;
            }

            for (int i = 0; i < pawns.Count; i++)
            {
                if (MechanoidMechanitorPurgeDirectivePopulationPolicy
                        .IsFleshHumanlike(pawns[i]))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
