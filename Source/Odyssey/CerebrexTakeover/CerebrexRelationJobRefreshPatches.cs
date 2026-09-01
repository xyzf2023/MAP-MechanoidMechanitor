using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 仅替换 Lord.Notify_FactionRelationsChanged 末尾对 Pawn_JobTracker.EndCurrentJob 的调用，
    /// 将其重定向到 CerebrexRelationJobRefreshBatch.EndCurrentJobOrQueue，便于在接管关系同步批次内
    /// 收集并去重工作刷新请求。保留 CheckTransitionOnSignal 与原有遍历结构。
    /// </summary>
    [HarmonyPatch(typeof(Lord), nameof(Lord.Notify_FactionRelationsChanged))]
    public static class Lord_NotifyFactionRelationsChanged_JobRefreshPatch
    {
        public static bool Prepare() => ModsConfig.OdysseyActive;

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo? endCurrentJob = AccessTools.Method(
                typeof(Pawn_JobTracker),
                nameof(Pawn_JobTracker.EndCurrentJob),
                new[] { typeof(JobCondition), typeof(bool), typeof(bool) });

            if (endCurrentJob == null)
            {
                Log.Warning(
                    "[MAP-机械族机械师] 主脑关系同步批次：无法定位 Pawn_JobTracker.EndCurrentJob，"
                    + "已完整回退原版指令。");
                foreach (CodeInstruction instruction in instructions)
                {
                    yield return instruction;
                }

                yield break;
            }

            MethodInfo wrapper = AccessTools.Method(
                typeof(CerebrexRelationJobRefreshBatch),
                nameof(CerebrexRelationJobRefreshBatch.EndCurrentJobOrQueue))!;

            List<CodeInstruction> original = new List<CodeInstruction>(instructions);

            bool Match(CodeInstruction instruction, out MethodInfo? called)
            {
                called = null;
                if ((instruction.opcode != OpCodes.Call && instruction.opcode != OpCodes.Callvirt)
                    || instruction.operand is not MethodInfo method)
                {
                    return false;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (method.DeclaringType != typeof(Pawn_JobTracker)
                    || method.Name != nameof(Pawn_JobTracker.EndCurrentJob)
                    || method.ReturnType != typeof(void)
                    || parameters.Length != 3
                    || parameters[0].ParameterType != typeof(JobCondition)
                    || parameters[1].ParameterType != typeof(bool)
                    || parameters[2].ParameterType != typeof(bool))
                {
                    return false;
                }

                called = method;
                return true;
            }

            int matchCount = 0;
            foreach (CodeInstruction instruction in original)
            {
                if (Match(instruction, out _))
                {
                    matchCount++;
                }
            }

            if (matchCount != 1)
            {
                Log.Warning(
                    "[MAP-机械族机械师] 主脑关系同步批次 Transpiler 未能唯一匹配 EndCurrentJob 调用"
                    + "（匹配数=" + matchCount + "），已完整回退原版指令，不影响原版行为。");
                foreach (CodeInstruction instruction in original)
                {
                    yield return instruction;
                }

                yield break;
            }

            foreach (CodeInstruction instruction in original)
            {
                if (Match(instruction, out _))
                {
                    CodeInstruction replacement = new CodeInstruction(instruction)
                    {
                        opcode = OpCodes.Call,
                        operand = wrapper
                    };
                    yield return replacement;
                }
                else
                {
                    yield return instruction;
                }
            }
        }
    }
}
