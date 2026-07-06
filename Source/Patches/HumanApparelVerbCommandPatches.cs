using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch]
    public static class HumanApparelVerbCommandPatches
    {
        private const string LogPrefix = "[MAP-机械族机械师] HumanApparelVerbCommandPatches：";

        private const int ErrorKeyTargetMethodNotFound = 879345411;
        private const int ErrorKeyResolveFailed = 879345412;
        private const int ErrorKeyMatchCount = 879345413;

        private static MethodInfo? cachedCreateVerbTargetCommandMethod;

        private static MethodBase? TargetMethod()
        {
            MethodInfo? method = GetCreateVerbTargetCommandMethod();
            if (method == null)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}未找到 CompApparelVerbOwner.CreateVerbTargetCommand(Thing, Verb)，补丁未应用。",
                    ErrorKeyTargetMethodNotFound);
            }

            return method;
        }

        private static bool Prepare()
        {
            return GetCreateVerbTargetCommandMethod() != null;
        }

        private static MethodInfo? GetCreateVerbTargetCommandMethod()
        {
            if (cachedCreateVerbTargetCommandMethod != null)
            {
                return cachedCreateVerbTargetCommandMethod;
            }

            cachedCreateVerbTargetCommandMethod = AccessTools.Method(
                typeof(CompApparelVerbOwner),
                "CreateVerbTargetCommand",
                new[] { typeof(Thing), typeof(Verb) });

            return cachedCreateVerbTargetCommandMethod;
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            MethodInfo? isColonistPlayerControlledGetter = AccessTools.PropertyGetter(
                typeof(Pawn),
                nameof(Pawn.IsColonistPlayerControlled));
            MethodInfo? helperMethod = AccessTools.Method(
                typeof(HumanApparelUtility),
                nameof(HumanApparelUtility.CanUseApparelVerbCommands));

            if (isColonistPlayerControlledGetter == null || helperMethod == null)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}无法解析 CreateVerbTargetCommand 相关方法，补丁未应用。",
                    ErrorKeyResolveFailed);
                return codes;
            }

            int matchCount = 0;
            int matchIndex = -1;

            for (int i = 0; i < codes.Count; i++)
            {
                if (!codes[i].Calls(isColonistPlayerControlledGetter))
                {
                    continue;
                }

                matchCount++;
                matchIndex = i;
            }

            if (matchCount != 1)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}CreateVerbTargetCommand 中 IsColonistPlayerControlled 调用预期仅 1 处，实际找到 {matchCount} 处，补丁未应用。",
                    ErrorKeyMatchCount);
                return codes;
            }

            CodeInstruction instruction = codes[matchIndex];
            instruction.opcode = OpCodes.Call;
            instruction.operand = helperMethod;

            return codes;
        }
    }
}
