using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    // 只读显示快照，不序列化；读取窗口不会创建配置或推动调度器。
    public sealed class DataProcessingTargetUISnapshot
    {
        public DataProcessingDynamicTargetRecord? config;
        public bool dynamicManaged;
        public bool evaluationPending;
        public bool taskActive;
        public DataProcessingDynamicState state;
        public DataProcessingSpecialization specialization;
        public DataProcessingSpecialization requestedSpecialization;
        public int actualSteps;
        public int normalSteps;
        public int requestedSteps;
        public float netCost;
        public bool IsLimited => !evaluationPending && actualSteps < requestedSteps;
    }

    public sealed class DataProcessingBudgetUISnapshot
    {
        public bool available;
        public float current;
        public float baseProcessing;
        public float threshold;
        public float fixedCost;
        public float dynamicCost;
        public float selfReturn;
        public int unmetSteps;
        public int limitedCount;
        public int pendingCount;
    }

    public sealed partial class GameComponent_DataProcessingAllocationRegistry
    {
        /// <summary>首次打开面板即建立监管者模板；保留已有配置，不启动自动分配。</summary>
        public void EnsureDashboardConfiguration(Pawn overseer)
        {
            if (overseer == null || !IsDynamicAllocationOverseerValid(overseer)
                || !ResearchFeatureUnlockUtility.IsDataProcessingAllocationUnlocked()
                || FindDynamicAllocationRecord(overseer) != null) return;
            // 必须先保留已有固定额度，再建立默认模板，避免首次开启时覆盖额度。
            EnsureQuotaMappingBeforeGlobalToggle(overseer);
            dynamicAllocationRecords.Add(new DataProcessingDynamicAllocationRecord(overseer, false));
            RebuildDynamicAllocationCaches();
        }

        /// <summary>
        /// 总开关切换前将尚无策略的目标从固定额度初始化。
        /// 已保存的常态额度不从运行时实际值反推，避免预算限额或任务增量覆盖玩家设置。
        /// </summary>
        public void EnsureQuotaMappingBeforeGlobalToggle(Pawn overseer)
        {
            var targets = new List<Pawn>();
            CollectDynamicAllocationTargets(overseer, targets);
            foreach (var target in targets)
            {
                if (target == null || target.Dead || target.Destroyed || target.Discarded
                    || !IsValidAllocationPairForList(overseer, target)
                    || FindDynamicTargetRecord(overseer, target) != null) continue;
                int fixedSteps = GetStepsForOverseerTarget(overseer, target);
                var config = GetOrCreateDynamicTargetRecord(overseer, target);
                config.normalSteps = fixedSteps;
                config.Normalize();
            }
        }

        private static int GetRequestedStepsForEvaluation(
            DataProcessingDynamicTargetRecord config, DynamicEvaluation evaluation)
        {
            return evaluation.taskActive
                ? config.GetMaxStepsForSpecialization(evaluation.specialization)
                : config.normalSteps;
        }

        public DataProcessingTargetUISnapshot GetTargetSnapshotForUI(Pawn overseer, Pawn target)
        {
            var config = FindDynamicTargetRecord(overseer, target);
            bool global = IsDynamicAllocationEnabled(overseer);
            int actual = GetStepsForOverseerTarget(overseer, target);
            var result = new DataProcessingTargetUISnapshot
            {
                config = config,
                actualSteps = actual,
                normalSteps = config?.normalSteps ?? actual,
                requestedSteps = global ? config?.normalSteps ?? actual : actual,
                specialization = GetSpecializationForOverseerTarget(overseer, target),
                dynamicManaged = global && (config?.enabled ?? true),
                netCost = GetBudgetCostUnits(overseer, target, actual) * 0.025f
            };
            result.requestedSpecialization = result.specialization;
            result.evaluationPending = pendingPostLoadDynamicReconciliation
                || pendingGlobalTransitionOverseers.Contains(overseer);
            if (result.dynamicManaged)
            {
                if (config != null && cachedDynamicEvaluationByTarget.TryGetValue(target, out var evaluation)
                    && ReferenceEquals(evaluation.overseer, overseer))
                {
                    result.state = evaluation.state;
                    result.taskActive = evaluation.taskActive;
                    result.requestedSpecialization = evaluation.specialization;
                    // 配置已经在写入/读档时规范化。此处使用副本，避免 GetMaxSteps 的 Normalize 写入。
                    var copy = new DataProcessingDynamicTargetRecord();
                    DataProcessingDynamicTargetSettingsSnapshot.Capture(config)
                        .ApplyTo(copy, DataProcessingTargetCopyMode.AllSettings);
                    result.requestedSteps = GetRequestedStepsForEvaluation(copy, evaluation);
                }
                else result.evaluationPending = true;
            }
            return result;
        }

        public DataProcessingBudgetUISnapshot GetBudgetSnapshotForUI(Pawn overseer)
        {
            var result = new DataProcessingBudgetUISnapshot();
            result.available = !pendingPostLoadDynamicReconciliation
                && !pendingGlobalTransitionOverseers.Contains(overseer)
                && DataProcessingAllocationUtility.TryGetCurrentConsciousness(overseer, out result.current)
                && TryCalculateAllocationFreeConsciousness(overseer, out result.baseProcessing);
            result.threshold = (FindDynamicAllocationRecord(overseer)?.minConsciousnessPercent ?? 100) / 100f;
            var targets = new List<Pawn>();
            CollectDynamicAllocationTargets(overseer, targets);
            var seen = new HashSet<Pawn>();
            foreach (var target in targets)
            {
                if (target == null || !seen.Add(target)) continue;
                var snapshot = GetTargetSnapshotForUI(overseer, target);
                if (snapshot.dynamicManaged) result.dynamicCost += snapshot.netCost;
                else result.fixedCost += snapshot.netCost;
                if (snapshot.evaluationPending) result.pendingCount++;
                if (snapshot.IsLimited)
                {
                    result.unmetSteps += snapshot.requestedSteps - snapshot.actualSteps;
                    result.limitedCount++;
                }
                if (ReferenceEquals(target, overseer))
                    result.selfReturn = snapshot.actualSteps * DataProcessingAllocationUtility.StepPercent * 0.5f;
            }
            return result;
        }

        /// <summary>批量应用已有快照规则；复核归属和生命周期，整批仅运行一次预算。</summary>
        public int ApplyTargetSettingsBatch(Pawn overseer, IEnumerable<Pawn> targets,
            DataProcessingDynamicTargetSettingsSnapshot snapshot, DataProcessingTargetCopyMode mode,
            DataProcessingSpecialization? selectedSpecialization = null, Dictionary<DataProcessingBatchField, int>? selectedFields = null)
        {
            if (overseer == null || snapshot == null || targets == null
                || !IsDynamicAllocationOverseerValid(overseer)
                || pendingPostLoadDynamicReconciliation
                || pendingGlobalTransitionOverseers.Contains(overseer)) return 0;
            if (selectedFields != null && selectedFields.Count == 0) return 0;
            var seen = new HashSet<Pawn>();
            bool dynamic = IsDynamicAllocationEnabled(overseer);
            int count = 0;
            foreach (var target in targets)
            {
                if (target == null || !seen.Add(target) || target.Dead || target.Destroyed || target.Discarded
                    || target.RaceProps?.IsMechanoid != true || !IsValidAllocationPairForList(overseer, target)) continue;
                var config = GetOrCreateDynamicTargetRecord(overseer, target);
                if (selectedSpecialization.HasValue && !TrySetManualSpecialization(overseer, target, selectedSpecialization.Value)) continue;
                if (selectedFields != null)
                {
                    foreach (var field in selectedFields)
                        DataProcessingBatchFieldUtility.Write(config, field.Key, field.Value);
                    config.Normalize();
                }
                else snapshot.ApplyTo(config, mode);
                if (selectedSpecialization.HasValue) config.defaultSpecialization = selectedSpecialization.Value;
                cachedDynamicEvaluationByTarget.Remove(target);
                nextDynamicCheckTickByTarget.Remove(target);
                if (dynamic && config.enabled)
                {
                    var evaluation = EvaluateTarget(config, target);
                    if (selectedSpecialization.HasValue) evaluation.specialization = config.defaultSpecialization;
                    cachedDynamicEvaluationByTarget[target] = evaluation;
                    ScheduleDynamicTargetCheck(target, config.checkIntervalTicks, immediate: false);
                }
                else if (!dynamic && ((selectedFields == null && selectedSpecialization.HasValue) || (selectedFields != null && selectedFields.ContainsKey(DataProcessingBatchField.Normal))))
                {
                    // 批量编辑固定额度同样遵守现有加档安全检查。
                    int desired = config.normalSteps;
                    int current = GetStepsForOverseerTarget(overseer, target);
                    if (desired <= current) SetSteps(overseer, target, desired);
                    else
                    {
                        long capacity = (long)DataProcessingAllocationUtility.GetAdditionalAssignableSteps(overseer)
                            * (ReferenceEquals(overseer, target) ? 2 : 1);
                        int attempts = (int)System.Math.Min(desired - current, System.Math.Max(0L, capacity));
                        for (int i = 0; i < attempts; i++)
                            if (!TryAddStep(overseer, target)) break;
                    }
                    config.normalSteps = desired;
                    config.Normalize();
                }
                count++;
            }
            if (count > 0)
            {
                RebuildDynamicTargetCaches();
                if (dynamic) RunDynamicPlanForOverseer(overseer);
            }
            return count;
        }

        /// <summary>手动特化立即生效，并保持到下一次自动规则检查。</summary>
        public bool SetDashboardSpecialization(Pawn overseer, Pawn target, DataProcessingSpecialization specialization)
        {
            if (!IsValidAllocationPairForList(overseer, target)) return false;
            var config = GetOrCreateDynamicTargetRecord(overseer, target);
            if (!TrySetManualSpecialization(overseer, target, specialization)) return false;
            if (IsDynamicAllocationEnabled(overseer) && config.enabled)
            {
                var evaluation = EvaluateTarget(config, target);
                evaluation.specialization = config.defaultSpecialization;
                cachedDynamicEvaluationByTarget[target] = evaluation;
                ScheduleDynamicTargetCheck(target, config.checkIntervalTicks, immediate: false);
                RunDynamicPlanForOverseer(overseer);
            }
            return true;
        }
    }
}
