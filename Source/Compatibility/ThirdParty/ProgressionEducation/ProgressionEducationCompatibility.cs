using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.ProgressionEducation
{
    /// <summary>
    /// 为第三方 MOD《Progression: Education》（ferny.ProgressionEducation）提供第一阶段教师兼容。
    /// 仅允许具备 ClassroomTeaching 能力的机械族 Pawn（正式机械族机械师，或挂载
    /// CompClassroomTeachingUser 的非机械师机械族）担任 SkillClassLogic / DaycareClassLogic 教师；
    /// 上述 Pawn 不得作为任何课程学生，不得担任 ProficiencyClassLogic 或未知课程教师。
    /// 仅在确认该 MOD 已加载后动态解析目标并安装 4 个 Postfix；
    /// 未加载时返回 Inactive，目标结构变化时返回 TargetChanged 并安全跳过。
    /// 本模块不建立任何 ProgressionEducation 程序集静态依赖。
    /// </summary>
    internal sealed class ProgressionEducationCompatibility :
        IThirdPartyCompatibilityModule
    {
        public string ModuleId => "ProgressionEducation";

        public string DisplayName => "Progression: Education";

        public string PackageId => "ferny.ProgressionEducation";

        public ThirdPartyCompatibilityResult Apply(Harmony harmony)
        {
            ModContentPack? mod =
                ThirdPartyCompatibilityTargetResolver.FindRunningMod(PackageId);
            if (mod == null)
            {
                return ThirdPartyCompatibilityResult.CreateInactive(
                    ModuleId,
                    DisplayName,
                    PackageId);
            }

            if (!TryResolveAllTargets(
                    mod,
                    out ResolvedTargets? targets,
                    out string targetFailure))
            {
                return ThirdPartyCompatibilityResult.CreateTargetChanged(
                    ModuleId,
                    DisplayName,
                    PackageId,
                    targetFailure);
            }

            if (targets == null)
            {
                return ThirdPartyCompatibilityResult.CreateFailed(
                    ModuleId,
                    DisplayName,
                    PackageId,
                    "Progression: Education 目标解析结果为空。",
                    new InvalidOperationException("解析后的目标成员集合为空。"));
            }

            MethodInfo? candidatePoolPostfix = GetPostfix(
                nameof(ProgressionEducationCompatibilityPatch.Postfix_ClassCandidatePoolCtor));
            MethodInfo? assignmentsPostfix = GetPostfix(
                nameof(ProgressionEducationCompatibilityPatch.Postfix_ClassAssignmentsManagerCtor));
            MethodInfo? teacherPostfix = GetPostfix(
                nameof(ProgressionEducationCompatibilityPatch.Postfix_TeacherRoleCanAcceptPawn));
            MethodInfo? studentPostfix = GetPostfix(
                nameof(ProgressionEducationCompatibilityPatch.Postfix_StudentRoleCanAcceptPawn));

            if (candidatePoolPostfix == null
                || assignmentsPostfix == null
                || teacherPostfix == null
                || studentPostfix == null)
            {
                return ThirdPartyCompatibilityResult.CreateFailed(
                    ModuleId,
                    DisplayName,
                    PackageId,
                    "无法解析本项目的 Progression: Education 兼容 Postfix 方法。",
                    new MissingMethodException(
                        typeof(ProgressionEducationCompatibilityPatch).FullName,
                        nameof(ProgressionEducationCompatibilityPatch.Postfix_ClassCandidatePoolCtor)
                        + " / "
                        + nameof(ProgressionEducationCompatibilityPatch.Postfix_ClassAssignmentsManagerCtor)
                        + " / "
                        + nameof(ProgressionEducationCompatibilityPatch.Postfix_TeacherRoleCanAcceptPawn)
                        + " / "
                        + nameof(ProgressionEducationCompatibilityPatch.Postfix_StudentRoleCanAcceptPawn)));
            }

            ProgressionEducationCompatibilityPatch.Configure(
                targets.AddPawnMethod,
                targets.AllPawnsField,
                targets.StudyGroupField,
                targets.SubjectLogicField,
                targets.SkillClassLogicType,
                targets.DaycareClassLogicType);

            try
            {
                harmony.Patch(
                    targets.ClassCandidatePoolCtor,
                    postfix: new HarmonyMethod(candidatePoolPostfix));
                harmony.Patch(
                    targets.ClassAssignmentsManagerCtor,
                    postfix: new HarmonyMethod(assignmentsPostfix));
                harmony.Patch(
                    targets.TeacherCanAcceptPawn,
                    postfix: new HarmonyMethod(teacherPostfix));
                harmony.Patch(
                    targets.StudentCanAcceptPawn,
                    postfix: new HarmonyMethod(studentPostfix));
            }
            catch (Exception ex)
            {
                UnpatchQuietly(
                    harmony, targets.ClassCandidatePoolCtor, candidatePoolPostfix);
                UnpatchQuietly(
                    harmony, targets.ClassAssignmentsManagerCtor, assignmentsPostfix);
                UnpatchQuietly(
                    harmony, targets.TeacherCanAcceptPawn, teacherPostfix);
                UnpatchQuietly(
                    harmony, targets.StudentCanAcceptPawn, studentPostfix);
                return ThirdPartyCompatibilityResult.CreateFailed(
                    ModuleId,
                    DisplayName,
                    PackageId,
                    "安装 Progression: Education 兼容补丁时发生异常。",
                    ex,
                    targets.ClassCandidatePoolCtor,
                    targets.ClassAssignmentsManagerCtor,
                    targets.TeacherCanAcceptPawn,
                    targets.StudentCanAcceptPawn);
            }

            return ThirdPartyCompatibilityResult.CreateApplied(
                ModuleId,
                DisplayName,
                PackageId,
                "已动态安装 4 个 Postfix："
                + "ClassCandidatePool(Map) 构造器、"
                + "ClassAssignmentsManager(TeacherRole, StudentRole, Map, Dictionary<string, Pawn>) 构造器、"
                + "TeacherRole.CanAcceptPawn、StudentRole.CanAcceptPawn。");
        }

        private static void UnpatchQuietly(
            Harmony harmony,
            MethodBase original,
            MethodInfo patch)
        {
            try
            {
                harmony.Unpatch(original, patch);
            }
            catch
            {
                // 回滚失败不得掩盖最初的补丁安装异常。
            }
        }

        private static MethodInfo? GetPostfix(string methodName)
        {
            return typeof(ProgressionEducationCompatibilityPatch).GetMethod(
                methodName,
                BindingFlags.Static
                | BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly);
        }

        private static bool TryResolveAllTargets(
            ModContentPack mod,
            out ResolvedTargets? targets,
            out string failureReason)
        {
            targets = null;
            failureReason = string.Empty;

            const string ns = "ProgressionEducation.";

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(
                    mod,
                    ns + "ClassCandidatePool",
                    out Type? classCandidatePoolType,
                    out string typeFailure))
            {
                failureReason = typeFailure;
                return false;
            }

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(
                    mod,
                    ns + "ClassAssignmentsManager",
                    out Type? classAssignmentsManagerType,
                    out typeFailure))
            {
                failureReason = typeFailure;
                return false;
            }

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(
                    mod,
                    ns + "ClassRole",
                    out Type? classRoleType,
                    out typeFailure))
            {
                failureReason = typeFailure;
                return false;
            }

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(
                    mod,
                    ns + "TeacherRole",
                    out Type? teacherRoleType,
                    out typeFailure))
            {
                failureReason = typeFailure;
                return false;
            }

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(
                    mod,
                    ns + "StudentRole",
                    out Type? studentRoleType,
                    out typeFailure))
            {
                failureReason = typeFailure;
                return false;
            }

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(
                    mod,
                    ns + "StudyGroup",
                    out Type? studyGroupType,
                    out typeFailure))
            {
                failureReason = typeFailure;
                return false;
            }

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(
                    mod,
                    ns + "ClassSubjectLogic",
                    out Type? classSubjectLogicType,
                    out typeFailure))
            {
                failureReason = typeFailure;
                return false;
            }

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(
                    mod,
                    ns + "SkillClassLogic",
                    out Type? skillClassLogicType,
                    out typeFailure))
            {
                failureReason = typeFailure;
                return false;
            }

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(
                    mod,
                    ns + "DaycareClassLogic",
                    out Type? daycareClassLogicType,
                    out typeFailure))
            {
                failureReason = typeFailure;
                return false;
            }

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueConstructor(
                    classCandidatePoolType!,
                    new[] { typeof(Map) },
                    out ConstructorInfo? classCandidatePoolCtor,
                    out string ctorFailure))
            {
                failureReason = ctorFailure;
                return false;
            }

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueConstructor(
                    classAssignmentsManagerType!,
                    new[]
                    {
                        teacherRoleType!,
                        studentRoleType!,
                        typeof(Map),
                        typeof(Dictionary<string, Pawn>)
                    },
                    out ConstructorInfo? classAssignmentsManagerCtor,
                    out ctorFailure))
            {
                failureReason = ctorFailure;
                return false;
            }

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueInstanceMethod(
                    mod,
                    ns + "ClassCandidatePool",
                    "AddPawn",
                    typeof(void),
                    new[] { typeof(Pawn) },
                    out MethodInfo? addPawnMethod,
                    out string methodFailure))
            {
                failureReason = methodFailure;
                return false;
            }

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueInstanceMethod(
                    mod,
                    ns + "TeacherRole",
                    "CanAcceptPawn",
                    typeof(AcceptanceReport),
                    new[] { typeof(Pawn) },
                    out MethodInfo? teacherCanAcceptPawn,
                    out methodFailure))
            {
                failureReason = methodFailure;
                return false;
            }

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueInstanceMethod(
                    mod,
                    ns + "StudentRole",
                    "CanAcceptPawn",
                    typeof(AcceptanceReport),
                    new[] { typeof(Pawn) },
                    out MethodInfo? studentCanAcceptPawn,
                    out methodFailure))
            {
                failureReason = methodFailure;
                return false;
            }

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueField(
                    classRoleType!,
                    "studyGroup",
                    studyGroupType!,
                    BindingFlags.Public
                    | BindingFlags.Instance
                    | BindingFlags.DeclaredOnly,
                    out FieldInfo? studyGroupField,
                    out string fieldFailure))
            {
                failureReason = fieldFailure;
                return false;
            }

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueField(
                    studyGroupType!,
                    "subjectLogic",
                    classSubjectLogicType!,
                    BindingFlags.Public
                    | BindingFlags.Instance
                    | BindingFlags.DeclaredOnly,
                    out FieldInfo? subjectLogicField,
                    out fieldFailure))
            {
                failureReason = fieldFailure;
                return false;
            }

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueField(
                    classAssignmentsManagerType!,
                    "allPawns",
                    typeof(List<Pawn>),
                    BindingFlags.NonPublic
                    | BindingFlags.Instance
                    | BindingFlags.DeclaredOnly,
                    out FieldInfo? allPawnsField,
                    out fieldFailure))
            {
                failureReason = fieldFailure;
                return false;
            }

            targets = new ResolvedTargets(
                classCandidatePoolType!,
                classAssignmentsManagerType!,
                teacherRoleType!,
                studentRoleType!,
                classCandidatePoolCtor!,
                classAssignmentsManagerCtor!,
                addPawnMethod!,
                teacherCanAcceptPawn!,
                studentCanAcceptPawn!,
                studyGroupField!,
                subjectLogicField!,
                allPawnsField!,
                skillClassLogicType!,
                daycareClassLogicType!);
            return true;
        }

        private sealed class ResolvedTargets
        {
            public ResolvedTargets(
                Type classCandidatePoolType,
                Type classAssignmentsManagerType,
                Type teacherRoleType,
                Type studentRoleType,
                ConstructorInfo classCandidatePoolCtor,
                ConstructorInfo classAssignmentsManagerCtor,
                MethodInfo addPawnMethod,
                MethodInfo teacherCanAcceptPawn,
                MethodInfo studentCanAcceptPawn,
                FieldInfo studyGroupField,
                FieldInfo subjectLogicField,
                FieldInfo allPawnsField,
                Type skillClassLogicType,
                Type daycareClassLogicType)
            {
                ClassCandidatePoolType = classCandidatePoolType;
                ClassAssignmentsManagerType = classAssignmentsManagerType;
                TeacherRoleType = teacherRoleType;
                StudentRoleType = studentRoleType;
                ClassCandidatePoolCtor = classCandidatePoolCtor;
                ClassAssignmentsManagerCtor = classAssignmentsManagerCtor;
                AddPawnMethod = addPawnMethod;
                TeacherCanAcceptPawn = teacherCanAcceptPawn;
                StudentCanAcceptPawn = studentCanAcceptPawn;
                StudyGroupField = studyGroupField;
                SubjectLogicField = subjectLogicField;
                AllPawnsField = allPawnsField;
                SkillClassLogicType = skillClassLogicType;
                DaycareClassLogicType = daycareClassLogicType;
            }

            public Type ClassCandidatePoolType { get; }

            public Type ClassAssignmentsManagerType { get; }

            public Type TeacherRoleType { get; }

            public Type StudentRoleType { get; }

            public ConstructorInfo ClassCandidatePoolCtor { get; }

            public ConstructorInfo ClassAssignmentsManagerCtor { get; }

            public MethodInfo AddPawnMethod { get; }

            public MethodInfo TeacherCanAcceptPawn { get; }

            public MethodInfo StudentCanAcceptPawn { get; }

            public FieldInfo StudyGroupField { get; }

            public FieldInfo SubjectLogicField { get; }

            public FieldInfo AllPawnsField { get; }

            public Type SkillClassLogicType { get; }

            public Type DaycareClassLogicType { get; }
        }
    }
}
