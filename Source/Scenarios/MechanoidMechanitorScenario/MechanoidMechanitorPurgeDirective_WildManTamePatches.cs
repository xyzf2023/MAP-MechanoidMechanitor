using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public static class MechanoidMechanitorPurgeDirective_WildManTamePatches
    {
        /// <summary>
        /// UI 层：阻止把血肉智慧野人标记为“驯服”。
        /// 普通动物不受影响。
        /// </summary>
        [HarmonyPatch(
            typeof(Designator_Tame),
            nameof(Designator_Tame.CanDesignateThing))]
        public static class
            MechanoidMechanitorPurgeDirective_DesignatorTame_CanDesignateThing_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(
                Thing t,
                ref AcceptanceReport __result)
            {
                if (!__result.Accepted
                    || !(t is Pawn pawn)
                    || !pawn.IsWildMan()
                    || !MechanoidMechanitorPurgeDirectivePopulationPolicy.RestrictionActive
                    || !MechanoidMechanitorPurgeDirectivePopulationPolicy
                        .IsFleshHumanlike(pawn))
                {
                    return;
                }

                __result =
                    MechanoidMechanitorPurgeDirectivePopulationPolicy.BlockReason;
            }
        }

        /// <summary>
        /// 硬保险：即使绕过 UI 直接 DesignateThing，
        /// 也不允许把血肉智慧野人驯服为玩家人口。
        /// 普通动物仍可正常驯服。
        /// </summary>
        [HarmonyPatch(
            typeof(Designator_Tame),
            nameof(Designator_Tame.DesignateThing))]
        public static class
            MechanoidMechanitorPurgeDirective_DesignatorTame_DesignateThing_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(Thing t)
            {
                if (t is Pawn pawn
                    && pawn.IsWildMan()
                    && MechanoidMechanitorPurgeDirectivePopulationPolicy
                        .WouldAddForbiddenFreeColonist(
                            pawn,
                            Faction.OfPlayerSilentFail))
                {
                    Messages.Message(
                        MechanoidMechanitorPurgeDirectivePopulationPolicy.BlockReason,
                        pawn,
                        MessageTypeDefOf.RejectInput);
                    return false;
                }

                return true;
            }
        }
    }
}
