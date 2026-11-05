using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.GD5Expansion;

internal static class LegacyShieldControlPatch
{
    private static bool enabled;
    private static readonly MethodInfo Overseer = ReflectionTargets.RequireMethod(typeof(MechanitorUtility),
        "GetOverseer", true, typeof(Pawn), typeof(Pawn));
    private static readonly MethodInfo Controlled = ReflectionTargets.RequireMethod(typeof(Pawn),
        "get_IsColonyMechPlayerControlled", false, typeof(bool));

    internal static void EnsureControlGate(ModContentPack expansion)
    {
        MethodInfo? target = null;
        MethodInfo patch = ReflectionTargets.RequireMethod(typeof(LegacyShieldControlPatch), nameof(Transpiler),
            true, typeof(IEnumerable<CodeInstruction>), typeof(IEnumerable<CodeInstruction>));
        Harmony harmony = new(ExpansionCompatibilityBootstrap.SourceId + ".shield");
        try
        {
            Type type = ReflectionTargets.RequireType(expansion, "MK3expand.CompAbilityEffect_PlaceShield");
            if (type.Assembly.GetName().Name != "MK3expand")
                throw new InvalidOperationException("守御投盾目标并非闪拓主程序集类型。");
            target = ReflectionTargets.RequireMethod(type, "GizmoDisabled", false,
                typeof(bool), typeof(string).MakeByRefType());
            List<CodeInstruction> original = PatchProcessor.GetOriginalInstructions(target).ToList();
            if (original.Count(code => code.Calls(Overseer)) == 0
                && original.Count(code => code.Calls(Controlled)) == 1)
            {
                ExpansionCompatibilityLog.Startup("守御投盾已原生检查玩家控制，无需旧版回退。");
                return;
            }
            // 安装前先完整验证这一处窄范围改写。
            _ = Transpiler(original).ToList();
            harmony.Patch(target, transpiler: new HarmonyMethod(patch));
            if (Harmony.GetPatchInfo(target)?.Transpilers.Count(entry => entry.PatchMethod == patch
                    && entry.owner == harmony.Id) != 1)
                throw new InvalidOperationException("无法确认投盾控制门槛补丁安装成功。");
            enabled = true;
            ExpansionCompatibilityLog.Startup("旧版守御投盾改用玩家控制判断；保留倒地及能量门槛。");
        }
        catch (Exception ex)
        {
            enabled = false;
            if (target != null)
            {
                try { harmony.Unpatch(target, patch); }
                catch (Exception rollback)
                {
                    ExpansionCompatibilityLog.Warning("守御投盾兼容回滚失败。", "投盾回滚异常。", rollback);
                }
            }
            ExpansionCompatibilityLog.Warning("守御投盾兼容加载失败。",
                "投盾目标结构不符，未启用该回退。", ex);
        }
    }

    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        List<CodeInstruction> codes = instructions.ToList();
        int[] matches = Enumerable.Range(0, codes.Count).Where(index => codes[index].Calls(Overseer)).ToArray();
        if (matches.Length != 1 || codes[matches[0]].blocks.Count != 0
            || codes.Any(code => code.Calls(Controlled)))
            throw new InvalidOperationException("投盾旧门槛必须恰好一次 GetOverseer 且尚无玩家控制判断。");
        int index = matches[0];
        int next = NextNonNop(codes, index + 1);
        if (next >= codes.Count) throw new InvalidOperationException("GetOverseer 后没有空值判断。");
        OpCode op = codes[next].opcode;
        if (op == OpCodes.Ldnull)
        {
            int compare = NextNonNop(codes, next + 1);
            if (compare >= codes.Count || codes[compare].opcode != OpCodes.Ceq
                || codes[next].blocks.Count != 0 || codes[compare].blocks.Count != 0
                || codes[next].labels.Count != 0 || codes[compare].labels.Count != 0)
                throw new InvalidOperationException("GetOverseer 返回值不再只用于空值比较。");
            // 将同一处空值比较改为 false 比较；标签与后续门槛保持不变。
            codes[next].opcode = OpCodes.Ldc_I4_0;
            codes[next].operand = null;
        }
        else if (op != OpCodes.Brtrue && op != OpCodes.Brtrue_S
                 && op != OpCodes.Brfalse && op != OpCodes.Brfalse_S)
            throw new InvalidOperationException("GetOverseer 返回值不再直接用于空值分支。");
        if (codes[next].blocks.Count != 0 || codes[next].labels.Count != 0)
            throw new InvalidOperationException("空值判断含外部跳入或异常块边界。");
        codes[index].opcode = OpCodes.Call;
        codes[index].operand = ReflectionTargets.RequireMethod(typeof(LegacyShieldControlPatch),
            nameof(HasControl), true, typeof(bool), typeof(Pawn));
        return codes;
    }

    // 安装成功前该辅助方法沿用原判断；即使撤销补丁失败留有残留，也不会提前生效。
    internal static bool HasControl(Pawn pawn) => enabled
        ? pawn != null && pawn.IsColonyMechPlayerControlled
        : MechanitorUtility.GetOverseer(pawn) != null;

    private static int NextNonNop(List<CodeInstruction> codes, int start)
    {
        while (start < codes.Count && codes[start].opcode == OpCodes.Nop) start++;
        return start;
    }
}
