using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(CompUsable), nameof(CompUsable.CanBeUsedBy))]
    public static class Patch_CompUsable_CanBeUsedBy_MechanoidMechanitorImplant
    {
        private const string LogPrefix =
            "[MAP-机械族机械师] MechanoidMechanitorImplantPatches.CompUsable.CanBeUsedBy：";

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            FieldInfo? field = AccessTools.Field(
                typeof(CompProperties_Usable),
                nameof(CompProperties_Usable.userMustHaveHediff));
            MethodInfo? resolver = AccessTools.Method(
                typeof(MechanoidMechanitorImplantUtility),
                nameof(MechanoidMechanitorImplantUtility.ResolveRequiredHediff));

            if (field == null || resolver == null)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}缺少 userMustHaveHediff 或 ResolveRequiredHediff 反射目标，补丁未应用。",
                    879345501);
                return codes;
            }

            MechanoidMechanitorImplantTranspilerSupport.InsertResolverAfterFieldLoads(
                codes,
                field,
                resolver,
                "CompProperties_Usable.userMustHaveHediff",
                LogPrefix,
                3,
                879345502,
                879345503);
            return codes;
        }
    }

    [HarmonyPatch(typeof(CompUseEffect_InstallImplant), nameof(CompUseEffect_InstallImplant.CanBeUsedBy))]
    public static class Patch_CompUseEffect_InstallImplant_CanBeUsedBy_MechanoidMechanitorImplant
    {
        private const string LogPrefix =
            "[MAP-机械族机械师] MechanoidMechanitorImplantPatches.CompUseEffect_InstallImplant.CanBeUsedBy：";

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            FieldInfo? allowNonColonistsField = AccessTools.Field(
                typeof(CompProperties_UseEffectInstallImplant),
                nameof(CompProperties_UseEffectInstallImplant.allowNonColonists));
            MethodInfo? allowNonColonistsResolver = AccessTools.Method(
                typeof(MechanoidMechanitorImplantUtility),
                nameof(MechanoidMechanitorImplantUtility.ResolveAllowNonColonists));

            if (allowNonColonistsField == null || allowNonColonistsResolver == null)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}无法解析 allowNonColonists 字段或 ResolveAllowNonColonists，该子修改未应用；bodyPart 子修改仍可继续。",
                    879345511);
            }
            else
            {
                MechanoidMechanitorImplantTranspilerSupport.InsertResolverAfterFieldLoads(
                    codes,
                    allowNonColonistsField,
                    allowNonColonistsResolver,
                    "CompProperties_UseEffectInstallImplant.allowNonColonists",
                    LogPrefix,
                    1,
                    879345512,
                    879345514);
            }

            FieldInfo? bodyPartField = AccessTools.Field(
                typeof(CompProperties_UseEffectInstallImplant),
                nameof(CompProperties_UseEffectInstallImplant.bodyPart));
            MethodInfo? bodyPartResolver = AccessTools.Method(
                typeof(MechanoidMechanitorImplantUtility),
                nameof(MechanoidMechanitorImplantUtility.ResolveImplantBodyPart));

            if (bodyPartField == null || bodyPartResolver == null)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}无法解析 bodyPart 字段或 ResolveImplantBodyPart，该子修改未应用；allowNonColonists 子修改仍可继续。",
                    879345516);
            }
            else
            {
                MechanoidMechanitorImplantTranspilerSupport.InsertResolverAfterFieldLoads(
                    codes,
                    bodyPartField,
                    bodyPartResolver,
                    "CompProperties_UseEffectInstallImplant.bodyPart",
                    LogPrefix,
                    2,
                    879345513,
                    879345515);
            }

            return codes;
        }
    }

    [HarmonyPatch(typeof(CompUseEffect_InstallImplant), nameof(CompUseEffect_InstallImplant.DoEffect))]
    public static class Patch_CompUseEffect_InstallImplant_DoEffect_MechanoidMechanitorImplant
    {
        private const string LogPrefix =
            "[MAP-机械族机械师] MechanoidMechanitorImplantPatches.CompUseEffect_InstallImplant.DoEffect：";

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return PatchBodyPartLoads(
                instructions,
                LogPrefix,
                879345521,
                879345522,
                879345523);
        }

        internal static IEnumerable<CodeInstruction> PatchBodyPartLoads(
            IEnumerable<CodeInstruction> instructions,
            string logPrefix,
            int resolveErrorKey,
            int fieldNotFoundKey,
            int countWarningKey)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            FieldInfo? field = AccessTools.Field(
                typeof(CompProperties_UseEffectInstallImplant),
                nameof(CompProperties_UseEffectInstallImplant.bodyPart));
            MethodInfo? resolver = AccessTools.Method(
                typeof(MechanoidMechanitorImplantUtility),
                nameof(MechanoidMechanitorImplantUtility.ResolveImplantBodyPart));

            if (field == null || resolver == null)
            {
                Log.ErrorOnce(
                    $"{logPrefix}缺少 bodyPart 或 ResolveImplantBodyPart 反射目标，补丁未应用。",
                    resolveErrorKey);
                return codes;
            }

            MechanoidMechanitorImplantTranspilerSupport.InsertResolverAfterFieldLoads(
                codes,
                field,
                resolver,
                "CompProperties_UseEffectInstallImplant.bodyPart",
                logPrefix,
                1,
                fieldNotFoundKey,
                countWarningKey);
            return codes;
        }
    }

    [HarmonyPatch(typeof(CompUseEffect_InstallImplant), nameof(CompUseEffect_InstallImplant.GetExistingImplant))]
    public static class Patch_CompUseEffect_InstallImplant_GetExistingImplant_MechanoidMechanitorImplant
    {
        private const string LogPrefix =
            "[MAP-机械族机械师] MechanoidMechanitorImplantPatches.CompUseEffect_InstallImplant.GetExistingImplant：";

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return Patch_CompUseEffect_InstallImplant_DoEffect_MechanoidMechanitorImplant.PatchBodyPartLoads(
                instructions,
                LogPrefix,
                879345531,
                879345532,
                879345533);
        }
    }

    internal static class MechanoidMechanitorImplantTranspilerSupport
    {
        internal static void InsertResolverAfterFieldLoads(
            List<CodeInstruction> codes,
            FieldInfo field,
            MethodInfo resolver,
            string fieldName,
            string logPrefix,
            int vanillaBaselineCount,
            int fieldNotFoundKey,
            int countWarningKey)
        {
            List<int> indices = new List<int>();
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].LoadsField(field))
                {
                    indices.Add(i);
                }
            }

            if (indices.Count == 0)
            {
                Log.ErrorOnce(
                    $"{logPrefix}未找到 {fieldName} 字段读取，实际 0 处，该子修改未应用。",
                    fieldNotFoundKey);
                return;
            }

            if (Prefs.DevMode && indices.Count != vanillaBaselineCount)
            {
                Log.WarningOnce(
                    $"{logPrefix}{fieldName} 字段读取实际 {indices.Count} 处，当前原版基线 {vanillaBaselineCount} 处；仍将对全部匹配点应用 resolver。",
                    countWarningKey);
            }

            for (int i = indices.Count - 1; i >= 0; i--)
            {
                int insertAt = indices[i] + 1;
                codes.Insert(insertAt, new CodeInstruction(OpCodes.Ldarg_0));
                codes.Insert(insertAt + 1, new CodeInstruction(OpCodes.Ldarg_1));
                codes.Insert(insertAt + 2, new CodeInstruction(OpCodes.Call, resolver));
            }
        }
    }
}
