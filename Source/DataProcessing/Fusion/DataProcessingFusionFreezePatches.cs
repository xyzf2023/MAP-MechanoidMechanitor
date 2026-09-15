using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 冻结项仍保存在原注册表。计划只计算其他目标，并预扣自身净成本；
    /// 会话中的冻结值仅用于验证，绝不反向覆盖玩家的整个分配注册表。
    /// </summary>
    internal static class DataProcessingFusionFreezeUtility
    {
        [ThreadStatic] internal static Pawn? BudgetOwner;
        private static readonly Type RegistryType = typeof(GameComponent_DataProcessingAllocationRegistry);
        internal static readonly MethodInfo CalculateFree =
            DataProcessingFusionReadPatches.Require(RegistryType, "TryCalculateAllocationFreeConsciousness");
        private static readonly MethodInfo ApplyPositive =
            DataProcessingFusionReadPatches.Require(RegistryType, "TryApplyCommandFocusHediffForTarget");
        internal static readonly FieldInfo PlanTarget =
            AccessTools.Field(AccessTools.Inner(RegistryType, "PlanEntry"), "target")
            ?? throw new MissingFieldException("数据处理 PlanEntry.target 未找到");

        internal static bool Matches(
            GameComponent_DataProcessingAllocationRegistry registry, MechFusionSession session)
        {
            Pawn source = session.SourcePawn!;
            MechFusionMechanitorSnapshot snapshot = session.MechanitorSnapshot!;
            return registry.GetStepsForOverseerTarget(source, source)
                       == snapshot.fusionSelfAllocationSteps
                && (snapshot.fusionSelfAllocationSteps == 0
                    || registry.GetSpecializationForOverseerTarget(source, source)
                       == snapshot.fusionSelfAllocationSpecialization);
        }

        internal static bool PrepareBudget(
            GameComponent_DataProcessingAllocationRegistry registry,
            Pawn owner, MechFusionSession session)
        {
            MechFusionMechanitorSnapshot snapshot = session.MechanitorSnapshot!;
            if (!Matches(registry, session))
            {
                DataProcessingOverseerResolver.ExitForSafety(session, "自身档位或特化已被外部改变");
                return false;
            }

            // 读档时负面先被撤销。被冻结的自身不进入计划，因此必须在此
            // 确保其正面返还存在，才能允许原计划最后恢复监管者负面。
            if (snapshot.fusionSelfAllocationSteps > 0)
            {
                bool restored = (bool)(ApplyPositive.Invoke(registry, new object[]
                {
                    owner, snapshot.fusionSelfAllocationSpecialization,
                    snapshot.fusionSelfAllocationSteps, true
                }) ?? false);
                if (!restored) return false;
            }

            object[] args = { owner, 0f };
            if (!(bool)(CalculateFree.Invoke(registry, args) ?? false)) return false;
            float available = (float)args[1] - snapshot.fusionSelfAllocationSteps
                * DataProcessingAllocationUtility.StepPercent * 0.5f;
            if (snapshot.fusionSelfAllocationSteps > 0
                && available + 0.0001f < DataProcessingAllocationUtility.MinReservedConsciousness)
            {
                DataProcessingOverseerResolver.EndBeforeSelfReclaim(owner, "冻结自身分配已超过安全容量");
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(GameComponent_DataProcessingAllocationRegistry), "ApplyDynamicPlan")]
    internal static class DataProcessingFusionFrozenPlanPatch
    {
        // 不改原方法签名；只从本轮临时 PlanEntry 列表排除自身，不删配置或实际记录。
        private static bool Prefix(
            GameComponent_DataProcessingAllocationRegistry __instance,
            Pawn overseer, object entries, ref bool __result, out Pawn? __state)
        {
            __state = DataProcessingFusionFreezeUtility.BudgetOwner;
            DataProcessingFusionFreezeUtility.BudgetOwner = null;
            if (!DataProcessingOverseerResolver.TryGetFrozenSelf(overseer, out MechFusionSession? session))
            {
                if (session?.IsActive == true && session.MechanitorSnapshot?.captured == true)
                {
                    DataProcessingOverseerResolver.ExitForSafety(session, "旧版会话缺少自身分配冻结快照");
                    __result = false;
                    return false;
                }
                return true;
            }

            if (!DataProcessingFusionFreezeUtility.PrepareBudget(__instance, overseer, session!))
            {
                __result = false;
                return false;
            }

            IList list = (IList)entries;
            for (int i = list.Count - 1; i >= 0; i--)
                if (ReferenceEquals(DataProcessingFusionFreezeUtility.PlanTarget.GetValue(list[i]), overseer))
                    list.RemoveAt(i);

            DataProcessingFusionFreezeUtility.BudgetOwner = overseer;
            return true;
        }

        private static void Finalizer(Pawn? __state)
        {
            DataProcessingFusionFreezeUtility.BudgetOwner = __state;
        }
    }

    [HarmonyPatch(typeof(GameComponent_DataProcessingAllocationRegistry), "TryCalculateAllocationFreeConsciousness")]
    internal static class DataProcessingFusionFrozenBudgetPatch
    {
        private static void Postfix(Pawn overseer, ref float baseConsciousness, bool __result)
        {
            if (__result
                && ReferenceEquals(DataProcessingFusionFreezeUtility.BudgetOwner, overseer)
                && DataProcessingOverseerResolver.TryGetFrozenSelf(overseer, out MechFusionSession? session))
            {
                baseConsciousness -= session!.MechanitorSnapshot!.fusionSelfAllocationSteps
                    * DataProcessingAllocationUtility.StepPercent * 0.5f;
            }
        }
    }

    /// <summary>所有手动写入入口都检查冻结，不把 UI 禁用当成唯一保护。</summary>
    [HarmonyPatch]
    internal static class DataProcessingFusionFrozenEditPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            Type type = typeof(GameComponent_DataProcessingAllocationRegistry);
            string[] names =
            {
                "TryAddStep", "TryRemoveStep", "TrySetSpecialization", "TrySetManualSpecialization",
                "SetDynamicAllocationEnabledForTarget", "SetDynamicTargetNormalSteps",
                "SetDynamicTargetDefaultSpecialization", "SetDynamicTargetPriority",
                "SetDynamicTargetCheckInterval", "SetDynamicTargetCommonMaxSteps",
                "SetDynamicTargetAdvancedMaxEnabled", "SetDynamicTargetMaxStepsForSpecialization",
                "SetDynamicTargetRule", "ApplyDynamicTargetSettingsSnapshot"
            };
            foreach (string name in names) yield return DataProcessingFusionReadPatches.Require(type, name);
            yield return DataProcessingFusionReadPatches.Require(
                typeof(DataProcessingDefaultTemplateAndCopyFixUtility), "ApplySnapshotOnce");
        }

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Pawn? overseer, Pawn? target, ref bool __result)
        {
            if (!DataProcessingOverseerResolver.IsFrozenSelf(overseer, target)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch]
    internal static class DataProcessingFusionFrozenActualEditPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            Type type = typeof(GameComponent_DataProcessingAllocationRegistry);
            yield return DataProcessingFusionReadPatches.Require(type, "SetSteps");
            yield return DataProcessingFusionReadPatches.Require(type, "SetActualStepsWithoutSync");
            yield return DataProcessingFusionReadPatches.Require(type, "SetStoredSpecializationWithoutImmediateSync");
        }

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Pawn? overseer, Pawn? target)
        {
            return !DataProcessingOverseerResolver.IsFrozenSelf(overseer, target);
        }
    }

    /// <summary>批量恢复默认设置仍处理其他目标，但从两类收集结果中排除自身。</summary>
    [HarmonyPatch]
    internal static class DataProcessingFusionResetSelectionPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            Type type = typeof(DataProcessingDefaultTemplateAndCopyFixUtility);
            yield return DataProcessingFusionReadPatches.Require(type, "CollectActiveMechanoids");
            yield return DataProcessingFusionReadPatches.Require(type, "CollectDormantMechanoidConfigs");
        }

        private static void Postfix(Pawn overseer, HashSet<Pawn> targets)
        {
            if (DataProcessingOverseerResolver.IsFrozenSelf(overseer, overseer)) targets.Remove(overseer);
        }
    }

    [HarmonyPatch(typeof(GameComponent_DataProcessingAllocationRegistry), "ApplyProtectionReductionPlan")]
    internal static class DataProcessingFusionSelfProtectionPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(
            Pawn overseer, Dictionary<DataProcessingAllocationRecord, int> plannedReduction)
        {
            foreach (KeyValuePair<DataProcessingAllocationRecord, int> pair in plannedReduction)
            {
                if (pair.Value > 0 && ReferenceEquals(pair.Key?.target, overseer))
                {
                    DataProcessingOverseerResolver.EndBeforeSelfReclaim(overseer, "意识保护需要回收自身分配");
                    break;
                }
            }
        }
    }

    [HarmonyPatch(typeof(GameComponent_DataProcessingAllocationRegistry), "ClearOverseerActualAllocations")]
    internal static class DataProcessingFusionOwnerClearPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(Pawn? overseer)
        {
            DataProcessingOverseerResolver.EndBeforeSelfReclaim(overseer, "外部算力回收或清空分配");
        }
    }

    [HarmonyPatch(typeof(GameComponent_DataProcessingAllocationRegistry), "ClearTarget")]
    internal static class DataProcessingFusionSelfClearPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(Pawn? target)
        {
            DataProcessingOverseerResolver.EndBeforeSelfReclaim(target, "清空自身分配");
        }
    }
}
