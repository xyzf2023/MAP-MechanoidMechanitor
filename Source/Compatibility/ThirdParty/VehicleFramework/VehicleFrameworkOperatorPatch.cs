using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.VehicleFramework
{
    internal static class VehicleFrameworkOperatorPatch
    {
        internal static bool Enabled;

        internal static bool IsColonyMechWithoutOperatorAuthorization(Pawn pawn, int handlingType)
        {
            bool original = pawn.IsColonyMech;
            if (!original) return false;

            return !(Enabled
                && handlingType != 0 && (handlingType & ~3) == 0
                && Scribe.mode == LoadSaveMode.Inactive
                && !MechanoidMechanitorPostLoadSafetyCoordinator.ShouldDeferPositiveRestore
                && GameComponent_MechanoidMechanitorRegistry.IsPawnAliveAndInitialized(pawn)
                && pawn.RaceProps?.IsMechanoid == true
                && pawn.Faction != null && pawn.Faction.IsPlayerSafe()
                && pawn.HostFaction == null && !pawn.IsPrisoner && !pawn.IsSlave
                && pawn.health?.capacities != null && pawn.needs != null
                && !pawn.Downed && !pawn.InMentalState
                && MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn)
                && MechanoidMechanitorRoleUtility.AllowsShuttlePilot(pawn)
                // 不能用 IsColonyMechPlayerControlled：其 Spawned 条件会让已上车驾驶员失效。
                // 复用现有监管状态与征召许可，包含低电关机等限制，不创建 Tracker 或授权。
                && pawn.OverseerSubject?.State == OverseerSubjectState.Overseen
                && MechanitorUtility.CanDraftMech(pawn).Accepted);
        }

        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            MethodInfo? mechGetter = AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.IsColonyMech));
            MethodInfo? prisonerGetter = AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.IsPrisoner));
            MethodInfo? helper = AccessTools.DeclaredMethod(typeof(VehicleFrameworkOperatorPatch),
                nameof(IsColonyMechWithoutOperatorAuthorization), new[] { typeof(Pawn), typeof(int) });
            if (mechGetter == null || prisonerGetter == null || helper == null
                || !helper.IsStatic || helper.ReturnType != typeof(bool))
                throw new InvalidOperationException("VF 操作资格：无法解析机械体、囚犯判断或兼容 helper。");

            List<int> significant = new List<int>();
            int mechCount = 0, prisonerCount = 0, match = -1;
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode != OpCodes.Nop) significant.Add(i);
                if (codes[i].Calls(mechGetter)) mechCount++;
                if (codes[i].Calls(prisonerGetter)) prisonerCount++;
            }

            // 已核对 VF 1.6：HandlingType.None 在方法开头直接通过，乘客不进入身份检查。
            if (significant.Count < 4
                || codes[significant[0]].opcode != OpCodes.Ldarg_1
                || !IsBranchTrue(codes[significant[1]])
                || codes[significant[1]].operand is not Label operatorStart
                || codes[significant[2]].opcode != OpCodes.Ldc_I4_1
                || codes[significant[3]].opcode != OpCodes.Ret
                || codes.FindIndex(c => c.labels.Contains(operatorStart)) <= significant[3])
                throw new InvalidOperationException("VF 操作资格：普通乘客的早退结构已变化，兼容安全停用。");

            // 仅接受 pawn.IsPrisoner || pawn.IsColonyMech 的唯一直接短路拒绝块。
            for (int n = 1; n + 6 < significant.Count; n++)
            {
                if (!codes[significant[n]].Calls(prisonerGetter)) continue;
                CodeInstruction prisonerBranch = codes[significant[n + 1]];
                CodeInstruction mechBranch = codes[significant[n + 4]];
                if (codes[significant[n - 1]].opcode != OpCodes.Ldarg_0
                    || !IsBranchTrue(prisonerBranch)
                    || prisonerBranch.operand is not Label rejected
                    || codes[significant[n + 2]].opcode != OpCodes.Ldarg_0
                    || !codes[significant[n + 3]].Calls(mechGetter)
                    || (mechBranch.opcode != OpCodes.Brfalse && mechBranch.opcode != OpCodes.Brfalse_S)
                    || mechBranch.operand is not Label accepted
                    || codes[significant[n + 5]].opcode != OpCodes.Ldc_I4_0
                    || !codes[significant[n + 5]].labels.Contains(rejected)
                    || codes[significant[n + 6]].opcode != OpCodes.Ret
                    || codes.FindIndex(c => c.labels.Contains(accepted)) <= significant[n + 6])
                    continue;

                for (int i = significant[n - 1]; i <= significant[n + 6]; i++)
                {
                    bool isRejection = i == significant[n + 5];
                    if (codes[i].blocks.Count != 0
                        || (i > significant[n - 1] && !isRejection && codes[i].labels.Count != 0))
                        throw new InvalidOperationException("VF 操作资格：身份拒绝块内部的跳转或异常块已变化。");
                }
                match = significant[n + 3];
            }
            if (mechCount != 1 || prisonerCount != 1 || match < 0)
                throw new InvalidOperationException(
                    $"VF 操作资格：身份拒绝块不再唯一或结构变化（机械体 {mechCount}，囚犯 {prisonerCount}）。");

            // 仅向原 getter 的 Pawn 参数补入角色枚举；其余指令、分支和元数据保持原样。
            codes[match].opcode = OpCodes.Call;
            codes[match].operand = helper;
            codes.Insert(match, new CodeInstruction(OpCodes.Ldarg_1));
            return codes;
        }

        private static bool IsBranchTrue(CodeInstruction code) =>
            code.opcode == OpCodes.Brtrue || code.opcode == OpCodes.Brtrue_S;
    }
}
