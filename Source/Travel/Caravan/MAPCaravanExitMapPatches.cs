using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 让已注册且符合资格的机械族机械师，在非殖民地地图的绿色边缘撤离区
    /// 像普通殖民者一样离开地图并创建或加入远行队。
    /// </summary>
    [HarmonyPatch(
        typeof(FloatMenuOptionProvider_DraftedMove),
        nameof(FloatMenuOptionProvider_DraftedMove.PawnGotoAction))]
    public static class MAPCaravanExitMap_PawnGotoAction_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(IntVec3 clickCell, Pawn pawn, IntVec3 gotoLoc)
        {
            if (pawn?.Map == null || pawn.jobs == null)
            {
                return;
            }

            if (!MAPTravelUtility.IsEligibleRegisteredMechanoidMechanitor(pawn))
            {
                return;
            }

            if (!pawn.Map.exitMapGrid.IsExitCell(clickCell))
            {
                return;
            }

            Job? curJob = pawn.CurJob;
            if (curJob == null
                || curJob.def != JobDefOf.Goto
                || curJob.targetA.Cell != gotoLoc)
            {
                return;
            }

            curJob.exitMapOnArrival = true;
            MechanicalFlightMapExitUtility.NotifyJobStarted(pawn, curJob);
        }
    }

    [HarmonyPatch(
        typeof(CaravanExitMapUtility),
        nameof(CaravanExitMapUtility.CanExitMapAndJoinOrCreateCaravanNow))]
    public static class MAPCaravanExitMap_CanExitMapAndJoinOrCreateCaravanNow_Patch
    {
        private const int ErrorKeyMatchCount = 879347001;

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return MAPCaravanExitMapTranspilerUtility.ReplaceSingleIsColonistCall(
                instructions,
                nameof(CaravanExitMapUtility.CanExitMapAndJoinOrCreateCaravanNow),
                ErrorKeyMatchCount);
        }
    }

    [HarmonyPatch(
        typeof(CaravanExitMapUtility),
        nameof(CaravanExitMapUtility.ExitMapAndJoinOrCreateCaravan))]
    public static class MAPCaravanExitMap_ExitMapAndJoinOrCreateCaravan_Patch
    {
        private const int ErrorKeyMatchCount = 879347002;

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return MAPCaravanExitMapTranspilerUtility.ReplaceSingleIsColonistCall(
                instructions,
                nameof(CaravanExitMapUtility.ExitMapAndJoinOrCreateCaravan),
                ErrorKeyMatchCount);
        }
    }

    internal static class MAPCaravanExitMapTranspilerUtility
    {
        private const string LogPrefix =
            "[MAP-机械族机械师] MAPCaravanExitMapPatches：";

        private const int ErrorKeyResolveMembers = 879347000;

        internal static IEnumerable<CodeInstruction> ReplaceSingleIsColonistCall(
            IEnumerable<CodeInstruction> instructions,
            string targetMethodName,
            int errorKeyMatchCount)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            MethodInfo? isColonistGetter = AccessTools.PropertyGetter(
                typeof(Pawn),
                nameof(Pawn.IsColonist));
            MethodInfo? helperMethod = AccessTools.Method(
                typeof(MAPTravelUtility),
                nameof(MAPTravelUtility.IsColonistOrEligibleMechanoidMechanitor));

            if (isColonistGetter == null || helperMethod == null)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}无法解析 Pawn.IsColonist / " +
                    "IsColonistOrEligibleMechanoidMechanitor，" +
                    $"目标方法 {targetMethodName} 未应用机械族机械师边缘撤离补丁。",
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
                    $"{LogPrefix}{targetMethodName} 中 Pawn.IsColonist 属性读取方法 " +
                    $"预期仅 1 处，实际匹配 {matchCount} 处，" +
                    "未应用机械族机械师边缘撤离补丁。",
                    errorKeyMatchCount);
                return codes;
            }

            CodeInstruction instruction = codes[matchIndex];
            instruction.opcode = OpCodes.Call;
            instruction.operand = helperMethod;
            return codes;
        }
    }
}
