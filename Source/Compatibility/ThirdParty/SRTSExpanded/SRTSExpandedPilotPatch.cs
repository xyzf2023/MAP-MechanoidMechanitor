using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MAP_MechanoidMechanitor;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.SRTSExpanded
{
    /// <summary>
    /// SRTS Expanded 驾驶员资格兼容补丁。
    /// 只替换 CompLaunchableSRTS.IsPilot(Pawn) 中唯一一处 Pawn.IsFreeColonist 调用；
    /// 原版自由殖民者完全保持原行为，额外放行拥有 MAP ShuttlePilot 能力的合法玩家机械体。
    /// SRTS 后续的 PilotingAbility、最低驾驶值、驾驶员人数及其他起飞规则全部保留。
    /// </summary>
    internal static class SRTSExpandedPilotPatch
    {
        private const string LogPrefix =
            "[MAP-机械族机械师] 第三方兼容（SRTS Expanded）：";

        internal static IEnumerable<CodeInstruction> TranspilerIsPilot(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            MethodInfo? getter = AccessTools.PropertyGetter(
                typeof(Pawn),
                nameof(Pawn.IsFreeColonist));
            MethodInfo? helper = AccessTools.Method(
                typeof(SRTSExpandedPilotPatch),
                nameof(IsFreeColonistOrAuthorizedMAPShuttlePilot));

            if (getter == null || helper == null)
            {
                throw new InvalidOperationException(
                    $"{LogPrefix}无法解析 Pawn.IsFreeColonist 属性读取方法 或本 MOD 辅助方法，补丁未应用。");
            }

            int matchCount = 0;
            int getterIndex = -1;
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].Calls(getter))
                {
                    matchCount++;
                    getterIndex = i;
                }
            }

            if (matchCount != 1)
            {
                throw new InvalidOperationException(
                    $"{LogPrefix}CompLaunchableSRTS.IsPilot(Pawn) 中 Pawn.IsFreeColonist 调用" +
                    $"预期仅 1 处，实际找到 {matchCount} 处，结构已变化，兼容安全跳过。");
            }

            if (codes[getterIndex].blocks.Count > 0)
            {
                throw new InvalidOperationException(
                    $"{LogPrefix}IsPilot(Pawn) 中 IsFreeColonist 调用位于 异常处理块，" +
                    "无法安全扩展，兼容安全跳过。");
            }

            // 原 getter 与辅助方法都消费一个 Pawn、返回一个 bool，因此可以原位替换调用目标，
            // 无需改动周围分支、标签或求值栈结构。
            codes[getterIndex].opcode = OpCodes.Call;
            codes[getterIndex].operand = helper;

            return codes;
        }

        internal static bool IsFreeColonistOrAuthorizedMAPShuttlePilot(Pawn? pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            // 完整保留 SRTS 对普通自由殖民者的既有行为。
            if (pawn.IsFreeColonist)
            {
                return true;
            }

            // 额外资格只授予合法、可操作的玩家机械体；不把普通玩家机械体宽泛视为驾驶员。
            if (pawn.RaceProps?.IsMechanoid != true
                || pawn.Faction == null
                || !pawn.Faction.IsPlayerSafe()
                || pawn.Dead
                || pawn.Destroyed
                || pawn.Downed)
            {
                return false;
            }

            // 资格来源统一走本 MOD 能力层：正式机械族机械师与其他被明确授予
            // ShuttlePilot 能力的机械体均可通过；兼容层不自行识别具体 PawnDef 或身份来源。
            return MechanoidMechanitorRoleUtility.AllowsShuttlePilot(pawn);
        }
    }
}
