using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    internal static class MechanoidMechanitorSkillPatchIL
    {
        internal const string LogPrefix = "[MAP-机械族机械师] SkillRecord优先补丁：";

        internal static FieldInfo? MechFixedSkillLevelField =>
            AccessTools.Field(typeof(RaceProperties), nameof(RaceProperties.mechFixedSkillLevel));

        internal static MethodInfo? ResolveMechSkillLevelMethod =>
            AccessTools.Method(
                typeof(MechanoidMechanitorSkillUtility),
                nameof(MechanoidMechanitorSkillUtility.ResolveMechSkillLevel));

        internal static List<int> FindMechFixedSkillLoads(
            List<CodeInstruction> codes,
            FieldInfo mechFixedSkillLevelField)
        {
            List<int> matches = new List<int>();
            for (int i = 0; i < codes.Count; i++)
            {
                CodeInstruction instruction = codes[i];
                if (instruction.opcode == OpCodes.Ldfld
                    && instruction.operand is FieldInfo field
                    && field.Equals(mechFixedSkillLevelField))
                {
                    matches.Add(i);
                }
            }

            return matches;
        }

        internal static void InsertResolver(
            List<CodeInstruction> codes,
            int fieldLoadIndex,
            CodeInstruction loadPawn,
            CodeInstruction loadSkill,
            MethodInfo resolver)
        {
            codes.Insert(fieldLoadIndex + 1, loadPawn);
            codes.Insert(fieldLoadIndex + 2, loadSkill);
            codes.Insert(fieldLoadIndex + 3, new CodeInstruction(OpCodes.Call, resolver));
        }
    }

    [HarmonyPatch(
        typeof(QualityUtility),
        nameof(QualityUtility.GenerateQualityCreatedByPawn),
        new Type[] { typeof(Pawn), typeof(SkillDef), typeof(bool) })]
    public static class MechanoidMechanitorQualitySkillRecordPatch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            FieldInfo? mechSkillField = MechanoidMechanitorSkillPatchIL.MechFixedSkillLevelField;
            MethodInfo? resolver = MechanoidMechanitorSkillPatchIL.ResolveMechSkillLevelMethod;
            if (mechSkillField == null || resolver == null)
            {
                Log.Error($"{MechanoidMechanitorSkillPatchIL.LogPrefix}品质补丁缺少目标字段或解析方法，保持原版逻辑。");
                return codes;
            }

            List<int> matches =
                MechanoidMechanitorSkillPatchIL.FindMechFixedSkillLoads(codes, mechSkillField);
            if (matches.Count != 1)
            {
                Log.Error(
                    $"{MechanoidMechanitorSkillPatchIL.LogPrefix}QualityUtility.GenerateQualityCreatedByPawn 中 mechFixedSkillLevel 预期 1 处，实际 {matches.Count} 处，保持原版逻辑。");
                return codes;
            }

            MechanoidMechanitorSkillPatchIL.InsertResolver(
                codes,
                matches[0],
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldarg_1),
                resolver);
            return codes;
        }
    }

    [HarmonyPatch(typeof(SkillRequirement), nameof(SkillRequirement.PawnSatisfies))]
    public static class MechanoidMechanitorSkillRequirementSkillRecordPatch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            FieldInfo? mechSkillField = MechanoidMechanitorSkillPatchIL.MechFixedSkillLevelField;
            FieldInfo? requirementSkillField =
                AccessTools.Field(typeof(SkillRequirement), nameof(SkillRequirement.skill));
            MethodInfo? resolver = MechanoidMechanitorSkillPatchIL.ResolveMechSkillLevelMethod;
            if (mechSkillField == null || requirementSkillField == null || resolver == null)
            {
                Log.Error($"{MechanoidMechanitorSkillPatchIL.LogPrefix}SkillRequirement 补丁缺少目标成员，保持原版逻辑。");
                return codes;
            }

            List<int> matches =
                MechanoidMechanitorSkillPatchIL.FindMechFixedSkillLoads(codes, mechSkillField);
            if (matches.Count != 1)
            {
                Log.Error(
                    $"{MechanoidMechanitorSkillPatchIL.LogPrefix}SkillRequirement.PawnSatisfies 中 mechFixedSkillLevel 预期 1 处，实际 {matches.Count} 处，保持原版逻辑。");
                return codes;
            }

            int index = matches[0];
            codes.Insert(index + 1, new CodeInstruction(OpCodes.Ldarg_1));
            codes.Insert(index + 2, new CodeInstruction(OpCodes.Ldarg_0));
            codes.Insert(index + 3, new CodeInstruction(OpCodes.Ldfld, requirementSkillField));
            codes.Insert(index + 4, new CodeInstruction(OpCodes.Call, resolver));
            return codes;
        }
    }

    [HarmonyPatch(
        typeof(GenConstruct),
        nameof(GenConstruct.CanConstruct),
        new Type[] { typeof(Thing), typeof(Pawn), typeof(bool), typeof(bool), typeof(JobDef) })]
    public static class MechanoidMechanitorConstructionSkillRecordPatch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            FieldInfo? mechSkillField = MechanoidMechanitorSkillPatchIL.MechFixedSkillLevelField;
            FieldInfo? constructionSkillField =
                AccessTools.Field(typeof(SkillDefOf), nameof(SkillDefOf.Construction));
            FieldInfo? artisticSkillField =
                AccessTools.Field(typeof(SkillDefOf), nameof(SkillDefOf.Artistic));
            MethodInfo? resolver = MechanoidMechanitorSkillPatchIL.ResolveMechSkillLevelMethod;
            if (mechSkillField == null
                || constructionSkillField == null
                || artisticSkillField == null
                || resolver == null)
            {
                Log.Error($"{MechanoidMechanitorSkillPatchIL.LogPrefix}建造补丁缺少目标成员，保持原版逻辑。");
                return codes;
            }

            List<int> matches =
                MechanoidMechanitorSkillPatchIL.FindMechFixedSkillLoads(codes, mechSkillField);
            if (matches.Count != 2)
            {
                Log.Error(
                    $"{MechanoidMechanitorSkillPatchIL.LogPrefix}GenConstruct.CanConstruct 中 mechFixedSkillLevel 预期 2 处，实际 {matches.Count} 处，保持原版逻辑。");
                return codes;
            }

            // 原版顺序固定为 Construction 检查后 Artistic 检查。
            // 从后向前插入，避免第一次插入改变前一个匹配位置。
            MechanoidMechanitorSkillPatchIL.InsertResolver(
                codes,
                matches[1],
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Ldsfld, artisticSkillField),
                resolver);
            MechanoidMechanitorSkillPatchIL.InsertResolver(
                codes,
                matches[0],
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Ldsfld, constructionSkillField),
                resolver);
            return codes;
        }
    }

    [HarmonyPatch(
        typeof(WorkGiver_GrowerSow),
        nameof(WorkGiver_GrowerSow.JobOnCell),
        new Type[] { typeof(Pawn), typeof(IntVec3), typeof(bool) })]
    public static class MechanoidMechanitorSowingSkillRecordPatch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            FieldInfo? mechSkillField = MechanoidMechanitorSkillPatchIL.MechFixedSkillLevelField;
            FieldInfo? plantsSkillField =
                AccessTools.Field(typeof(SkillDefOf), nameof(SkillDefOf.Plants));
            MethodInfo? resolver = MechanoidMechanitorSkillPatchIL.ResolveMechSkillLevelMethod;
            if (mechSkillField == null || plantsSkillField == null || resolver == null)
            {
                Log.Error($"{MechanoidMechanitorSkillPatchIL.LogPrefix}播种补丁缺少目标成员，保持原版逻辑。");
                return codes;
            }

            List<int> matches =
                MechanoidMechanitorSkillPatchIL.FindMechFixedSkillLoads(codes, mechSkillField);
            if (matches.Count != 1)
            {
                Log.Error(
                    $"{MechanoidMechanitorSkillPatchIL.LogPrefix}WorkGiver_GrowerSow.JobOnCell 中 mechFixedSkillLevel 预期 1 处，实际 {matches.Count} 处，保持原版逻辑。");
                return codes;
            }

            MechanoidMechanitorSkillPatchIL.InsertResolver(
                codes,
                matches[0],
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Ldsfld, plantsSkillField),
                resolver);
            return codes;
        }
    }

    [HarmonyPatch(typeof(MechanitorUtility), nameof(MechanitorUtility.AnyPlayerMechCanDoWork))]
    public static class MechanoidMechanitorAnyPlayerMechCanDoWorkPatch
    {
        [HarmonyPostfix]
        public static void Postfix(
            WorkTypeDef workType,
            int skillRequired,
            ref Pawn pawn,
            ref bool __result)
        {
            Pawn? originalPawn = pawn;
            bool originalResult = __result;

            // 原版候选不是机械族机械师时，原版正结果可直接复用。
            if (originalResult
                && originalPawn != null
                && !MechanoidMechanitorCapabilityUtility.HasCapability(originalPawn, MechanoidMechanitorCapability.IndividualSkills))
            {
                return;
            }

            if (!ModsConfig.BiotechActive || Find.CurrentMap == null)
            {
                // 环境不完整时不无条件破坏原版结果。
                return;
            }

            // 原版候选是机械族机械师：必须用本 MOD 真实技能规则重新验证。
            if (originalResult
                && originalPawn != null
                && CanCandidateDoWork(originalPawn, workType, skillRequired))
            {
                return;
            }

            // 只有需要补充搜索时，才清空原版结果并扫描玩家派系 Pawn。
            pawn = null!;
            __result = false;

            List<Pawn> pawns =
                Find.CurrentMap.mapPawns.PawnsInFaction(Faction.OfPlayer);
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn candidate = pawns[i];
                if (CanCandidateDoWork(candidate, workType, skillRequired))
                {
                    pawn = candidate;
                    __result = true;
                    return;
                }
            }
        }

        private static bool CanCandidateDoWork(
            Pawn candidate,
            WorkTypeDef workType,
            int skillRequired)
        {
            if (MechanoidMechanitorCapabilityUtility.HasCapability(candidate, MechanoidMechanitorCapability.IndividualSkills))
            {
                if (!MechanoidMechanitorCapabilityUtility.HasCapability(candidate, MechanoidMechanitorCapability.GeneralMechWork)
                    && (!candidate.IsColonyMechPlayerControlled
                        || candidate.RaceProps.mechEnabledWorkTypes?.Contains(workType) != true))
                    return false;

                // 机械族机械师的可用工作类型可能来自升格后的动态授权，
                // 不能再以升格前种族的 mechEnabledWorkTypes 提前排除。
                if (candidate.WorkTypeIsDisabled(workType))
                {
                    return false;
                }

                if (ProductivityCoreUtility.HasActiveEffect(candidate))
                {
                    return true;
                }

                if (MechanoidMechanitorSkillUtility.TryGetPreferredWorkTypeSkillLevel(
                        candidate,
                        workType,
                        out int preferredLevel))
                {
                    // 已存在真实 SkillRecord 时必须使用真实技能等级；
                    // 真实技能低于要求时判为失败，不得被 mechFixedSkillLevel 覆盖。
                    return preferredLevel >= skillRequired;
                }

                // 只有真实技能基础设施缺失时才退回机械族固定技能。
                return candidate.RaceProps.mechFixedSkillLevel >= skillRequired;
            }

            // 普通机械体仍严格遵循原版种族工作表。
            if (!candidate.RaceProps.mechEnabledWorkTypes.Contains(workType))
            {
                return false;
            }

            return candidate.IsColonyMech
                && candidate.GetOverseer() != null
                && candidate.RaceProps.mechFixedSkillLevel >= skillRequired;
        }
    }

    [HarmonyPatch(typeof(Alert_NeedMiner), nameof(Alert_NeedMiner.GetReport))]
    public static class MechanoidMechanitorNeedMinerSkillRecordPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref AlertReport __result)
        {
            List<Designation> designations = new List<Designation>();
            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];
                if (!map.IsPlayerHome)
                {
                    continue;
                }

                designations.Clear();
                designations.AddRange(
                    map.designationManager.designationsByDef[DesignationDefOf.Mine]);
                designations.AddRange(
                    map.designationManager.designationsByDef[DesignationDefOf.MineVein]);
                if (designations.NullOrEmpty())
                {
                    continue;
                }

                bool hasMiner = false;
                List<Pawn> pawns = map.mapPawns.PawnsInFaction(Faction.OfPlayer);
                for (int j = 0; j < pawns.Count; j++)
                {
                    Pawn candidate = pawns[j];
                    if ((!candidate.Spawned && !candidate.BrieflyDespawned())
                        || candidate.Downed)
                    {
                        continue;
                    }

                    if (candidate.IsFreeColonist
                        && candidate.workSettings != null
                        && candidate.workSettings.GetPriority(WorkTypeDefOf.Mining) > 0)
                    {
                        hasMiner = true;
                        break;
                    }

                    if (MechanoidMechanitorCapabilityUtility.HasCapability(candidate, MechanoidMechanitorCapability.GeneralMechWork))
                    {
                        if (MechanoidMechanitorWorkAlertUtility
                                .IsAvailableMechanitorForWork(
                                    candidate,
                                    map,
                                    WorkTypeDefOf.Mining,
                                    SkillDefOf.Mining,
                                    minimumSkill: 1))
                        {
                            hasMiner = true;
                            break;
                        }

                        continue;
                    }

                    // 普通机械体仍严格遵循原版种族工作表和固定机械技能。
                    if (candidate.IsColonyMechPlayerControlled
                        && candidate.RaceProps.mechEnabledWorkTypes.Contains(
                            WorkTypeDefOf.Mining)
                        && candidate.RaceProps.mechFixedSkillLevel > 0)
                    {
                        hasMiner = true;
                        break;
                    }
                }

                if (!hasMiner)
                {
                    __result = AlertReport.CulpritIs(
                        new GlobalTargetInfo(designations[0].target.Cell, map));
                    return;
                }
            }

            __result = false;
        }
    }
}
