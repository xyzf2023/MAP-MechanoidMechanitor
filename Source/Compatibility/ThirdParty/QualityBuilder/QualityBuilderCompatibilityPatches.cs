using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.QualityBuilder
{
    internal static class QualityBuilderCompatibilityPatches
    {
        internal static bool Enabled;

        private static bool CanApply => Enabled
            && Scribe.mode == LoadSaveMode.Inactive
            && !MechanoidMechanitorPostLoadSafetyCoordinator.ShouldDeferPositiveRestore;

        public static void PawnCanConstructPostfix(Pawn? __0, ref bool __result)
        {
            // 原资格为真时保留；只补充拥有自律授权的玩家机械族。
            if (__result || !CanApply || !AutonomousMechUtility.IsPlayerAutonomousMech(__0))
                return;

            Pawn pawn = __0!;
            WorkTypeDef construction = WorkTypeDefOf.Construction;
            if (construction == null || pawn.workSettings?.Initialized != true
                || !MechWorkTypeAuthorizationUtility.RaceProfileAllowsWorkType(
                    pawn.RaceProps.mechEnabledWorkTypes, construction, pawn)
                || pawn.WorkTypeIsDisabled(construction)
                || !pawn.workSettings.WorkIsActive(construction))
                return;

            // 与上游普通建造者一样检查行动能力，避免失去工作能力的机体抬高最佳技能门槛。
            PawnCapacitiesHandler? capacities = pawn.health?.capacities;
            if (capacities?.CapableOf(PawnCapacityDefOf.Manipulation) == true
                && capacities.CapableOf(PawnCapacityDefOf.Moving))
                __result = true;
        }

        public static void GetPawnConstructionSkillPostfix(Pawn? __0, ref int __result)
        {
            if (!CanApply || __0 == null || __0.Destroyed || __0.Discarded || !__0.IsColonyMech)
                return;

            // 复用原版品质生成等路径的技能解析；普通自律机械体仍沿用种族固定等级。
            __result = MechanoidMechanitorSkillUtility.ResolveMechSkillLevel(
                __result, __0, SkillDefOf.Construction);
        }
    }
}
