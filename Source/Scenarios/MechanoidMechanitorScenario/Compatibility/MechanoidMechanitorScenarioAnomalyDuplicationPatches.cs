using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using MAP_MechanoidMechanitor.Scenarios;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 第一层防护：在 CompObelisk_Duplicator.CompTick 完成候选列表构建之后、执行最终
    /// Count 判断与 RandomElement 选择之前，就地移除机械族机械师候选。
    /// 过滤后若列表为空，原版会自然跳过本次复制（不调用 RandomElement / TryDuplicatePawn），
    /// 因此不会生成任何复制体，也不会向机械族机械师注册表写入幽灵记录。
    /// </summary>
    [HarmonyPatch(typeof(CompObelisk_Duplicator), "CompTick")]
    public static class MechanoidMechanitorScenario_CompObelisk_Duplicator_CompTick_Patch
    {
        private const string LogPrefix =
            "[MAP-机械族机械师] Anomaly 腐化复制方尖碑候选过滤：";
        private const int ReflectionFailKey = 879360101;
        private const int RandomElementFailKey = 879360102;
        private const int FinalCountFailKey = 879360103;
        private const int ExceptionBlockFailKey = 879360104;

        [HarmonyPrepare]
        public static bool Prepare()
        {
            return ModsConfig.AnomalyActive;
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            // 第一时间完整物化，禁止边 yield 边修改。
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            FieldInfo? candidateField = AccessTools.Field(
                typeof(CompObelisk_Duplicator),
                "tmpDuplicateCandidates");
            MethodInfo? countGetter = AccessTools.PropertyGetter(
                typeof(List<Pawn>),
                nameof(List<Pawn>.Count));
            MethodInfo? filterMethod = AccessTools.Method(
                typeof(MechanoidMechanitorScenarioAnomalyDuplicationUtility),
                nameof(MechanoidMechanitorScenarioAnomalyDuplicationUtility.FilterCorruptedObeliskCandidates));

            if (candidateField == null || countGetter == null || filterMethod == null)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}无法解析反射目标（tmpDuplicateCandidates / List.Count / Filter），补丁未应用。",
                    ReflectionFailKey);
                return codes;
            }

            // 第一步：唯一识别本方法用于选择复制目标的 RandomElement 调用。
            // 原版 CompTick 中 tmpDuplicateCandidates.Count 有多处读取，但只有最终复制逻辑块
            // 才会通过 tmpDuplicateCandidates.RandomElement() 选择目标 Pawn。
            int randomElementIndex = FindDuplicateTargetRandomElement(codes, candidateField);
            if (randomElementIndex < 0)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}无法唯一确认腐化复制方尖碑 RandomElement 目标选择位置（结果 {randomElementIndex}），候选过滤补丁未应用，原版指令原样返回。",
                    RandomElementFailKey);
                return codes;
            }

            // 第二步：从唯一 RandomElement 向前定位控制它的最终 Count 判断。
            // 两个回退分支（Count == 0 时改取 WorldPawns）的分支目标位于 RandomElement 之前，
            // 只有最终 "Count != 0" 判断的分支目标位于 RandomElement 之后（跳过整个复制块）。
            // 因此用"分支目标是否位于 RandomElement 之后"区分最终 Count 与回退 Count。
            List<int> finalCountAnchors = new List<int>();
            for (int i = 0; i < codes.Count - 1; i++)
            {
                if (!codes[i].LoadsField(candidateField)
                    || !codes[i + 1].Calls(countGetter))
                {
                    continue;
                }

                int branchIndex = FindConditionalBranchAfter(codes, i + 2);
                if (branchIndex < 0)
                {
                    continue;
                }

                if (codes[branchIndex].operand is not Label branchLabel)
                {
                    continue;
                }

                int targetIndex = IndexOfLabel(codes, branchLabel);
                if (targetIndex < 0)
                {
                    continue;
                }

                if (targetIndex > randomElementIndex)
                {
                    finalCountAnchors.Add(i);
                }
            }

            if (finalCountAnchors.Count != 1)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}通过 RandomElement 控制流无法唯一确认最终 Count 判断锚点（匹配 {finalCountAnchors.Count} 处），补丁未应用，原版指令原样返回。",
                    FinalCountFailKey);
                return codes;
            }

            int anchorIndex = finalCountAnchors[0];
            CodeInstruction anchor = codes[anchorIndex];

            // 第七步：异常块安全检查。若该指令是异常处理边界入口，禁止猜测移动。
            if (anchor.blocks.Count > 0)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}最终 Count 锚点携带无法安全处理的 ExceptionBlock，补丁未应用，原版指令原样返回。",
                    ExceptionBlockFailKey);
                return codes;
            }

            // 第七步：转移控制流入口 Label。
            // 该 ldsfld 可能同时是某条前序分支（if/else 汇合）的跳转目标；
            // 若不把 Label 转移到新插入的过滤首条指令，该分支会绕过过滤直接落到原 Count 判断。
            CodeInstruction filterLoad = new CodeInstruction(OpCodes.Ldsfld, candidateField);
            if (anchor.labels.Count > 0)
            {
                filterLoad.labels.AddRange(anchor.labels);
                anchor.labels.Clear();
            }

            CodeInstruction filterCall = new CodeInstruction(OpCodes.Call, filterMethod);

            // 插入后形成：
            //   filterLoad  -> ldsfld tmpDuplicateCandidates
            //   filterCall  -> FilterCorruptedObeliskCandidates(...)
            //   anchor      -> ldsfld tmpDuplicateCandidates
            //   get_Count / 条件分支 / RandomElement ...
            // 过滤发生在 Count 之前；过滤后为空时原版 Count 判断失败，不再执行 RandomElement。
            codes.Insert(anchorIndex, filterLoad);
            codes.Insert(anchorIndex + 1, filterCall);

            return codes;
        }

        /// <summary>
        /// 查找本方法中用于选择复制目标的 RandomElement 调用。
        /// 要求：方法名为 RandomElement、返回 Pawn、且所消费的列表来自 tmpDuplicateCandidates。
        /// 找不到返回 -1；找到多个返回 -2；唯一找到返回其指令索引。
        /// </summary>
        private static int FindDuplicateTargetRandomElement(
            List<CodeInstruction> codes,
            FieldInfo candidateField)
        {
            int foundIndex = -1;
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode != OpCodes.Call && codes[i].opcode != OpCodes.Callvirt)
                {
                    continue;
                }

                if (codes[i].operand is not MethodInfo method)
                {
                    continue;
                }

                if (method.Name != "RandomElement")
                {
                    continue;
                }

                if (method.ReturnType != typeof(Pawn))
                {
                    continue;
                }

                // 该调用消费的列表必须直接来自 tmpDuplicateCandidates 这个静态字段。
                if (i <= 0 || !codes[i - 1].LoadsField(candidateField))
                {
                    continue;
                }

                if (foundIndex != -1)
                {
                    return -2;
                }

                foundIndex = i;
            }

            return foundIndex;
        }

        /// <summary>
        /// 从 start 起在有限窗口内查找第一条条件分支指令（if/else 控制流入口）。
        /// </summary>
        private static int FindConditionalBranchAfter(List<CodeInstruction> codes, int start)
        {
            int limit = Math.Min(start + 8, codes.Count);
            for (int i = start; i < limit; i++)
            {
                if (codes[i].opcode.FlowControl == FlowControl.Cond_Branch)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// 查找携带指定 Label 的指令索引（用于解析分支跳转目标）。
        /// </summary>
        private static int IndexOfLabel(List<CodeInstruction> codes, Label label)
        {
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].labels.Contains(label))
                {
                    return i;
                }
            }

            return -1;
        }
    }

    /// <summary>
    /// 第二层防护（权威安全门）：在 AnomalyUtility.TryDuplicatePawn 创建复制体之前拦截
    /// CompObelisk_Duplicator.CompTick 发起的敌对自动复制调用。
    /// 仅当剧本启用、目标是机械族机械师、且调用特征精确匹配原版腐化方尖碑敌对复制
    /// （Faction.OfEntities、allowCreepjoiners == false、randomOutcome == false、
    /// negativeOutcomes == false）时才阻止，避免误伤其他复制机制或玩家主动互动。
    /// </summary>
    [HarmonyPatch(typeof(AnomalyUtility), "TryDuplicatePawn")]
    public static class MechanoidMechanitorScenario_AnomalyUtility_TryDuplicatePawn_Patch
    {
        [HarmonyPrepare]
        public static bool Prepare()
        {
            return ModsConfig.AnomalyActive;
        }

        [HarmonyPrefix]
        public static bool Prefix(
            Pawn? originalPawn,
            ref Pawn? duplicatePawn,
            Faction? faction,
            bool allowCreepjoiners,
            bool randomOutcome,
            bool negativeOutcomes,
            ref bool __result)
        {
            if (!GameComponent_MechanoidMechanitorScenarioState.IsEnabled)
            {
                return true;
            }

            if (originalPawn == null)
            {
                return true;
            }

            if (!MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(originalPawn))
            {
                return true;
            }

            if (faction != Faction.OfEntities)
            {
                return true;
            }

            if (allowCreepjoiners || randomOutcome || negativeOutcomes)
            {
                return true;
            }

            duplicatePawn = null;
            __result = false;
            return false;
        }
    }
}
