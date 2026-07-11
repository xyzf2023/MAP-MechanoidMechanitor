using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 在机械族机械师专属玩法中，扩展 ColonistBar 远行队分组的殖民者头像筛选，
    /// 并为开启「头像显示」的普通机械体整合地图头像条目。
    /// 不修改 Pawn.IsColonist 的全局语义。
    /// </summary>
    [HarmonyPatch(typeof(ColonistBar), "CheckRecacheEntries")]
    public static class MechanoidMechanitorScenario_ColonistBar_CheckRecacheEntries_Patch
    {
        private const string LogPrefix = "[MAP-机械族机械师]";

        private const int MaxInstructionsAfterIsColonist = 8;

        private const int ErrorKeyCaravanPatchResolveFailed = 879345102;
        private const int ErrorKeyCaravanIsColonistMatchCount = 879345103;
        private const int ErrorKeyPortraitAppendResolveFailed = 879345104;
        private const int ErrorKeyPortraitAppendMatchCount = 879345106;

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            PatchCaravanIsColonistCheck(codes);
            InjectPortraitDisplayAppend(codes);

            return codes;
        }

        private static bool PatchCaravanIsColonistCheck(List<CodeInstruction> codes)
        {
            MethodInfo? isColonistGetter = AccessTools.PropertyGetter(
                typeof(Pawn),
                nameof(Pawn.IsColonist));
            MethodInfo? isColonySubhumanGetter = AccessTools.PropertyGetter(
                typeof(Pawn),
                nameof(Pawn.IsColonySubhumanPlayerControlled));
            MethodInfo? helperMethod = AccessTools.Method(
                typeof(MechanoidMechanitorScenarioFreeColonistUtility),
                nameof(MechanoidMechanitorScenarioFreeColonistUtility.CountsAsColonistForCaravanBar));

            if (isColonistGetter == null
                || isColonySubhumanGetter == null
                || helperMethod == null)
            {
                Log.ErrorOnce(
                    $"{LogPrefix} MechanoidMechanitorScenarioColonistBarPatches：无法解析 ColonistBar 远行队筛选相关方法，补丁未应用。",
                    ErrorKeyCaravanPatchResolveFailed);
                return false;
            }

            List<int> candidateIndices = new List<int>();

            for (int i = 0; i < codes.Count; i++)
            {
                if (!codes[i].Calls(isColonistGetter))
                {
                    continue;
                }

                if (!HasSubhumanCheckWithin(codes, i, isColonySubhumanGetter))
                {
                    continue;
                }

                candidateIndices.Add(i);
            }

            if (candidateIndices.Count != 1)
            {
                Log.ErrorOnce(
                    $"{LogPrefix} MechanoidMechanitorScenarioColonistBarPatches：ColonistBar 远行队 IsColonist 检查预期仅 1 处，实际找到 {candidateIndices.Count} 处。",
                    ErrorKeyCaravanIsColonistMatchCount);
                return false;
            }

            CodeInstruction instruction = codes[candidateIndices[0]];
            instruction.opcode = OpCodes.Call;
            instruction.operand = helperMethod;
            return true;
        }

        private static bool HasSubhumanCheckWithin(
            List<CodeInstruction> codes,
            int isColonistIndex,
            MethodInfo isColonySubhumanGetter)
        {
            int searchLimit = isColonistIndex + 1 + MaxInstructionsAfterIsColonist;
            if (searchLimit > codes.Count)
            {
                searchLimit = codes.Count;
            }

            for (int j = isColonistIndex + 1; j < searchLimit; j++)
            {
                if (codes[j].Calls(isColonySubhumanGetter))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool InjectPortraitDisplayAppend(List<CodeInstruction> codes)
        {
            FieldInfo? cachedEntriesField = AccessTools.Field(
                typeof(ColonistBar),
                "cachedEntries");
            FieldInfo? reorderableGroupsField = AccessTools.Field(
                typeof(ColonistBar),
                "cachedReorderableGroups");
            MethodInfo? clearMethod = AccessTools.Method(
                typeof(List<int>),
                nameof(List<int>.Clear));
            MethodInfo? appendMethod = AccessTools.Method(
                typeof(MechanoidMechanitorScenarioColonistBarPortraitUtility),
                nameof(MechanoidMechanitorScenarioColonistBarPortraitUtility.AppendMapPortraitDisplayEntries),
                new[] { typeof(List<ColonistBar.Entry>) });

            if (cachedEntriesField == null
                || reorderableGroupsField == null
                || clearMethod == null
                || appendMethod == null)
            {
                Log.ErrorOnce(
                    $"{LogPrefix} MechanoidMechanitorScenarioColonistBarPatches：无法解析 ColonistBar 头像追加相关方法，补丁未应用。",
                    ErrorKeyPortraitAppendResolveFailed);
                return false;
            }

            List<int> candidateIndices = new List<int>();

            for (int i = 0; i < codes.Count; i++)
            {
                if (!codes[i].LoadsField(reorderableGroupsField))
                {
                    continue;
                }

                if (i + 1 >= codes.Count || !codes[i + 1].Calls(clearMethod))
                {
                    continue;
                }

                candidateIndices.Add(i);
            }

            if (candidateIndices.Count != 1)
            {
                Log.ErrorOnce(
                    $"{LogPrefix} MechanoidMechanitorScenarioColonistBarPatches：ColonistBar 头像追加入口预期仅 1 处，实际找到 {candidateIndices.Count} 处。",
                    ErrorKeyPortraitAppendMatchCount);
                return false;
            }

            InsertBeforePreservingLabels(
                codes,
                candidateIndices[0],
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, cachedEntriesField),
                new CodeInstruction(OpCodes.Call, appendMethod));
            return true;
        }

        private static void InsertBeforePreservingLabels(
            List<CodeInstruction> codes,
            int index,
            params CodeInstruction[] inserted)
        {
            if (inserted.Length == 0)
            {
                return;
            }

            CodeInstruction target = codes[index];
            CodeInstruction first = inserted[0];

            if (target.labels.Count > 0)
            {
                first.labels.AddRange(target.labels);
                target.labels.Clear();
            }

            TransferExceptionBlocksForInsertBefore(target, first);

            codes.InsertRange(index, inserted);
        }

        private static void TransferExceptionBlocksForInsertBefore(
            CodeInstruction target,
            CodeInstruction first)
        {
            if (target.blocks.Count == 0)
            {
                return;
            }

            List<ExceptionBlock> movedBlocks = new List<ExceptionBlock>();
            List<ExceptionBlock> retainedBlocks = new List<ExceptionBlock>();

            for (int i = 0; i < target.blocks.Count; i++)
            {
                ExceptionBlock block = target.blocks[i];
                if (ShouldMoveExceptionBlockBeforeInsertedInstructions(block))
                {
                    movedBlocks.Add(block);
                }
                else
                {
                    retainedBlocks.Add(block);
                }
            }

            target.blocks.Clear();
            target.blocks.AddRange(retainedBlocks);
            first.blocks.AddRange(movedBlocks);
        }

        /// <summary>
        /// Begin 类边界在指令执行前生效，插入前移到新序列入口；End 与未知类型留在原指令。
        /// </summary>
        private static bool ShouldMoveExceptionBlockBeforeInsertedInstructions(
            ExceptionBlock block)
        {
            switch (block.blockType)
            {
                case ExceptionBlockType.BeginExceptionBlock:
                case ExceptionBlockType.BeginCatchBlock:
                case ExceptionBlockType.BeginExceptFilterBlock:
                case ExceptionBlockType.BeginFaultBlock:
                case ExceptionBlockType.BeginFinallyBlock:
                    return true;
                case ExceptionBlockType.EndExceptionBlock:
                    return false;
                default:
                    return false;
            }
        }
    }
}
