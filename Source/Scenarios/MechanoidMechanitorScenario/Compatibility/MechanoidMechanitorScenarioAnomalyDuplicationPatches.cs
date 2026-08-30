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
        private const int AnchorInvalidKey = 879360101;

        [HarmonyPrepare]
        public static bool Prepare()
        {
            return ModsConfig.AnomalyActive;
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
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
                    AnchorInvalidKey);
                return codes;
            }

            // 锚点：ldsfld tmpDuplicateCandidates 之后紧跟 callvirt List<Pawn>.get_Count()，
            // 即最终 "tmpDuplicateCandidates.Count != 0" 判断读取列表大小的位置。
            // 插入点必须位于该 Count 判断之前，从而过滤后为空时原版自然跳过本次复制。
            List<int> anchorIndices = new List<int>();
            for (int i = 0; i < codes.Count - 1; i++)
            {
                if (codes[i].LoadsField(candidateField)
                    && codes[i + 1].Calls(countGetter))
                {
                    anchorIndices.Add(i);
                }
            }

            if (anchorIndices.Count != 1)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}候选列表 Count 判断锚点数量={anchorIndices.Count}（期望 1，无法唯一确认插入位置），补丁未应用，原版指令原样返回。",
                    AnchorInvalidKey);
                return codes;
            }

            int anchorIndex = anchorIndices[0];

            // 在锚点前插入：重新加载同一静态列表并就地过滤。
            // 随后原版 ldsfld + get_Count 读取的是已过滤后的列表大小。
            codes.Insert(anchorIndex, new CodeInstruction(OpCodes.Ldsfld, candidateField));
            codes.Insert(anchorIndex + 1, new CodeInstruction(OpCodes.Call, filterMethod));

            return codes;
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
