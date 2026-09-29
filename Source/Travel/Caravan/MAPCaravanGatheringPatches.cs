using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 让拥有远行队物资收集能力的机械族，
    /// 像普通殖民者一样获得 PrepareCaravan_GatherItems Duty。
    /// </summary>
    [HarmonyPatch(
        typeof(LordToil_PrepareCaravan_GatherItems),
        nameof(LordToil_PrepareCaravan_GatherItems.UpdateAllDuties))]
    public static class MAPLordToilPrepareCaravanGatherItemsPatch
    {
        private const int ErrorKeyMatchCount = 879348021;

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return MAPCaravanGatheringTranspilerUtility
                .ReplaceSingleIsColonistCall(
                    instructions,
                    nameof(LordToil_PrepareCaravan_GatherItems)
                        + "."
                        + nameof(LordToil_PrepareCaravan_GatherItems.UpdateAllDuties),
                    ErrorKeyMatchCount);
        }
    }

    /// <summary>
    /// 让远行队物资完成判定等待所有机械搬运者完成收集，
    /// 避免机械族尚未完成搬运时过早进入下一阶段。
    /// </summary>
    [HarmonyPatch(
        typeof(CaravanFormingUtility),
        nameof(CaravanFormingUtility.AllItemsLoadedOntoCaravan))]
    public static class MAPCaravanAllItemsLoadedPatch
    {
        private const int ErrorKeyMatchCount = 879348002;

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return MAPCaravanGatheringTranspilerUtility
                .ReplaceSingleIsColonistCall(
                    instructions,
                    nameof(CaravanFormingUtility)
                        + "."
                        + nameof(CaravanFormingUtility.AllItemsLoadedOntoCaravan),
                    ErrorKeyMatchCount);
        }
    }

    internal static class MAPCaravanGatheringTranspilerUtility
    {
        private const string LogPrefix =
            "[MAP-机械族机械师] MAPCaravanGatheringPatches：";

        private const int ErrorKeyResolveMembers = 879348000;

        internal static IEnumerable<CodeInstruction>
            ReplaceSingleIsColonistCall(
                IEnumerable<CodeInstruction> instructions,
                string targetMethodName,
                int errorKeyMatchCount)
        {
            List<CodeInstruction> codes =
                new List<CodeInstruction>(instructions);

            MethodInfo? isColonistGetter =
                AccessTools.PropertyGetter(
                    typeof(Pawn),
                    nameof(Pawn.IsColonist));

            MethodInfo? collectorHelper =
                AccessTools.Method(
                    typeof(MAPTravelUtility),
                    nameof(MAPTravelUtility.CanActAsCaravanCollector));

            if (isColonistGetter == null || collectorHelper == null)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}无法解析 Pawn.IsColonist 或 "
                    + "MAPTravelUtility.CanActAsCaravanCollector，"
                    + $"目标方法 {targetMethodName} 保留原版行为。",
                    ErrorKeyResolveMembers);

                return codes;
            }

            int matchCount = 0;
            int matchIndex = -1;

            for (int i = 0; i < codes.Count; i++)
            {
                if (!codes[i].Calls(isColonistGetter))
                {
                    continue;
                }

                matchCount++;
                matchIndex = i;
            }

            if (matchCount != 1)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}{targetMethodName} 中 "
                    + "Pawn.IsColonist 属性读取方法 预期仅有 1 处，"
                    + $"实际匹配 {matchCount} 处。"
                    + "本次不进行替换并保留原版行为。",
                    errorKeyMatchCount);

                return codes;
            }

            CodeInstruction instruction = codes[matchIndex];
            instruction.opcode = OpCodes.Call;
            instruction.operand = collectorHelper;

            return codes;
        }
    }
}
