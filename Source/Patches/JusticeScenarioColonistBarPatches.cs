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
    /// 在机械族机械师专属剧本中，扩展 ColonistBar 远行队分组的殖民者头像筛选，
    /// 并为开启「头像显示」的普通机械体整合地图头像条目。
    /// 不修改 Pawn.IsColonist 的全局语义。
    /// </summary>
    [HarmonyPatch(typeof(ColonistBar), "CheckRecacheEntries")]
    public static class JusticeScenario_ColonistBar_CheckRecacheEntries_Patch
    {
        private const string LogPrefix = "[MAP_MechanoidMechanitor]";

        private const int MaxInstructionsAfterIsColonist = 8;

        private const int ErrorKeyCaravanPatchResolveFailed = 879345102;
        private const int ErrorKeyCaravanIsColonistMatchCount = 879345103;
        private const int ErrorKeyPortraitAppendResolveFailed = 879345104;
        private const int ErrorKeyPortraitAppendInjectionFailed = 879345105;

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            PatchCaravanIsColonistCheck(codes);
            InjectPortraitDisplayAppend(codes);

            return codes;
        }

        private static void PatchCaravanIsColonistCheck(List<CodeInstruction> codes)
        {
            MethodInfo? isColonistGetter = AccessTools.PropertyGetter(
                typeof(Pawn),
                nameof(Pawn.IsColonist));
            MethodInfo? isColonySubhumanGetter = AccessTools.PropertyGetter(
                typeof(Pawn),
                nameof(Pawn.IsColonySubhumanPlayerControlled));
            MethodInfo? helperMethod = AccessTools.Method(
                typeof(JusticeScenarioFreeColonistUtility),
                nameof(JusticeScenarioFreeColonistUtility.CountsAsColonistForCaravanBar));

            if (isColonistGetter == null
                || isColonySubhumanGetter == null
                || helperMethod == null)
            {
                Log.ErrorOnce(
                    $"{LogPrefix} JusticeScenarioColonistBarPatches: " +
                    "could not resolve ColonistBar caravan filter methods. Patch not applied.",
                    ErrorKeyCaravanPatchResolveFailed);
                return;
            }

            int matchCount = 0;

            for (int i = 0; i < codes.Count; i++)
            {
                if (!codes[i].Calls(isColonistGetter))
                {
                    continue;
                }

                bool followedBySubhumanCheck = false;
                int searchLimit = i + MaxInstructionsAfterIsColonist;
                if (searchLimit > codes.Count)
                {
                    searchLimit = codes.Count;
                }

                for (int j = i + 1; j < searchLimit; j++)
                {
                    if (codes[j].Calls(isColonySubhumanGetter))
                    {
                        followedBySubhumanCheck = true;
                        break;
                    }
                }

                if (!followedBySubhumanCheck)
                {
                    continue;
                }

                // 原地修改 opcode/operand，保留原 callvirt 指令上的 labels 与 exception blocks。
                CodeInstruction instruction = codes[i];
                instruction.opcode = OpCodes.Call;
                instruction.operand = helperMethod;
                matchCount++;
            }

            if (matchCount != 1)
            {
                Log.ErrorOnce(
                    $"{LogPrefix} JusticeScenarioColonistBarPatches: " +
                    $"expected exactly one ColonistBar caravan IsColonist check, found {matchCount}.",
                    ErrorKeyCaravanIsColonistMatchCount);
            }
        }

        private static void InjectPortraitDisplayAppend(List<CodeInstruction> codes)
        {
            FieldInfo? reorderableGroupsField = AccessTools.Field(
                typeof(ColonistBar),
                "cachedReorderableGroups");
            MethodInfo? clearMethod = AccessTools.Method(
                typeof(List<int>),
                nameof(List<int>.Clear));
            MethodInfo? appendMethod = AccessTools.Method(
                typeof(JusticeScenarioColonistBarPortraitUtility),
                nameof(JusticeScenarioColonistBarPortraitUtility.AppendMapPortraitDisplayEntries));

            if (reorderableGroupsField == null
                || clearMethod == null
                || appendMethod == null)
            {
                Log.ErrorOnce(
                    $"{LogPrefix} JusticeScenarioColonistBarPatches: " +
                    "could not resolve ColonistBar portrait append methods. Patch not applied.",
                    ErrorKeyPortraitAppendResolveFailed);
                return;
            }

            bool injected = false;

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

                // labels 与 Begin 类 exception blocks 移到插入序列入口；EndExceptionBlock 留在 ldfld。
                InsertBeforePreservingLabels(
                    codes,
                    i,
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Call, appendMethod));
                injected = true;
                break;
            }

            if (!injected)
            {
                Log.ErrorOnce(
                    $"{LogPrefix} JusticeScenarioColonistBarPatches: " +
                    "could not inject ColonistBar portrait append call.",
                    ErrorKeyPortraitAppendInjectionFailed);
            }
        }

        private static void InsertBeforePreservingLabels(
            List<CodeInstruction> codes,
            int index,
            CodeInstruction first,
            CodeInstruction second)
        {
            CodeInstruction target = codes[index];

            if (target.labels.Count > 0)
            {
                first.labels.AddRange(target.labels);
                target.labels.Clear();
            }

            TransferExceptionBlocksForInsertBefore(target, first);

            codes.InsertRange(index, new[] { first, second });
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
