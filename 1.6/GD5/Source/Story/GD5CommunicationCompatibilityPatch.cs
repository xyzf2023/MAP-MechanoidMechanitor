using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor.GD5
{
    internal static class GD5CommunicationCompatibilityPatch
    {
        private static bool IsHiveAlly =>
            GD5StoryFlowService.GetHiveRelation() == GD5DialogueCondition.HiveAlly;

        // 只替换传入的已解锁交易说明，不覆盖尚未建立合作时的提示。
        internal static void TradeWindowPrefix(ref string description)
        {
            if (GD5StoryFlowService.IsEnabled
                && description == "GD.BlackMechTradeDescription".Translate().ToString())
                description = "MAP_GD5.Communication.TradeDescription".Translate();
        }

        internal static void ShouldPayTaxPostfix(ref bool __result)
        {
            if (IsHiveAlly) __result = false;
        }

        private static string ResolveTaxTextKey(string originalKey)
        {
            return IsHiveAlly ? "MAP_GD5.Communication.HiveAllyTaxExempt" : originalKey;
        }

        // 仅替换当前通讯窗口的税款文案键，不全局覆盖翻译或重写窗口布局。
        internal static IEnumerable<CodeInstruction> TaxTextTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = instructions.ToList();
            string[] keys = { "GD.EmpireTaxTip", "GD.EmpireTaxHostile" };
            if (keys.Any(key => codes.Count(c => c.opcode == OpCodes.Ldstr && Equals(c.operand, key)) != 1))
                throw new InvalidOperationException("闪毁5通讯窗口的税款文案结构已变化。");
            var resolver = AccessTools.DeclaredMethod(typeof(GD5CommunicationCompatibilityPatch), nameof(ResolveTaxTextKey));
            foreach (CodeInstruction code in codes)
            {
                yield return code;
                if (code.opcode == OpCodes.Ldstr && keys.Contains(code.operand as string))
                    yield return new CodeInstruction(OpCodes.Call, resolver);
            }
        }
    }
}
