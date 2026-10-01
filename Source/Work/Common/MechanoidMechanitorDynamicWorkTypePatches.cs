using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 仅扩展 GetDisabledWorkTypes 中机械体种族列表的 Contains 判断。
    /// 原版其他禁用来源和第三方 Prefix/Postfix 继续按原有流程生效。
    /// </summary>
    [HarmonyPatch]
    internal static class MechanoidMechanitorDynamicWorkTypePatches
    {
        private const string LogPrefix = "[MAP-机械族机械师] 动态工作类别：";
        private const int ResolveErrorKey = 0x574B_0101;
        private const int StructureErrorKey = 0x574B_0102;
        private static MethodInfo? targetMethod;
        private static readonly FieldInfo? RaceWorkTypesField = AccessTools.Field(
            typeof(RaceProperties), nameof(RaceProperties.mechEnabledWorkTypes));
        private static readonly MethodInfo? ContainsMethod = AccessTools.Method(
            typeof(List<WorkTypeDef>), nameof(List<WorkTypeDef>.Contains),
            new[] { typeof(WorkTypeDef) });
        private static readonly MethodInfo? HelperMethod = AccessTools.Method(
            typeof(MechWorkTypeAuthorizationUtility),
            nameof(MechWorkTypeAuthorizationUtility.RaceProfileAllowsWorkType));

        private static bool Prepare()
        {
            if (RaceWorkTypesField == null || ContainsMethod == null || HelperMethod == null)
            {
                Log.ErrorOnce($"{LogPrefix}无法解析种族列表或工作资格方法，补丁未应用。", ResolveErrorKey);
                return false;
            }

            try
            {
                targetMethod = null;
                foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(Pawn)))
                {
                    // 本机 1.6 的判断位于 FillList 局部函数；不依赖编译器生成的数字后缀。
                    if (method.IsStatic || method.ReturnType != typeof(void)
                        || !method.Name.StartsWith(
                            "<GetDisabledWorkTypes>g__FillList|", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    List<CodeInstruction> codes = new List<CodeInstruction>(
                        PatchProcessor.GetOriginalInstructions(method));
                    if (FindRaceProfileContains(codes) < 0)
                    {
                        continue;
                    }
                    if (targetMethod != null)
                    {
                        targetMethod = null;
                        break;
                    }
                    targetMethod = method;
                }

                if (targetMethod != null)
                {
                    return true;
                }
                Log.ErrorOnce($"{LogPrefix}未找到唯一的原版种族工作列表判断，补丁未应用。", ResolveErrorKey);
            }
            catch (Exception ex)
            {
                targetMethod = null;
                Log.ErrorOnce($"{LogPrefix}解析原版工作类别判断失败，补丁未应用：{ex}", ResolveErrorKey);
            }
            return false;
        }

        private static MethodBase TargetMethod() => targetMethod!;

        private static int FindRaceProfileContains(List<CodeInstruction> codes)
        {
            int fieldCount = 0;
            int callIndex = -1;
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode != OpCodes.Ldfld
                    || !Equals(codes[i].operand, RaceWorkTypesField))
                {
                    continue;
                }

                fieldCount++;
                for (int j = i + 1; j < codes.Count && j <= i + 8; j++)
                {
                    if (ContainsMethod != null && codes[j].Calls(ContainsMethod))
                    {
                        if (callIndex >= 0 || codes[j].blocks.Count != 0)
                        {
                            return -1;
                        }
                        callIndex = j;
                        break;
                    }
                }
            }
            return fieldCount == 1 ? callIndex : -1;
        }

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            int callIndex = FindRaceProfileContains(codes);
            if (callIndex < 0 || HelperMethod == null)
            {
                Log.ErrorOnce($"{LogPrefix}种族列表判断结构已变化，补丁未应用。", StructureErrorKey);
                return codes;
            }

            CodeInstruction loadPawn = new CodeInstruction(OpCodes.Ldarg_0);
            loadPawn.labels.AddRange(codes[callIndex].labels);
            codes[callIndex].labels.Clear();
            codes[callIndex].opcode = OpCodes.Call;
            codes[callIndex].operand = HelperMethod;
            codes.Insert(callIndex, loadPawn);
            return codes;
        }
    }
}
