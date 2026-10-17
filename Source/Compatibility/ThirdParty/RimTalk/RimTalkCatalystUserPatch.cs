using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.RimTalk
{
    internal static class RimTalkCatalystUserPatch
    {
        internal static bool Enabled;

        internal static bool IsColonistOrAuthorizedCatalystUser(Pawn? pawn)
        {
            if (pawn == null) return false;
            if (pawn.IsColonistPlayerControlled) return true;

            return Enabled
                && Scribe.mode == LoadSaveMode.Inactive
                && !MechanoidMechanitorPostLoadSafetyCoordinator.ShouldDeferPositiveRestore
                && GameComponent_MechanoidMechanitorRegistry.IsPawnAliveAndInitialized(pawn)
                && pawn.Spawned
                && !pawn.InMentalState
                && !pawn.IsPrisoner
                && !pawn.IsSlave
                && pawn.HostFaction == null
                && MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn)
                // null 不开放仅限特定机械师植入体的特例；复用完整道具使用资格。
                && ColonistLikeCompUsableUtility.IsAuthorizedMechanicalCompUsableUser(pawn, null);
        }

        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            MethodInfo? getter = AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.IsColonistPlayerControlled));
            MethodInfo? helper = AccessTools.DeclaredMethod(typeof(RimTalkCatalystUserPatch),
                nameof(IsColonistOrAuthorizedCatalystUser), new[] { typeof(Pawn) });
            if (getter == null || helper == null || !helper.IsStatic || helper.ReturnType != typeof(bool))
                throw new InvalidOperationException("RimTalk 催化剂：无法解析使用者身份判断或兼容 helper。");

            int index = -1;
            int count = 0;
            for (int i = 0; i < codes.Count; i++)
            {
                if (!codes[i].Calls(getter)) continue;
                index = i;
                count++;
            }
            if (count != 1)
                throw new InvalidOperationException($"RimTalk 催化剂：使用者身份判断预期 1 处，实际 {count} 处。");

            int pawnLoad = index - 1;
            while (pawnLoad >= 0 && codes[pawnLoad].opcode == OpCodes.Nop) pawnLoad--;
            if (pawnLoad < 0 || codes[pawnLoad].opcode != OpCodes.Ldarg_1)
                throw new InvalidOperationException("RimTalk 催化剂：身份判断不再直接读取 DoEffectOn 的 user 参数。");

            // Pawn -> bool 的同栈替换；保留标签、异常块、目标及其余全部派工指令。
            codes[index].opcode = OpCodes.Call;
            codes[index].operand = helper;
            return codes;
        }
    }
}
