using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.MiliraRace
{
    internal static class MiliraAutoSuitCompatibilityPatch
    {
        internal static bool Enabled;

        internal static bool IsColonistOrAuthorizedMechanitor(Pawn? pawn)
        {
            if (pawn == null)
                return false;
            if (pawn.IsColonist)
                return true;

            // 工作调度仍负责工作类型、行动能力和优先级；这里只补充正式身份。
            return Enabled && Scribe.mode == LoadSaveMode.Inactive
                && !MechanoidMechanitorPostLoadSafetyCoordinator.ShouldDeferPositiveRestore
                && pawn.Spawned && !pawn.Destroyed && !pawn.Dead && !pawn.Discarded
                && pawn.Faction?.IsPlayer == true && pawn.HostFaction == null
                && !pawn.IsPrisoner && !pawn.IsSlave
                && MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn);
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo? getter = AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.IsColonist));
            MethodInfo? helper = AccessTools.DeclaredMethod(typeof(MiliraAutoSuitCompatibilityPatch),
                nameof(IsColonistOrAuthorizedMechanitor), new[] { typeof(Pawn) });
            if (getter == null || helper == null || !helper.IsStatic || helper.ReturnType != typeof(bool))
                throw new InvalidOperationException("米莉安自动换装：无法解析操作者身份判断。");

            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            int match = -1;
            for (int i = 0; i < codes.Count; i++)
            {
                if (!codes[i].Calls(getter)) continue;
                if (match >= 0)
                    throw new InvalidOperationException("米莉安自动换装：IsColonist 判断不唯一。");
                match = i;
            }
            if (match < 0)
                throw new InvalidOperationException("米莉安自动换装：未找到 IsColonist 判断。");

            int pawnLoad = match - 1;
            while (pawnLoad >= 0 && codes[pawnLoad].opcode == OpCodes.Nop) pawnLoad--;
            if (pawnLoad < 0 || codes[pawnLoad].opcode != OpCodes.Ldarg_1)
                throw new InvalidOperationException("米莉安自动换装：身份判断不再直接读取工作者参数。");
            if (codes[match].blocks.Count > 0)
                throw new InvalidOperationException("米莉安自动换装：身份判断位于异常块边界。");

            // 输入输出与原 getter 完全一致，保留全部目标、禁止及预留判断。
            codes[match].opcode = OpCodes.Call;
            codes[match].operand = helper;
            return codes;
        }
    }
}
