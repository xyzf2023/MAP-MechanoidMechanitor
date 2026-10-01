using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.NewRatkin
{
    internal static class NewRatkinWanderingTraderMenuPatch
    {
        // 仅替换第三方菜单状态机中的调用，不修改 Pawn.IsColonist 本身。
        internal static bool IsColonistOrAvailableMechanitor(Pawn? pawn)
        {
            if (pawn == null)
                return false;
            if (pawn.IsColonist)
                return true;

            return NewRatkinCompatibilityUtility.IsPlayerMechanitor(pawn)
                && pawn.Spawned
                && !pawn.InMentalState
                && pawn.story != null
                && pawn.skills != null
                && CompColonistLikeFloatMenuUser.PawnCanUseColonistLikeFloatMenu(pawn);
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo? getter = AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.IsColonist));
            MethodInfo? helper = AccessTools.DeclaredMethod(typeof(NewRatkinWanderingTraderMenuPatch),
                nameof(IsColonistOrAvailableMechanitor), new[] { typeof(Pawn) });
            if (getter == null || helper == null)
                throw new InvalidOperationException("鼠族游商：无法解析殖民者判断或交互资格辅助方法。");

            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            int match = -1;
            for (int i = 0; i < codes.Count; i++)
            {
                if (!codes[i].Calls(getter))
                    continue;
                if (match >= 0)
                    throw new InvalidOperationException("鼠族游商菜单：IsColonist 判断不唯一。");
                match = i;
            }
            if (match < 0)
                throw new InvalidOperationException("鼠族游商菜单：未找到预期的 IsColonist 判断。");

            // 实例 getter 和静态 helper 均消费一个 Pawn 并返回 bool；原地修改保留标签与异常块。
            codes[match].opcode = OpCodes.Call;
            codes[match].operand = helper;
            return codes;
        }
    }
}
