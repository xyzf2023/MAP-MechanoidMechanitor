using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 原注册表可能在 Pawn.Kill 完成前已经移除无效实际记录，导致常规暂停流程无法再通过
    /// “存在实际记录”判断发现残留 Hediff。死亡通知完成后按持久配置再擦除一次运行效果。
    /// </summary>
    [HarmonyPatch(
        typeof(DataProcessingPawnLifecycleCoordinator),
        nameof(DataProcessingPawnLifecycleCoordinator.NotifyPawnDied))]
    internal static class DataProcessingDeathRuntimeEffectScrubPatch
    {
        private const int ReflectionFailureLogKey = 0x4D415052; // "MAPR"

        private static readonly FieldInfo? DynamicTargetRecordsField =
            AccessTools.Field(
                typeof(GameComponent_DataProcessingAllocationRegistry),
                "dynamicTargetRecords");

        private static void Postfix(Pawn? pawn)
        {
            GameComponent_DataProcessingAllocationRegistry? registry =
                GameComponent_DataProcessingAllocationRegistry.CurrentRegistry;
            if (registry == null || pawn == null)
            {
                return;
            }

            List<DataProcessingDynamicTargetRecord>? records =
                DynamicTargetRecordsField?.GetValue(registry)
                    as List<DataProcessingDynamicTargetRecord>;
            if (records == null)
            {
                Log.ErrorOnce(
                    "[MAP-机械族机械师] 无法访问动态目标配置，死亡后的残留效果擦除已跳过。",
                    ReflectionFailureLogKey);
                return;
            }

            List<DataProcessingDynamicTargetRecord> snapshot =
                new List<DataProcessingDynamicTargetRecord>(records);
            for (int i = 0; i < snapshot.Count; i++)
            {
                DataProcessingDynamicTargetRecord? config = snapshot[i];
                Pawn? target = config?.target;
                if (target == null || target.Discarded)
                {
                    continue;
                }

                if (ReferenceEquals(target, pawn)
                    || ReferenceEquals(config?.overseer, pawn))
                {
                    // 生命周期安全 ClearTarget 会只释放实际记录与指令聚焦，
                    // 保留特化、动态配置和顶置记录。
                    registry.ClearTarget(target);
                }
            }
        }
    }
}
