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

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            FieldInfo? userMustHaveHediffField = AccessTools.Field(typeof(CompProperties_Usable), nameof(CompProperties_Usable.userMustHaveHediff));
            MethodInfo? resolveRequiredHediffMethod = AccessTools.Method(typeof(JusticeMechanitorImplantUtility), nameof(JusticeMechanitorImplantUtility.ResolveRequiredHediff));

            if (userMustHaveHediffField == null || resolveRequiredHediffMethod == null)
            {
                Log.Error($"{LogPrefix}缺少反射目标，补丁未应用。");
                return codes;
            }

            List<int> requiredHediffFieldIndices = new List<int>();
            for (int i = 0; i < codes.Count; i++)
            {
                CodeInstruction code = codes[i];
                if (code.opcode == OpCodes.Ldfld
                    && code.operand is FieldInfo field
                    && field.Equals(userMustHaveHediffField))
                {
                    requiredHediffFieldIndices.Add(i);
                }
            }

            if (requiredHediffFieldIndices.Count != 3)
            {
                Log.Error(
                    $"{LogPrefix}userMustHaveHediff 的 IL 匹配数量异常，实际找到 {requiredHediffFieldIndices.Count} 处，补丁未应用。");
                return codes;
            }

            // The generic colonist-like CompUsable patch already handles the IsFlesh gate.
            // Insert from back to front to avoid index shift.
            for (int i = requiredHediffFieldIndices.Count - 1; i >= 0; i--)
            {
                int idx = requiredHediffFieldIndices[i];
                codes.Insert(idx + 1, new CodeInstruction(OpCodes.Ldarg_0));
                codes.Insert(idx + 2, new CodeInstruction(OpCodes.Ldarg_1));
                codes.Insert(idx + 3, new CodeInstruction(OpCodes.Call, resolveRequiredHediffMethod));
            }

            return codes;
        }
    }

    [HarmonyPatch(typeof(CompUseEffect_InstallImplant), nameof(CompUseEffect_InstallImplant.CanBeUsedBy))]
    public static class Patch_CompUseEffect_InstallImplant_CanBeUsedBy_JusticeMechanitorImplant
    {
        private const string LogPrefix =
            "[MAP-机械族机械师] JusticeMechanitorImplantPatches.CompUseEffect_InstallImplant.CanBeUsedBy：";

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            FieldInfo? allowNonColonistsField = AccessTools.Field(typeof(CompProperties_UseEffectInstallImplant), nameof(CompProperties_UseEffectInstallImplant.allowNonColonists));
            FieldInfo? bodyPartField = AccessTools.Field(typeof(CompProperties_UseEffectInstallImplant), nameof(CompProperties_UseEffectInstallImplant.bodyPart));
            MethodInfo? resolveAllowNonColonistsMethod = AccessTools.Method(typeof(JusticeMechanitorImplantUtility), nameof(JusticeMechanitorImplantUtility.ResolveAllowNonColonists));
            MethodInfo? resolveBodyPartMethod = AccessTools.Method(typeof(JusticeMechanitorImplantUtility), nameof(JusticeMechanitorImplantUtility.ResolveImplantBodyPart));

            if (allowNonColonistsField == null || bodyPartField == null || resolveAllowNonColonistsMethod == null || resolveBodyPartMethod == null)
            {
                Log.Error($"{LogPrefix}缺少反射目标，补丁未应用。");
                return codes;
            }

            List<int> allowIndices = new List<int>();
            List<int> bodyPartIndices = new List<int>();
            for (int i = 0; i < codes.Count; i++)
            {
                CodeInstruction code = codes[i];
                if (code.opcode != OpCodes.Ldfld || code.operand is not FieldInfo field)
                {
                    continue;
                }

                if (field.Equals(allowNonColonistsField))
                {
                    allowIndices.Add(i);
                }
                else if (field.Equals(bodyPartField))
                {
                    bodyPartIndices.Add(i);
                }
            }

            if (allowIndices.Count != 1 || bodyPartIndices.Count != 2)
            {
                Log.Error(
                    $"{LogPrefix}allowNonColonists 与 bodyPart 的 IL 匹配数量异常，allowNonColonists ldfld={allowIndices.Count}，bodyPart ldfld={bodyPartIndices.Count}，补丁未应用。");
                return codes;
            }

            for (int i = bodyPartIndices.Count - 1; i >= 0; i--)
            {
                int idx = bodyPartIndices[i];
                codes.Insert(idx + 1, new CodeInstruction(OpCodes.Ldarg_0));
                codes.Insert(idx + 2, new CodeInstruction(OpCodes.Ldarg_1));
                codes.Insert(idx + 3, new CodeInstruction(OpCodes.Call, resolveBodyPartMethod));
            }

            int allowIdx = allowIndices[0];
            codes.Insert(allowIdx + 1, new CodeInstruction(OpCodes.Ldarg_0));
            codes.Insert(allowIdx + 2, new CodeInstruction(OpCodes.Ldarg_1));
            codes.Insert(allowIdx + 3, new CodeInstruction(OpCodes.Call, resolveAllowNonColonistsMethod));

            return codes;
        }
    }

    [HarmonyPatch(typeof(CompUseEffect_InstallImplant), nameof(CompUseEffect_InstallImplant.DoEffect))]
    public static class Patch_CompUseEffect_InstallImplant_DoEffect_JusticeMechanitorImplant
    {
        private const string LogPrefix =
            "[MAP-机械族机械师] JusticeMechanitorImplantPatches.CompUseEffect_InstallImplant.DoEffect：";

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            FieldInfo? bodyPartField = AccessTools.Field(typeof(CompProperties_UseEffectInstallImplant), nameof(CompProperties_UseEffectInstallImplant.bodyPart));
            MethodInfo? resolveBodyPartMethod = AccessTools.Method(typeof(JusticeMechanitorImplantUtility), nameof(JusticeMechanitorImplantUtility.ResolveImplantBodyPart));
            if (bodyPartField == null || resolveBodyPartMethod == null)
            {
                Log.Error($"{LogPrefix}缺少反射目标，补丁未应用。");
                return codes;
            }

            List<int> bodyPartIndices = new List<int>();
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Ldfld
                    && codes[i].operand is FieldInfo field
                    && field.Equals(bodyPartField))
                {
                    bodyPartIndices.Add(i);
                }
            }

            if (bodyPartIndices.Count != 1)
            {
                Log.Error($"{LogPrefix}bodyPart 的 IL 匹配数量异常，实际找到 {bodyPartIndices.Count} 处，补丁未应用。");
                return codes;
            }

            int idx = bodyPartIndices[0];
            codes.Insert(idx + 1, new CodeInstruction(OpCodes.Ldarg_0));
            codes.Insert(idx + 2, new CodeInstruction(OpCodes.Ldarg_1));
            codes.Insert(idx + 3, new CodeInstruction(OpCodes.Call, resolveBodyPartMethod));
            return codes;
        }
    }

    [HarmonyPatch(typeof(CompUseEffect_InstallImplant), nameof(CompUseEffect_InstallImplant.GetExistingImplant))]
    public static class Patch_CompUseEffect_InstallImplant_GetExistingImplant_JusticeMechanitorImplant
    {
        private const string LogPrefix =
            "[MAP-机械族机械师] JusticeMechanitorImplantPatches.CompUseEffect_InstallImplant.GetExistingImplant：";

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            FieldInfo? bodyPartField = AccessTools.Field(typeof(CompProperties_UseEffectInstallImplant), nameof(CompProperties_UseEffectInstallImplant.bodyPart));
            MethodInfo? resolveBodyPartMethod = AccessTools.Method(typeof(JusticeMechanitorImplantUtility), nameof(JusticeMechanitorImplantUtility.ResolveImplantBodyPart));
            if (bodyPartField == null || resolveBodyPartMethod == null)
            {
                Log.Error($"{LogPrefix}缺少反射目标，补丁未应用。");
                return codes;
            }

            List<int> bodyPartIndices = new List<int>();
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Ldfld
                    && codes[i].operand is FieldInfo field
                    && field.Equals(bodyPartField))
                {
                    bodyPartIndices.Add(i);
                }
            }

            if (bodyPartIndices.Count != 1)
            {
                Log.Error($"{LogPrefix}bodyPart 的 IL 匹配数量异常，实际找到 {bodyPartIndices.Count} 处，补丁未应用。");
                return codes;
            }

            int idx = bodyPartIndices[0];
            codes.Insert(idx + 1, new CodeInstruction(OpCodes.Ldarg_0));
            codes.Insert(idx + 2, new CodeInstruction(OpCodes.Ldarg_1));
            codes.Insert(idx + 3, new CodeInstruction(OpCodes.Call, resolveBodyPartMethod));
            return codes;
        }
    }
}
