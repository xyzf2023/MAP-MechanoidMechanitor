using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(CompUsable), nameof(CompUsable.CanBeUsedBy))]
    public static class Patch_CompUsable_CanBeUsedBy_JusticeMechanitorImplant
    {
        private const string LogPrefix =
            "[MAP-机械族机械师] JusticeMechanitorImplantPatches.CompUsable.CanBeUsedBy：";

        private const int ErrorKeyResolveFailed = 879345501;
        private const int ErrorKeyUserMustHaveHediffNotFound = 879345502;
        private const int WarningKeyUserMustHaveHediffCount = 879345503;

        private const int VanillaBaselineUserMustHaveHediffCount = 3;

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            FieldInfo? userMustHaveHediffField = AccessTools.Field(
                typeof(CompProperties_Usable),
                nameof(CompProperties_Usable.userMustHaveHediff));
            MethodInfo? resolveRequiredHediffMethod = AccessTools.Method(
                typeof(JusticeMechanitorImplantUtility),
                nameof(JusticeMechanitorImplantUtility.ResolveRequiredHediff));

            if (userMustHaveHediffField == null || resolveRequiredHediffMethod == null)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}缺少 userMustHaveHediff 或 ResolveRequiredHediff 反射目标，补丁未应用。",
                    ErrorKeyResolveFailed);
                return codes;
            }

            List<int> fieldIndices = JusticeMechanitorImplantTranspilerSupport.FindFieldLoadIndices(
                codes,
                userMustHaveHediffField);

            if (fieldIndices.Count == 0)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}未找到 CompProperties_Usable.userMustHaveHediff 字段读取，实际 0 处，该子修改未应用。",
                    ErrorKeyUserMustHaveHediffNotFound);
                return codes;
            }

            JusticeMechanitorImplantTranspilerSupport.LogDevModeCountMismatch(
                LogPrefix,
                "CompProperties_Usable.userMustHaveHediff",
                fieldIndices.Count,
                VanillaBaselineUserMustHaveHediffCount,
                WarningKeyUserMustHaveHediffCount);

            List<JusticeMechanitorImplantTranspilerSupport.ResolverInsertion> insertions =
                JusticeMechanitorImplantTranspilerSupport.CreateFieldResolverInsertions(
                    fieldIndices,
                    resolveRequiredHediffMethod);

            JusticeMechanitorImplantTranspilerSupport.InsertResolverCallsFromBack(codes, insertions);
            return codes;
        }
    }

    [HarmonyPatch(typeof(CompUseEffect_InstallImplant), nameof(CompUseEffect_InstallImplant.CanBeUsedBy))]
    public static class Patch_CompUseEffect_InstallImplant_CanBeUsedBy_JusticeMechanitorImplant
    {
        private const string LogPrefix =
            "[MAP-机械族机械师] JusticeMechanitorImplantPatches.CompUseEffect_InstallImplant.CanBeUsedBy：";

        private const int ErrorKeyResolveFailed = 879345511;
        private const int ErrorKeyAllowNonColonistsNotFound = 879345512;
        private const int ErrorKeyBodyPartNotFound = 879345513;
        private const int WarningKeyAllowNonColonistsCount = 879345514;
        private const int WarningKeyBodyPartCount = 879345515;

        private const int VanillaBaselineAllowNonColonistsCount = 1;
        private const int VanillaBaselineBodyPartCount = 2;

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            FieldInfo? allowNonColonistsField = AccessTools.Field(
                typeof(CompProperties_UseEffectInstallImplant),
                nameof(CompProperties_UseEffectInstallImplant.allowNonColonists));
            FieldInfo? bodyPartField = AccessTools.Field(
                typeof(CompProperties_UseEffectInstallImplant),
                nameof(CompProperties_UseEffectInstallImplant.bodyPart));
            MethodInfo? resolveAllowNonColonistsMethod = AccessTools.Method(
                typeof(JusticeMechanitorImplantUtility),
                nameof(JusticeMechanitorImplantUtility.ResolveAllowNonColonists));
            MethodInfo? resolveBodyPartMethod = AccessTools.Method(
                typeof(JusticeMechanitorImplantUtility),
                nameof(JusticeMechanitorImplantUtility.ResolveImplantBodyPart));

            if (allowNonColonistsField == null
                || bodyPartField == null
                || resolveAllowNonColonistsMethod == null
                || resolveBodyPartMethod == null)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}缺少 allowNonColonists、bodyPart 或 resolver 反射目标，全部子修改未应用。",
                    ErrorKeyResolveFailed);
                return codes;
            }

            List<JusticeMechanitorImplantTranspilerSupport.ResolverInsertion> insertions =
                new List<JusticeMechanitorImplantTranspilerSupport.ResolverInsertion>();

            List<int> allowIndices = JusticeMechanitorImplantTranspilerSupport.FindFieldLoadIndices(
                codes,
                allowNonColonistsField);
            if (allowIndices.Count == 0)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}未找到 CompProperties_UseEffectInstallImplant.allowNonColonists 字段读取，实际 0 处，该子修改未应用；bodyPart 子修改仍可继续。",
                    ErrorKeyAllowNonColonistsNotFound);
            }
            else
            {
                JusticeMechanitorImplantTranspilerSupport.LogDevModeCountMismatch(
                    LogPrefix,
                    "CompProperties_UseEffectInstallImplant.allowNonColonists",
                    allowIndices.Count,
                    VanillaBaselineAllowNonColonistsCount,
                    WarningKeyAllowNonColonistsCount);
                insertions.AddRange(
                    JusticeMechanitorImplantTranspilerSupport.CreateFieldResolverInsertions(
                        allowIndices,
                        resolveAllowNonColonistsMethod));
            }

            List<int> bodyPartIndices = JusticeMechanitorImplantTranspilerSupport.FindFieldLoadIndices(
                codes,
                bodyPartField);
            if (bodyPartIndices.Count == 0)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}未找到 CompProperties_UseEffectInstallImplant.bodyPart 字段读取，实际 0 处，该子修改未应用；allowNonColonists 子修改仍可继续。",
                    ErrorKeyBodyPartNotFound);
            }
            else
            {
                JusticeMechanitorImplantTranspilerSupport.LogDevModeCountMismatch(
                    LogPrefix,
                    "CompProperties_UseEffectInstallImplant.bodyPart",
                    bodyPartIndices.Count,
                    VanillaBaselineBodyPartCount,
                    WarningKeyBodyPartCount);
                insertions.AddRange(
                    JusticeMechanitorImplantTranspilerSupport.CreateFieldResolverInsertions(
                        bodyPartIndices,
                        resolveBodyPartMethod));
            }

            if (insertions.Count > 0)
            {
                JusticeMechanitorImplantTranspilerSupport.InsertResolverCallsFromBack(codes, insertions);
            }

            return codes;
        }
    }

    [HarmonyPatch(typeof(CompUseEffect_InstallImplant), nameof(CompUseEffect_InstallImplant.DoEffect))]
    public static class Patch_CompUseEffect_InstallImplant_DoEffect_JusticeMechanitorImplant
    {
        private const string LogPrefix =
            "[MAP-机械族机械师] JusticeMechanitorImplantPatches.CompUseEffect_InstallImplant.DoEffect：";

        private const int ErrorKeyResolveFailed = 879345521;
        private const int ErrorKeyBodyPartNotFound = 879345522;
        private const int WarningKeyBodyPartCount = 879345523;

        private const int VanillaBaselineBodyPartCount = 1;

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            FieldInfo? bodyPartField = AccessTools.Field(
                typeof(CompProperties_UseEffectInstallImplant),
                nameof(CompProperties_UseEffectInstallImplant.bodyPart));
            MethodInfo? resolveBodyPartMethod = AccessTools.Method(
                typeof(JusticeMechanitorImplantUtility),
                nameof(JusticeMechanitorImplantUtility.ResolveImplantBodyPart));

            if (bodyPartField == null || resolveBodyPartMethod == null)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}缺少 bodyPart 或 ResolveImplantBodyPart 反射目标，补丁未应用。",
                    ErrorKeyResolveFailed);
                return codes;
            }

            List<int> bodyPartIndices = JusticeMechanitorImplantTranspilerSupport.FindFieldLoadIndices(
                codes,
                bodyPartField);

            if (bodyPartIndices.Count == 0)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}未找到 CompProperties_UseEffectInstallImplant.bodyPart 字段读取，实际 0 处，补丁未应用。",
                    ErrorKeyBodyPartNotFound);
                return codes;
            }

            JusticeMechanitorImplantTranspilerSupport.LogDevModeCountMismatch(
                LogPrefix,
                "CompProperties_UseEffectInstallImplant.bodyPart",
                bodyPartIndices.Count,
                VanillaBaselineBodyPartCount,
                WarningKeyBodyPartCount);

            List<JusticeMechanitorImplantTranspilerSupport.ResolverInsertion> insertions =
                JusticeMechanitorImplantTranspilerSupport.CreateFieldResolverInsertions(
                    bodyPartIndices,
                    resolveBodyPartMethod);

            JusticeMechanitorImplantTranspilerSupport.InsertResolverCallsFromBack(codes, insertions);
            return codes;
        }
    }

    [HarmonyPatch(typeof(CompUseEffect_InstallImplant), nameof(CompUseEffect_InstallImplant.GetExistingImplant))]
    public static class Patch_CompUseEffect_InstallImplant_GetExistingImplant_JusticeMechanitorImplant
    {
        private const string LogPrefix =
            "[MAP-机械族机械师] JusticeMechanitorImplantPatches.CompUseEffect_InstallImplant.GetExistingImplant：";

        private const int ErrorKeyResolveFailed = 879345531;
        private const int ErrorKeyBodyPartNotFound = 879345532;
        private const int WarningKeyBodyPartCount = 879345533;

        private const int VanillaBaselineBodyPartCount = 1;

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            FieldInfo? bodyPartField = AccessTools.Field(
                typeof(CompProperties_UseEffectInstallImplant),
                nameof(CompProperties_UseEffectInstallImplant.bodyPart));
            MethodInfo? resolveBodyPartMethod = AccessTools.Method(
                typeof(JusticeMechanitorImplantUtility),
                nameof(JusticeMechanitorImplantUtility.ResolveImplantBodyPart));

            if (bodyPartField == null || resolveBodyPartMethod == null)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}缺少 bodyPart 或 ResolveImplantBodyPart 反射目标，补丁未应用。",
                    ErrorKeyResolveFailed);
                return codes;
            }

            List<int> bodyPartIndices = JusticeMechanitorImplantTranspilerSupport.FindFieldLoadIndices(
                codes,
                bodyPartField);

            if (bodyPartIndices.Count == 0)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}未找到 CompProperties_UseEffectInstallImplant.bodyPart 字段读取，实际 0 处，补丁未应用。",
                    ErrorKeyBodyPartNotFound);
                return codes;
            }

            JusticeMechanitorImplantTranspilerSupport.LogDevModeCountMismatch(
                LogPrefix,
                "CompProperties_UseEffectInstallImplant.bodyPart",
                bodyPartIndices.Count,
                VanillaBaselineBodyPartCount,
                WarningKeyBodyPartCount);

            List<JusticeMechanitorImplantTranspilerSupport.ResolverInsertion> insertions =
                JusticeMechanitorImplantTranspilerSupport.CreateFieldResolverInsertions(
                    bodyPartIndices,
                    resolveBodyPartMethod);

            JusticeMechanitorImplantTranspilerSupport.InsertResolverCallsFromBack(codes, insertions);
            return codes;
        }
    }

    internal static class JusticeMechanitorImplantTranspilerSupport
    {
        internal readonly struct ResolverInsertion
        {
            public int Index { get; }
            public MethodInfo Resolver { get; }
            public bool LoadArg0 { get; }
            public bool LoadArg1 { get; }

            public ResolverInsertion(int index, MethodInfo resolver, bool loadArg0, bool loadArg1)
            {
                Index = index;
                Resolver = resolver;
                LoadArg0 = loadArg0;
                LoadArg1 = loadArg1;
            }
        }

        internal static List<int> FindFieldLoadIndices(List<CodeInstruction> codes, FieldInfo field)
        {
            List<int> indices = new List<int>();
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].LoadsField(field))
                {
                    indices.Add(i);
                }
            }

            return indices;
        }

        internal static List<ResolverInsertion> CreateFieldResolverInsertions(
            IReadOnlyList<int> fieldIndices,
            MethodInfo resolver,
            bool loadArg0 = true,
            bool loadArg1 = true)
        {
            List<ResolverInsertion> insertions = new List<ResolverInsertion>(fieldIndices.Count);
            for (int i = 0; i < fieldIndices.Count; i++)
            {
                insertions.Add(new ResolverInsertion(
                    fieldIndices[i],
                    resolver,
                    loadArg0,
                    loadArg1));
            }

            return insertions;
        }

        internal static void InsertResolverCallsFromBack(
            List<CodeInstruction> codes,
            List<ResolverInsertion> insertions)
        {
            if (insertions.Count == 0)
            {
                return;
            }

            insertions.Sort(static (left, right) => right.Index.CompareTo(left.Index));

            for (int i = 0; i < insertions.Count; i++)
            {
                ResolverInsertion insertion = insertions[i];
                int insertAt = insertion.Index + 1;

                codes.Insert(insertAt, new CodeInstruction(OpCodes.Call, insertion.Resolver));
                if (insertion.LoadArg1)
                {
                    codes.Insert(insertAt, new CodeInstruction(OpCodes.Ldarg_1));
                }

                if (insertion.LoadArg0)
                {
                    codes.Insert(insertAt, new CodeInstruction(OpCodes.Ldarg_0));
                }
            }
        }

        internal static void LogDevModeCountMismatch(
            string logPrefix,
            string fieldName,
            int actualCount,
            int baselineCount,
            int warningKey)
        {
            if (!Prefs.DevMode || actualCount == baselineCount)
            {
                return;
            }

            Log.WarningOnce(
                $"{logPrefix}{fieldName} 字段读取实际 {actualCount} 处，当前原版基线 {baselineCount} 处；仍将对全部匹配点应用 resolver。",
                warningKey);
        }
    }
}
