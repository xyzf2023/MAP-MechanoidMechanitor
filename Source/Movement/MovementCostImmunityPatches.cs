using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    // 组件与机动作战统一使用此入口；保留原版任务速度、跟随速度和耗时上下限。
    [HarmonyPatch(typeof(Pawn_PathFollower), "CostToMoveIntoCell",
        new[] { typeof(Pawn), typeof(IntVec3) })]
    internal static class MovementCostImmunityPatches
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            MethodInfo? gridCost = AccessTools.Method(typeof(PathGrid),
                nameof(PathGrid.CalculatedCostAt),
                new[] { typeof(IntVec3), typeof(bool), typeof(IntVec3), typeof(int?) });
            MethodInfo? buildingCost = AccessTools.Method(typeof(Building),
                nameof(Building.PathWalkCostFor), new[] { typeof(Pawn) });
            MethodInfo? terrainFactor = AccessTools.Method(typeof(Dictionary<string, float>),
                "TryGetValue", new[] { typeof(string), typeof(float).MakeByRefType() });
            MethodInfo? gridHelper = AccessTools.Method(typeof(MovementCostImmunityUtility),
                nameof(MovementCostImmunityUtility.CalculatedCostAt));
            MethodInfo? buildingHelper = AccessTools.Method(typeof(MovementCostImmunityUtility),
                nameof(MovementCostImmunityUtility.PathWalkCostFor));
            MethodInfo? factorHelper = AccessTools.Method(typeof(MovementCostImmunityUtility),
                nameof(MovementCostImmunityUtility.TryGetTerrainSpeedFactor));

            int gridIndex = FindUniqueCall(codes, gridCost);
            int buildingIndex = FindUniqueCall(codes, buildingCost);
            int factorIndex = FindUniqueCall(codes, terrainFactor);
            if (gridIndex < 0 || buildingIndex < 0 || factorIndex < 0
                || gridHelper == null || buildingHelper == null || factorHelper == null
                || codes[gridIndex].blocks.Count != 0
                || codes[buildingIndex].blocks.Count != 0
                || codes[factorIndex].blocks.Count != 0)
            {
                Log.ErrorOnce("[MAP-机械族机械师] 移动减速豁免：无法精确匹配原版移动成本调用，"
                    + "保留原方法，未应用本补丁。", 0x4D430001);
                return codes;
            }

            // 所有调用点验证完成后再修改，避免部分替换；保持原指令的分支标签。
            var result = new List<CodeInstruction>(codes.Count + 2);
            for (int i = 0; i < codes.Count; i++)
            {
                CodeInstruction code = codes[i];
                if (i == gridIndex || i == factorIndex)
                {
                    // 静态目标方法的参数 0 是 Pawn；将其追加到原调用的参数栈。
                    var loadPawn = new CodeInstruction(OpCodes.Ldarg_0);
                    loadPawn.labels.AddRange(code.labels);
                    code.labels.Clear();
                    result.Add(loadPawn);
                    code.opcode = OpCodes.Call;
                    code.operand = i == gridIndex ? gridHelper : factorHelper;
                }
                else if (i == buildingIndex)
                {
                    // 原实例调用的栈参数已经是 (Building, Pawn)，可直接替换。
                    code.opcode = OpCodes.Call;
                    code.operand = buildingHelper;
                }
                result.Add(code);
            }
            return result;
        }

        private static int FindUniqueCall(List<CodeInstruction> codes, MethodInfo? method)
        {
            if (method == null)
                return -1;

            int found = -1;
            for (int i = 0; i < codes.Count; i++)
            {
                if (!codes[i].Calls(method))
                    continue;
                if (found >= 0)
                    return -1;
                found = i;
            }
            return found;
        }
    }
}
