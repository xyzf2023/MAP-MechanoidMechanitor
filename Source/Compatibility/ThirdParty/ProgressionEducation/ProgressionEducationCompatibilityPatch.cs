using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.ProgressionEducation
{
    /// <summary>
    /// Progression: Education 兼容补丁。
    /// 所有 Education 反射目标只在本模块初始化时由 ProgressionEducationCompatibility.Apply
    /// 解析并一次性 Configure，运行时热路径绝不重新反射。
    /// 本文件不引用任何 ProgressionEducation 程序集类型，仅使用 Verse / RimWorld / System 类型。
    /// </summary>
    internal static class ProgressionEducationCompatibilityPatch
    {
        private const string LogPrefix =
            "[MAP-机械族机械师] Progression: Education 兼容：";

        private const int WarningKeyNotConfigured = unchecked((int)0x5045_0001);
        private const int ErrorKeyConfigureNull = unchecked((int)0x5045_0002);
        private const int ErrorKeyCandidatePoolInvoke = unchecked((int)0x5045_0003);
        private const int ErrorKeyCandidatePoolCandidates = unchecked((int)0x5045_0004);
        private const int ErrorKeyAssignmentsField = unchecked((int)0x5045_0005);
        private const int ErrorKeyAssignmentsCandidates = unchecked((int)0x5045_0006);
        private const int ErrorKeyTeacherRead = unchecked((int)0x5045_0007);

        // MAP 主动施加拒绝时统一使用的 AcceptanceReport Reason 来源。
        // 禁止散装字符串；所有由本兼容层覆盖为 Reject 的路径都必须提供非空 Reason，
        // 否则 Progression: Education 的 CanParticipate 可能因 Reason 为空而误判 Pawn 整体可参与。
        private const string ReasonStudentRole =
            "该机械族仅被允许作为课堂教师，不能作为学生。";
        private const string ReasonMissingTeachingInfrastructure =
            "该机械族缺少课堂教学所需的作息基础设施。";
        private const string ReasonUnsupportedClassType =
            "该机械族当前仅支持普通技能课程和托儿课程教学。";
        private const string ReasonCompatibilityReflectionFailure =
            "该机械族课堂兼容层读取课程信息失败，暂时无法担任该角色。";

        private static AcceptanceReport RejectWithReason(string reason)
        {
            return new AcceptanceReport(reason);
        }

        private static MethodInfo? addPawnMethod;
        private static FieldInfo? allPawnsField;
        private static FieldInfo? studyGroupField;
        private static FieldInfo? subjectLogicField;
        private static Type? skillClassLogicType;
        private static Type? daycareClassLogicType;
        private static bool configured;

        /// <summary>
        /// 一次性配置反射目标。只允许由 ProgressionEducationCompatibility.Apply 调用，
        /// 所有目标验证完成后才调用。任一目标为 null 时保持未初始化并记录错误。
        /// </summary>
        public static void Configure(
            MethodInfo? classCandidatePoolAddPawn,
            FieldInfo? classAssignmentsManagerAllPawnsField,
            FieldInfo? classRoleStudyGroupField,
            FieldInfo? studyGroupSubjectLogicField,
            Type? skillClassLogicTypeArg,
            Type? daycareClassLogicTypeArg)
        {
            if (configured)
            {
                return;
            }

            if (classCandidatePoolAddPawn == null
                || classAssignmentsManagerAllPawnsField == null
                || classRoleStudyGroupField == null
                || studyGroupSubjectLogicField == null
                || skillClassLogicTypeArg == null
                || daycareClassLogicTypeArg == null)
            {
                Log.ErrorOnce(
                    LogPrefix
                    + "Configure 收到 null 反射目标，本模块保持未初始化，"
                    + "所有 Postfix 将安全跳过。",
                    ErrorKeyConfigureNull);
                return;
            }

            addPawnMethod = classCandidatePoolAddPawn;
            allPawnsField = classAssignmentsManagerAllPawnsField;
            studyGroupField = classRoleStudyGroupField;
            subjectLogicField = studyGroupSubjectLogicField;
            skillClassLogicType = skillClassLogicTypeArg;
            daycareClassLogicType = daycareClassLogicTypeArg;
            configured = true;
        }

        // ============ Patch 1：ClassCandidatePool(Map) 构造器 ============

        /// <summary>
        /// 构造器 Postfix：通过 Education 自己的 AddPawn（内部去重）追加合法机械族机械师。
        /// __0 即第 1 个构造参数（Map），不依赖原始参数名。
        /// </summary>
        public static void Postfix_ClassCandidatePoolCtor(object __instance, Map __0)
        {
            if (!IsConfigured())
            {
                return;
            }

            if (__instance == null || __0 == null)
            {
                return;
            }

            List<Pawn> candidates;
            try
            {
                candidates = ProgressionEducationCandidateUtility
                    .GetEligibleClassroomTeachers(__0);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce(
                    LogPrefix
                    + "ClassCandidatePool(Map) 构造器 Postfix 枚举候选失败，"
                    + $"Map={__0}，异常：{ex}",
                    ErrorKeyCandidatePoolCandidates);
                return;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                Pawn pawn = candidates[i];
                try
                {
                    addPawnMethod!.Invoke(
                        __instance,
                        new object[] { pawn });
                }
                catch (Exception ex)
                {
                    Log.ErrorOnce(
                        LogPrefix
                        + "ClassCandidatePool.AddPawn 调用失败，"
                        + $"Pawn={pawn.LabelShort}（{pawn.ThingID}），异常：{ex}",
                        ErrorKeyCandidatePoolInvoke);
                    return;
                }
            }
        }

        // ============ Patch 2：ClassAssignmentsManager 四参数构造器 ============

        /// <summary>
        /// 构造器 Postfix：直接向 Education 自己的 allPawns 私有字段追加合法机械族机械师。
        /// 参数 0 = TeacherRole，参数 1 = StudentRole，参数 2 = Map，参数 3 = forcedRoles。
        /// __2 即第 3 个构造参数（Map），不依赖原始参数名。
        /// </summary>
        public static void Postfix_ClassAssignmentsManagerCtor(
            object __instance,
            Map __2)
        {
            if (!IsConfigured())
            {
                return;
            }

            if (__instance == null || __2 == null)
            {
                return;
            }

            List<Pawn>? allPawns;
            try
            {
                allPawns = allPawnsField!.GetValue(__instance) as List<Pawn>;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce(
                    LogPrefix
                    + "ClassAssignmentsManager 构造器 Postfix 读取 allPawns 失败，"
                    + $"Map={__2}，异常：{ex}",
                    ErrorKeyAssignmentsField);
                return;
            }

            if (allPawns == null)
            {
                Log.ErrorOnce(
                    LogPrefix
                    + "ClassAssignmentsManager 构造器 Postfix：allPawns 字段为空，"
                    + "无法追加机械族机械师候选。",
                    ErrorKeyAssignmentsField);
                return;
            }

            List<Pawn> candidates;
            try
            {
                candidates = ProgressionEducationCandidateUtility
                    .GetEligibleClassroomTeachers(__2);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce(
                    LogPrefix
                    + "ClassAssignmentsManager 构造器 Postfix 枚举候选失败，"
                    + $"Map={__2}，异常：{ex}",
                    ErrorKeyAssignmentsCandidates);
                return;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                Pawn pawn = candidates[i];
                if (!allPawns.Contains(pawn))
                {
                    allPawns.Add(pawn);
                }
            }
        }

        // ============ Patch 3：TeacherRole.CanAcceptPawn(Pawn) ============

        /// <summary>
        /// Postfix：具备 ClassroomTeaching 能力的机械族 Pawn 仅在 Education 原结果已 Accept 时保留，
        /// 且必须具备 timetable 数据层（ColonistLikeTimetable），
        /// 且当前实时 subjectLogic 属于 SkillClassLogic / DaycareClassLogic（含派生）时才放行。
        /// 严禁把 Education 原本的拒绝改成接受；Proficiency 与未知课程一律拒绝。
        /// 读取 studyGroup / subjectLogic 反射异常时按白名单安全原则 fail-closed 拒绝，
        /// 不再保留 Education 原先的 Accepted=true（无法证明课程类型即默认拒绝）。
        /// </summary>
        public static void Postfix_TeacherRoleCanAcceptPawn(
            object __instance,
            Pawn __0,
            ref AcceptanceReport __result)
        {
            if (!IsConfigured())
            {
                return;
            }

            if (__0 == null)
            {
                return;
            }

            if (!MechanoidMechanitorCapabilityUtility.HasCapability(
                    __0,
                    MechanoidMechanitorCapability.ClassroomTeaching))
            {
                return;
            }

            // 拥有 ClassroomTeaching 但缺少 timetable 数据层属于配置/生命周期错误，fail-closed 拒绝。
            if (!MechanoidMechanitorCapabilityUtility.HasCapability(
                    __0,
                    MechanoidMechanitorCapability.ColonistLikeTimetable)
                || __0.timetable == null)
            {
                __result = RejectWithReason(ReasonMissingTeachingInfrastructure);
                return;
            }

            // 严禁把 Education 原本的拒绝改成接受。
            if (!__result.Accepted)
            {
                return;
            }

            object? studyGroup;
            try
            {
                studyGroup = studyGroupField!.GetValue(__instance);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce(
                    LogPrefix
                    + "TeacherRole.CanAcceptPawn Postfix 读取 studyGroup 失败，"
                    + $"Pawn={__0.LabelShort}（{__0.ThingID}），异常：{ex}",
                    ErrorKeyTeacherRead);
                __result = RejectWithReason(ReasonCompatibilityReflectionFailure);
                return;
            }

            if (studyGroup == null)
            {
                __result = RejectWithReason(ReasonMissingTeachingInfrastructure);
                return;
            }

            object? subjectLogic;
            try
            {
                subjectLogic = subjectLogicField!.GetValue(studyGroup);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce(
                    LogPrefix
                    + "TeacherRole.CanAcceptPawn Postfix 读取 subjectLogic 失败，"
                    + $"Pawn={__0.LabelShort}（{__0.ThingID}），异常：{ex}",
                    ErrorKeyTeacherRead);
                __result = RejectWithReason(ReasonCompatibilityReflectionFailure);
                return;
            }

            if (subjectLogic == null
                || (!skillClassLogicType!.IsInstanceOfType(subjectLogic)
                    && !daycareClassLogicType!.IsInstanceOfType(subjectLogic)))
            {
                __result = RejectWithReason(ReasonUnsupportedClassType);
            }
        }

        // ============ Patch 4：StudentRole.CanAcceptPawn(Pawn) ============

        /// <summary>
        /// Postfix：具备 ClassroomTeaching 能力的机械族 Pawn（正式机械族机械师 / 挂载
        /// CompClassroomTeachingUser 的非机械师机械族）无论课程类型，StudentRole 一律拒绝。
        /// 普通 Pawn 完全保持 Education 原结果。
        /// </summary>
        public static void Postfix_StudentRoleCanAcceptPawn(
            Pawn __0,
            ref AcceptanceReport __result)
        {
            if (!IsConfigured())
            {
                return;
            }

            if (__0 == null)
            {
                return;
            }

            if (!MechanoidMechanitorCapabilityUtility.HasCapability(
                    __0,
                    MechanoidMechanitorCapability.ClassroomTeaching))
            {
                return;
            }

            // 具备 ClassroomTeaching 能力的机械族 Pawn（正式机械族机械师 / 挂载
            // CompClassroomTeachingUser 的非机械师机械族）无论课程类型，StudentRole 一律拒绝，
            // 且必须提供非空 Reason，避免 Progression: Education 的 CanParticipate 误判。
            // 普通 Pawn 完全保持 Education 原结果。
            __result = RejectWithReason(ReasonStudentRole);
        }

        private static bool IsConfigured()
        {
            if (configured)
            {
                return true;
            }

            Log.WarningOnce(
                LogPrefix
                + "Postfix 在未 Configure 状态下被调用，安全跳过。",
                WarningKeyNotConfigured);
            return false;
        }
    }
}
