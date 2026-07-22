using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 原版 <see cref="CompOverseerSubject.TryMakeFeral"/> 在机械族长时间缺乏机械师监管后脱离控制时，
    /// 会无条件发送红色 <see cref="LetterDefOf.ThreatBig"/> 威胁信件。
    /// 本补丁仅在玩家派系与原版机械巢当前为非敌对关系时，将该信件改为灰色
    /// <see cref="LetterDefOf.NeutralEvent"/> 并替换正文，同时保留原版标题、分类计数名单与 LookTargets。
    /// 敌对状态下完全保持原版红色信件与原版正文。
    /// </summary>
    [HarmonyPatch(typeof(CompOverseerSubject), nameof(CompOverseerSubject.TryMakeFeral))]
    public static class MAPFeralLetterNeutralPatches
    {
        private const string LogPrefix = "[MAP-机械族机械师] MAPFeralLetterNeutralPatches：";

        /// <summary>
        /// 独立的派系关系判断，仅区分“敌对”与“非敌对”，不依赖肃清协议、剧情配置或好感度数值。
        /// </summary>
        private static bool ShouldUseNeutralFeralLetter()
        {
            Faction? player = Faction.OfPlayerSilentFail;
            Faction? mechHive = Faction.OfMechanoids;
            if (player == null || mechHive == null)
            {
                return false;
            }

            return !player.HostileTo(mechHive) && !mechHive.HostileTo(player);
        }

        /// <summary>
        /// 返回正文翻译键：非敌对使用 MOD 中性文案，敌对沿用原版键。
        /// 后续原版 <c>Translate(Faction.OfMechanoids, 分类计数名单)</c> 的参数传递保持不变。
        /// </summary>
        private static string GetFeralLetterTextKey()
        {
            return ShouldUseNeutralFeralLetter()
                ? "MAP_MechanoidMechanitor.LetterMechsReclaimed"
                : "LetterMechsFeral";
        }

        /// <summary>
        /// 返回信件类型：非敌对为中性信封，敌对为原版红色威胁信件。
        /// </summary>
        private static LetterDef GetFeralLetterDef()
        {
            return ShouldUseNeutralFeralLetter()
                ? LetterDefOf.NeutralEvent
                : LetterDefOf.ThreatBig;
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            FieldInfo? threatBigField = AccessTools.Field(typeof(LetterDefOf), nameof(LetterDefOf.ThreatBig));
            MethodInfo? textKeyHelper = AccessTools.Method(
                typeof(MAPFeralLetterNeutralPatches),
                nameof(GetFeralLetterTextKey));
            MethodInfo? letterDefHelper = AccessTools.Method(
                typeof(MAPFeralLetterNeutralPatches),
                nameof(GetFeralLetterDef));

            if (threatBigField == null || textKeyHelper == null || letterDefHelper == null)
            {
                Log.Error($"{LogPrefix}未能解析目标字段或辅助方法（ThreatBig={threatBigField != null}，"
                    + $"文本辅助={textKeyHelper != null}，信件辅助={letterDefHelper != null}），补丁未应用。");
                return codes;
            }

            // 定位信件发送区段：唯一的 LetterStack.ReceiveLetter 调用。
            int receiveLetterCount = 0;
            int receiveLetterIndex = -1;
            for (int i = 0; i < codes.Count; i++)
            {
                CodeInstruction instruction = codes[i];
                if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt)
                    && instruction.operand is MethodInfo method
                    && method.DeclaringType == typeof(LetterStack)
                    && method.Name == nameof(LetterStack.ReceiveLetter))
                {
                    receiveLetterCount++;
                    receiveLetterIndex = i;
                }
            }

            // 定位正文键常量：唯一的 ldstr "LetterMechsFeral"（不会误匹配标题键 "LetterLabelMechsFeral"）。
            int textKeyCount = 0;
            int textKeyIndex = -1;
            for (int i = 0; i < codes.Count; i++)
            {
                CodeInstruction instruction = codes[i];
                if (instruction.opcode == OpCodes.Ldstr
                    && instruction.operand is string operand
                    && operand == "LetterMechsFeral")
                {
                    textKeyCount++;
                    textKeyIndex = i;
                }
            }

            // 定位信件类型：唯一的 ldsfld LetterDefOf.ThreatBig。
            int letterDefCount = 0;
            int letterDefIndex = -1;
            for (int i = 0; i < codes.Count; i++)
            {
                CodeInstruction instruction = codes[i];
                if (instruction.opcode == OpCodes.Ldsfld
                    && instruction.operand is FieldInfo field
                    && field.Equals(threatBigField))
                {
                    letterDefCount++;
                    letterDefIndex = i;
                }
            }

            bool matchedUniquely = receiveLetterCount == 1
                && textKeyCount == 1
                && letterDefCount == 1
                && textKeyIndex < receiveLetterIndex
                && letterDefIndex < receiveLetterIndex;

            if (!matchedUniquely)
            {
                // 任一目标匹配失败、缺失或多次匹配时，不做任何部分修改，原样返回原版 IL。
                Log.Error($"{LogPrefix}匹配失败，补丁未应用（ReceiveLetter={receiveLetterCount}，"
                    + $"LetterMechsFeral={textKeyCount}，ThreatBig={letterDefCount}，"
                    + $"文本键索引={textKeyIndex}，信件类型索引={letterDefIndex}，"
                    + $"ReceiveLetter索引={receiveLetterIndex}）。");
                return codes;
            }

            // 就地改写以保留原指令的 labels 与 exception blocks，避免破坏跳转/异常结构。
            codes[textKeyIndex].opcode = OpCodes.Call;
            codes[textKeyIndex].operand = textKeyHelper;

            codes[letterDefIndex].opcode = OpCodes.Call;
            codes[letterDefIndex].operand = letterDefHelper;

            return codes;
        }
    }
}
