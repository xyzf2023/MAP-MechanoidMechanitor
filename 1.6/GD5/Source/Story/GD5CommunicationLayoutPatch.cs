using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using GD3;
using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor.GD5
{
    internal static class GD5CommunicationLayoutPatch
    {
        // 与原窗口绘制正文时的 Translate 参数保持一致，不提前修改正文或重复格式化姓名。
        private static float CalculateDescriptionHeight(string description, float width, Pawn pawn)
        {
            string displayed = GD5StoryFlowService.IsEnabled && pawn != null
                ? description.Translate(pawn.NameShortColored).ToString()
                : description;
            return Text.CalcHeight(displayed, width);
        }

        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = instructions.ToList();
            var calculateHeight = AccessTools.Method(typeof(Text), nameof(Text.CalcHeight),
                new[] { typeof(string), typeof(float) });
            var pawnField = AccessTools.Field(typeof(CommunicationWindow_BlackMech), "pawn");
            var replacement = AccessTools.DeclaredMethod(typeof(GD5CommunicationLayoutPatch),
                nameof(CalculateDescriptionHeight));
            if (calculateHeight == null || pawnField?.FieldType != typeof(Pawn) || replacement == null
                || codes.Count(c => c.Calls(calculateHeight)) != 1)
                throw new InvalidOperationException("闪毁5通讯正文高度计算结构已变化。");

            foreach (CodeInstruction code in codes)
            {
                if (!code.Calls(calculateHeight))
                {
                    yield return code;
                    continue;
                }

                // 原栈已有正文和宽度，再传入通讯者；保留原指令的跳转标签及异常块边界。
                var loadWindow = new CodeInstruction(code) { opcode = OpCodes.Ldarg_0, operand = null };
                yield return loadWindow;
                yield return new CodeInstruction(OpCodes.Ldfld, pawnField);
                yield return new CodeInstruction(OpCodes.Call, replacement);
            }
        }
    }
}
