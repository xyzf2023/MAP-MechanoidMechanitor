using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 原读档校正集合会包含仅保留配置的死亡监管者，导致意识读取失败后持续重试。
    /// 休眠监管者不需要读档预算校正，复活后由生命周期队列重新激活。
    /// </summary>
    [HarmonyPatch(
        typeof(GameComponent_DataProcessingAllocationRegistry),
        "CollectPostLoadReconciliationOverseers")]
    internal static class DataProcessingDormantPostLoadFilterPatch
    {
        private static void Postfix(ref HashSet<Pawn> __result)
        {
            if (__result == null)
            {
                return;
            }

            __result.RemoveWhere(IsDormantOrPermanentInvalid);
        }

        private static bool IsDormantOrPermanentInvalid(Pawn pawn)
        {
            return pawn == null
                || pawn.Dead
                || pawn.Destroyed
                || pawn.Discarded
                || pawn.health?.isBeingKilled == true;
        }
    }

    /// <summary>
    /// LoadedGame 原逻辑可能先删除旧存档中的死亡实际记录。读档流程全部完成后，
    /// 再依据仍保留的动态配置擦除休眠对象上的运行效果，避免在 PostLoadInit 阶段触碰 Hediff。
    /// </summary>
    [HarmonyPatch(
        typeof(GameComponent_DataProcessingAllocationRegistry),
        nameof(GameComponent_DataProcessingAllocationRegistry.LoadedGame))]
    internal static class DataProcessingDormantEffectScrubPatch
    {
        private const int ReflectionFailureLogKey = 0x4D415053; // "MAPS"

        private static readonly FieldInfo? DynamicTargetRecordsField =
            AccessTools.Field(
                typeof(GameComponent_DataProcessingAllocationRegistry),
                "dynamicTargetRecords");

        private static readonly FieldInfo? DynamicAllocationRecordsField =
            AccessTools.Field(
                typeof(GameComponent_DataProcessingAllocationRegistry),
                "dynamicAllocationRecords");

        private static void Postfix(
            GameComponent_DataProcessingAllocationRegistry __instance)
        {
            if (__instance == null)
            {
                return;
            }

            List<DataProcessingDynamicAllocationRecord>? globalRecords =
                DynamicAllocationRecordsField?.GetValue(__instance)
                    as List<DataProcessingDynamicAllocationRecord>;
            List<DataProcessingDynamicTargetRecord>? targetRecords =
                DynamicTargetRecordsField?.GetValue(__instance)
                    as List<DataProcessingDynamicTargetRecord>;

            if (globalRecords == null || targetRecords == null)
            {
                Log.ErrorOnce(
                    "[MAP-机械族机械师] 无法访问休眠动态配置，已跳过读档残留效果清理。",
                    ReflectionFailureLogKey);
                return;
            }

            List<DataProcessingDynamicAllocationRecord> globalSnapshot =
                new List<DataProcessingDynamicAllocationRecord>(globalRecords);
            for (int i = 0; i < globalSnapshot.Count; i++)
            {
                Pawn? overseer = globalSnapshot[i]?.overseer;
                if (!IsDormant(overseer))
                {
                    continue;
                }

                DataProcessingPawnLifecycleCoordinator.SuspendOverseerRuntime(
                    __instance,
                    overseer);
            }

            List<DataProcessingDynamicTargetRecord> targetSnapshot =
                new List<DataProcessingDynamicTargetRecord>(targetRecords);
            for (int i = 0; i < targetSnapshot.Count; i++)
            {
                DataProcessingDynamicTargetRecord? config = targetSnapshot[i];
                Pawn? target = config?.target;
                if (target == null || target.Discarded)
                {
                    continue;
                }

                if (IsDormant(target))
                {
                    // 死亡安全 ClearTarget 只清实际记录与正面 Hediff，不删除特化和动态配置。
                    __instance.ClearTarget(target);
                    continue;
                }

                if (IsDormant(config?.overseer))
                {
                    // 目标仍存活但监管者休眠：实际记录已在前序清理中释放，
                    // 此处按当前 0 档同步正面，保留特化配置。
                    __instance.SyncHediffForTarget(target);
                }
            }
        }

        private static bool IsDormant(Pawn? pawn)
        {
            return pawn != null
                && !pawn.Discarded
                && (pawn.Dead
                    || pawn.Destroyed
                    || pawn.health?.isBeingKilled == true);
        }
    }
}
