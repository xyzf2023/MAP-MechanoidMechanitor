using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    // 只扩展复活剂效果派工时的使用者资格；目标、工作及消耗均保留原版逻辑。
    [HarmonyPatch]
    internal static class ColonistLikeResurrectPatches
    {
        private const string LogPrefix = "[MAP-机械族机械师] 复活剂使用者资格：";

        private static MethodBase? TargetMethod() => AccessTools.Method(
            typeof(CompTargetEffect_Resurrect),
            nameof(CompTargetEffect_Resurrect.DoEffectOn),
            new[] { typeof(Pawn), typeof(Thing) });

        private static bool Prepare()
        {
            if (TargetMethod() != null)
                return true;

            Log.ErrorOnce($"{LogPrefix}未找到 DoEffectOn(Pawn, Thing)，补丁未应用。", 879345431);
            return false;
        }

        private static bool IsColonistOrAuthorizedResurrectUser(Pawn pawn)
        {
            // null 禁止走仅限机械师植入体的特例；复活剂要求完整道具使用资格。
            return pawn.IsColonistPlayerControlled
                || ColonistLikeCompUsableUtility.IsAuthorizedMechanicalCompUsableUser(pawn, null);
        }

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            MethodInfo? getter = AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.IsColonistPlayerControlled));
            MethodInfo? helper = AccessTools.Method(
                typeof(ColonistLikeResurrectPatches), nameof(IsColonistOrAuthorizedResurrectUser));
            if (getter == null || helper == null)
            {
                Log.ErrorOnce($"{LogPrefix}无法解析使用者资格方法，补丁未应用。", 879345432);
                return codes;
            }

            int getterIndex = -1;
            int matches = 0;
            for (int i = 0; i < codes.Count; i++)
            {
                if (!codes[i].Calls(getter))
                    continue;
                getterIndex = i;
                matches++;
            }

            // 确认唯一判断读取的是参数 user，结构变化时保留输入，避免误扩展其他 Pawn。
            if (matches != 1 || getterIndex < 1 || codes[getterIndex - 1].opcode != OpCodes.Ldarg_1)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}DoEffectOn 中 user.IsColonistPlayerControlled 结构不符（属性调用 {matches} 处），补丁未应用。",
                    879345433);
                return codes;
            }

            // 同栈签名 Pawn -> bool，原位保留分支标签、异常块以及其余全部指令。
            codes[getterIndex].opcode = OpCodes.Call;
            codes[getterIndex].operand = helper;
            return codes;
        }
    }
}
