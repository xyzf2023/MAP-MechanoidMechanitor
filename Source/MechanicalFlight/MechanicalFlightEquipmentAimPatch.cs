using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(PawnRenderUtility),
        nameof(PawnRenderUtility.DrawEquipmentAndApparelExtras))]
    internal static class MechanicalFlightEquipmentAimPatch
    {
        private const string LogPrefix =
            "[MAP-机械族机械师] MechanicalFlightEquipmentAimPatch：";

        private static Vector3 GetAimOrigin(Pawn pawn, Vector3 equipmentDrawPos)
        {
            // 仅在仍具有空中视觉偏移时改用武器当前的实际绘制锚点。
            // 普通 Pawn 与已经完全落地的机械体继续严格使用原版 Pawn.DrawPos。
            return MechanicalFlightUtility.HasHoverVisual(pawn)
                ? equipmentDrawPos
                : pawn.DrawPos;
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            MethodInfo? pawnDrawPosGetter = AccessTools.PropertyGetter(
                typeof(Pawn), nameof(Pawn.DrawPos));
            MethodInfo? thingDrawPosGetter = AccessTools.PropertyGetter(
                typeof(Thing), nameof(Thing.DrawPos));
            MethodInfo? helperMethod = AccessTools.Method(
                typeof(MechanicalFlightEquipmentAimPatch), nameof(GetAimOrigin));

            if (pawnDrawPosGetter == null && thingDrawPosGetter == null)
            {
                Log.Error($"{LogPrefix}未找到 DrawPos getter，补丁未应用。");
                return codes;
            }

            if (helperMethod == null)
            {
                Log.Error($"{LogPrefix}未找到 {nameof(GetAimOrigin)} 辅助方法，补丁未应用。");
                return codes;
            }

            List<int> matches = new List<int>();
            for (int i = 1; i < codes.Count; i++)
            {
                CodeInstruction instruction = codes[i];
                if ((instruction.opcode == OpCodes.Call
                        || instruction.opcode == OpCodes.Callvirt)
                    && instruction.operand is MethodInfo method
                    && (method.Equals(pawnDrawPosGetter)
                        || method.Equals(thingDrawPosGetter))
                    && codes[i - 1].opcode == OpCodes.Ldarg_0)
                {
                    matches.Add(i);
                }
            }

            // 原版这里只应有两次由参数 pawn（arg0）直接读取的 DrawPos：
            // 一次用于判断瞄准向量是否接近零，一次用于真正计算 AngleFlat。
            // 同时兼容 IL 将虚属性调用记录为 Pawn.DrawPos 或 Thing.DrawPos 的情况；
            // 通过前置 ldarg.0 限定射手自身，避免误匹配目标 Thing.DrawPos。
            if (matches.Count != 2)
            {
                Log.Error($"{LogPrefix}PawnRenderUtility.DrawEquipmentAndApparelExtras 中 "
                    + $"射手 DrawPos 读取预期 2 处，实际找到 {matches.Count} 处，补丁未应用。");
                return codes;
            }

            // 从后向前插入，避免前一次插入改变后续索引。
            for (int matchIndex = matches.Count - 1; matchIndex >= 0; matchIndex--)
            {
                int index = matches[matchIndex];
                CodeInstruction originalCall = codes[index];
                CodeInstruction loadEquipmentDrawPos =
                    new CodeInstruction(OpCodes.Ldarg_1);

                // 若未来编译器把分支标签或异常块挂在 getter 上，入口必须随新增的
                // ldarg.1 一起前移，否则可能跳过参数压栈并破坏 IL 栈平衡。
                loadEquipmentDrawPos.labels.AddRange(originalCall.labels);
                originalCall.labels.Clear();
                loadEquipmentDrawPos.blocks.AddRange(originalCall.blocks);
                originalCall.blocks.Clear();

                codes.Insert(index, loadEquipmentDrawPos);
                originalCall.opcode = OpCodes.Call;
                originalCall.operand = helperMethod;
            }

            return codes;
        }
    }
}
