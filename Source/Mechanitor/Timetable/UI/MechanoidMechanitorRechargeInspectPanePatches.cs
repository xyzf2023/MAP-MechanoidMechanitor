using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 在原版 Inspect Pane（左下角检查面板）顶部按钮排布流水线内部插入
    /// 机械族机械师个人充电阈值按钮。
    ///
    /// 只做极窄 Transpiler：不重写 DoInspectPaneButtons，不 Prefix return false，
    /// 原版 InfoCard / 遇敌反应 / Rename / Guilty / StorageGroup 等逻辑全部保留。
    /// 锚点失效时安全返回原始 IL，绝不生成损坏指令。
    /// </summary>
    [HarmonyPatch(
        typeof(MainTabWindow_Inspect),
        nameof(MainTabWindow_Inspect.DoInspectPaneButtons))]
    internal static class Patch_MainTabWindow_Inspect_DoInspectPaneButtons
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions,
            MethodBase original)
        {
            return MechanoidMechanitorRechargeInspectPaneTranspilerUtility
                .InsertRechargeButtonAfterHostilityButton(instructions, original);
        }
    }

    internal static class MechanoidMechanitorRechargeInspectPaneTranspilerUtility
    {
        private const string LogPrefix =
            "[MAP-机械族机械师] MechanoidMechanitorRechargeInspectPanePatches：";

        private const string PatchSkippedSuffix =
            "机械族机械师个人充电阈值按钮未插入 Inspect Pane，原版 IL 保持不变。";

        private const int ErrorKeyResolveMembers = 879347220;
        private const int ErrorKeyAnchorCount = 879347221;
        private const int ErrorKeyAnchorShape = 879347222;
        private const int ErrorKeyLineEndWidthArg = 879347223;

        // 遇敌反应按钮调用之后，lineEndWidth += 24f 的 stind.r4 必然出现在很近的位置，
        // 限制搜索窗口避免误匹配到后续 Rename / Guilty 分支的写入。
        private const int LineEndWidthSearchWindow = 12;

        /// <summary>
        /// 语义锚点：原版 HostilityResponseModeUtility.DrawResponseButton 调用。
        /// 在其后的 lineEndWidth += 24f 之后插入：
        ///   num = MechanoidMechanitorRechargeInspectPaneUtility
        ///             .DrawRechargeButtonAndAdvance(p, num, ref lineEndWidth);
        /// 因此充电阈值按钮必然紧挨遇敌反应按钮左侧，且在 Rename / Guilty 之前，
        /// 后续原版按钮继续基于推进后的 num 布局，不会重叠。
        /// </summary>
        internal static IEnumerable<CodeInstruction> InsertRechargeButtonAfterHostilityButton(
            IEnumerable<CodeInstruction> instructions,
            MethodBase original)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            MethodInfo? drawResponseButton = AccessTools.Method(
                typeof(HostilityResponseModeUtility),
                nameof(HostilityResponseModeUtility.DrawResponseButton),
                new[] { typeof(Rect), typeof(Pawn), typeof(bool) });
            MethodInfo? helper = AccessTools.Method(
                typeof(MechanoidMechanitorRechargeInspectPaneUtility),
                nameof(MechanoidMechanitorRechargeInspectPaneUtility
                    .DrawRechargeButtonAndAdvance));

            if (drawResponseButton == null || helper == null)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}无法解析 HostilityResponseModeUtility.DrawResponseButton / "
                        + $"DrawRechargeButtonAndAdvance，{PatchSkippedSuffix}",
                    ErrorKeyResolveMembers);
                return codes;
            }

            int callIndex = -1;
            int matchCount = 0;
            for (int i = 0; i < codes.Count; i++)
            {
                if (!codes[i].Calls(drawResponseButton))
                {
                    continue;
                }

                matchCount++;
                callIndex = i;
            }

            if (matchCount != 1)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}DoInspectPaneButtons 中 DrawResponseButton 调用预期 1 处，"
                        + $"实际匹配 {matchCount} 处，{PatchSkippedSuffix}",
                    ErrorKeyAnchorCount);
                return codes;
            }

            // 原版形态：
            //   ldloc num / ldc.r4 24 / sub / stloc num
            //   <构造 Rect(num, 0, 24, 24)>
            //   <加载 p>
            //   ldc.i4.0            // paintable: false
            //   call DrawResponseButton
            //   ldarg lineEndWidth / dup / ldind.r4 / ldc.r4 24 / add / stind.r4
            if (callIndex < 3 || codes[callIndex - 1].opcode != OpCodes.Ldc_I4_0)
            {
                LogAnchorShape("paintable 实参形态与预期不一致");
                return codes;
            }

            // p 的加载序列按“实参类型”识别，而不是按固定局部变量槽：
            // 可能是单条 ldloc（Pawn 局部），也可能是闭包 ldloc + ldfld（Guilty 的 lambda 捕获 p）。
            // 最终直接克隆原版真实使用的指令。
            List<CodeInstruction>? pawnLoad = TryResolvePawnLoad(codes, callIndex, original);
            if (pawnLoad == null)
            {
                LogAnchorShape("无法按 Pawn 类型识别遇敌反应按钮的 Pawn 实参加载指令");
                return codes;
            }

            int pawnLoadStartIndex = callIndex - 1 - pawnLoad.Count;

            // num（float 局部）：向前找最近一次 float 局部写入，即 num -= 24f。
            int storeNumIndex = -1;
            int numLocalIndex = -1;
            for (int i = pawnLoadStartIndex - 1; i >= 1; i--)
            {
                if (!IsStloc(codes[i]))
                {
                    continue;
                }

                int localIndex = GetLocalIndex(codes[i]);
                if (localIndex < 0)
                {
                    continue;
                }

                System.Type? localType = TryGetLocalType(codes[i], original, localIndex);
                if (localType != null && localType != typeof(float))
                {
                    continue;
                }

                storeNumIndex = i;
                numLocalIndex = localIndex;
                break;
            }

            if (storeNumIndex < 0)
            {
                LogAnchorShape("未找到 num -= 24f 的 float 局部变量写入");
                return codes;
            }

            OpCode beforeStoreOpcode = codes[storeNumIndex - 1].opcode;
            if (beforeStoreOpcode != OpCodes.Sub && beforeStoreOpcode != OpCodes.Add)
            {
                LogAnchorShape("num 写入之前不是 24f 的加减运算");
                return codes;
            }

            CodeInstruction? loadNumSource = null;
            for (int i = storeNumIndex + 1; i < callIndex; i++)
            {
                if (IsLdloc(codes[i]) && GetLocalIndex(codes[i]) == numLocalIndex)
                {
                    loadNumSource = codes[i];
                    break;
                }
            }

            if (loadNumSource == null)
            {
                LogAnchorShape("未找到 num 局部变量的读取指令");
                return codes;
            }

            int storeLineEndWidthIndex = -1;
            int searchLimit = System.Math.Min(
                codes.Count - 1,
                callIndex + LineEndWidthSearchWindow);
            for (int i = callIndex + 1; i <= searchLimit; i++)
            {
                if (codes[i].opcode == OpCodes.Stind_R4)
                {
                    storeLineEndWidthIndex = i;
                    break;
                }
            }

            if (storeLineEndWidthIndex < 0)
            {
                LogAnchorShape("未找到遇敌反应按钮之后的 lineEndWidth += 24f");
                return codes;
            }

            int lineEndWidthArgIndex = ResolveByRefFloatArgIndex(original);
            if (lineEndWidthArgIndex < 0)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}无法从方法签名解析 ref float lineEndWidth 参数位置，"
                        + PatchSkippedSuffix,
                    ErrorKeyLineEndWidthArg);
                return codes;
            }

            CodeInstruction? loadLineEndWidthSource = null;
            for (int i = storeLineEndWidthIndex - 1; i > callIndex; i--)
            {
                if (IsLdarg(codes[i]) && GetArgIndex(codes[i], original) == lineEndWidthArgIndex)
                {
                    loadLineEndWidthSource = codes[i];
                    break;
                }
            }

            if (loadLineEndWidthSource == null)
            {
                LogAnchorShape("未找到 lineEndWidth 的 ldarg 指令");
                return codes;
            }

            int insertIndex = storeLineEndWidthIndex + 1;
            if (insertIndex >= codes.Count)
            {
                LogAnchorShape("遇敌反应按钮块之后没有后续指令");
                return codes;
            }

            CodeInstruction anchorNext = codes[insertIndex];
            if (anchorNext.blocks.Count > 0)
            {
                LogAnchorShape("插入点位于异常处理块边界，放弃插入");
                return codes;
            }

            List<CodeInstruction> inserted = new List<CodeInstruction>();
            for (int i = 0; i < pawnLoad.Count; i++)
            {
                inserted.Add(Clone(pawnLoad[i]));
            }

            inserted.Add(Clone(loadNumSource));
            inserted.Add(Clone(loadLineEndWidthSource));
            inserted.Add(new CodeInstruction(OpCodes.Call, helper));
            inserted.Add(Clone(codes[storeNumIndex]));

            // 遇敌反应 if 的 false 分支原本跳到 anchorNext。把这些标签迁移到我们的首条指令，
            // 使“没有遇敌反应按钮的机械族机械师”同样会绘制充电阈值按钮。
            // 插入序列本身栈中性（压入实参 -> call -> 存回 num），落在标签处执行安全。
            if (anchorNext.labels.Count > 0)
            {
                inserted[0].labels.AddRange(anchorNext.labels);
                anchorNext.labels.Clear();
            }

            codes.InsertRange(insertIndex, inserted);
            return codes;
        }

        private static void LogAnchorShape(string reason)
        {
            Log.ErrorOnce(
                $"{LogPrefix}原版 DoInspectPaneButtons IL 形态与预期不一致（{reason}），"
                    + PatchSkippedSuffix,
                ErrorKeyAnchorShape);
        }

        private static CodeInstruction Clone(CodeInstruction source)
        {
            // 只复制 opcode / operand，绝不复制 labels 与 blocks，避免重复标签。
            return new CodeInstruction(source.opcode, source.operand);
        }

        /// <summary>
        /// 按“实参类型是 Pawn”识别原版遇敌反应按钮的 Pawn 加载指令序列，
        /// 兼容普通局部（ldloc p）与闭包捕获（ldloc 闭包实例 + ldfld Pawn p）两种形态，
        /// 不硬编码任何局部变量槽位。识别失败返回 null，由调用方安全放弃插入。
        /// </summary>
        private static List<CodeInstruction>? TryResolvePawnLoad(
            List<CodeInstruction> codes,
            int callIndex,
            MethodBase original)
        {
            CodeInstruction last = codes[callIndex - 2];

            if (callIndex >= 4
                && last.opcode == OpCodes.Ldfld
                && last.operand is FieldInfo field
                && field.FieldType == typeof(Pawn)
                && IsLdloc(codes[callIndex - 3]))
            {
                return new List<CodeInstruction> { codes[callIndex - 3], last };
            }

            if (IsLdloc(last)
                && TryGetLocalType(last, original, GetLocalIndex(last)) == typeof(Pawn))
            {
                return new List<CodeInstruction> { last };
            }

            return null;
        }

        private static bool IsLdloc(CodeInstruction code)
        {
            OpCode opcode = code.opcode;
            return opcode == OpCodes.Ldloc
                || opcode == OpCodes.Ldloc_S
                || opcode == OpCodes.Ldloc_0
                || opcode == OpCodes.Ldloc_1
                || opcode == OpCodes.Ldloc_2
                || opcode == OpCodes.Ldloc_3;
        }

        private static bool IsStloc(CodeInstruction code)
        {
            OpCode opcode = code.opcode;
            return opcode == OpCodes.Stloc
                || opcode == OpCodes.Stloc_S
                || opcode == OpCodes.Stloc_0
                || opcode == OpCodes.Stloc_1
                || opcode == OpCodes.Stloc_2
                || opcode == OpCodes.Stloc_3;
        }

        private static bool IsLdarg(CodeInstruction code)
        {
            OpCode opcode = code.opcode;
            return opcode == OpCodes.Ldarg
                || opcode == OpCodes.Ldarg_S
                || opcode == OpCodes.Ldarg_0
                || opcode == OpCodes.Ldarg_1
                || opcode == OpCodes.Ldarg_2
                || opcode == OpCodes.Ldarg_3;
        }

        /// <summary>
        /// 从实际 IL 指令解析局部变量槽位（兼容短形式与 LocalBuilder / LocalVariableInfo operand），
        /// 用于校验 num 的读写是否成对，而不是硬编码某个固定槽位。
        /// </summary>
        private static int GetLocalIndex(CodeInstruction code)
        {
            OpCode opcode = code.opcode;
            if (opcode == OpCodes.Ldloc_0 || opcode == OpCodes.Stloc_0)
            {
                return 0;
            }

            if (opcode == OpCodes.Ldloc_1 || opcode == OpCodes.Stloc_1)
            {
                return 1;
            }

            if (opcode == OpCodes.Ldloc_2 || opcode == OpCodes.Stloc_2)
            {
                return 2;
            }

            if (opcode == OpCodes.Ldloc_3 || opcode == OpCodes.Stloc_3)
            {
                return 3;
            }

            return GetOperandIndex(code.operand);
        }

        private static int GetArgIndex(CodeInstruction code, MethodBase original)
        {
            OpCode opcode = code.opcode;
            if (opcode == OpCodes.Ldarg_0)
            {
                return 0;
            }

            if (opcode == OpCodes.Ldarg_1)
            {
                return 1;
            }

            if (opcode == OpCodes.Ldarg_2)
            {
                return 2;
            }

            if (opcode == OpCodes.Ldarg_3)
            {
                return 3;
            }

            if (code.operand is ParameterInfo parameter)
            {
                return parameter.Position + (original.IsStatic ? 0 : 1);
            }

            return GetOperandIndex(code.operand);
        }

        private static int GetOperandIndex(object? operand)
        {
            switch (operand)
            {
                case LocalVariableInfo local:
                    return local.LocalIndex;
                case int index:
                    return index;
                case short shortIndex:
                    return shortIndex;
                case byte byteIndex:
                    return byteIndex;
                case sbyte sbyteIndex:
                    return sbyteIndex;
                default:
                    return -1;
            }
        }

        private static System.Type? TryGetLocalType(
            CodeInstruction code,
            MethodBase original,
            int localIndex)
        {
            if (code.operand is LocalVariableInfo local)
            {
                return local.LocalType;
            }

            IList<LocalVariableInfo>? locals = original.GetMethodBody()?.LocalVariables;
            if (locals == null || localIndex < 0 || localIndex >= locals.Count)
            {
                return null;
            }

            return locals[localIndex].LocalType;
        }

        /// <summary>
        /// 从方法签名解析 ref float 参数（lineEndWidth）的实参槽位，不硬编码 Ldarg_2。
        /// </summary>
        private static int ResolveByRefFloatArgIndex(MethodBase original)
        {
            ParameterInfo[] parameters = original.GetParameters();
            for (int i = 0; i < parameters.Length; i++)
            {
                System.Type parameterType = parameters[i].ParameterType;
                if (parameterType.IsByRef && parameterType.GetElementType() == typeof(float))
                {
                    return i + (original.IsStatic ? 0 : 1);
                }
            }

            return -1;
        }
    }
}
