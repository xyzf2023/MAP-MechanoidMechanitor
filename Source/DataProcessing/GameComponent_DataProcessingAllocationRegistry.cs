using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    public sealed class GameComponent_DataProcessingAllocationRegistry : GameComponent
    {
        private List<DataProcessingAllocationRecord> records = new List<DataProcessingAllocationRecord>();
        private Dictionary<Pawn, List<DataProcessingAllocationRecord>> recordsByOverseer =
            new Dictionary<Pawn, List<DataProcessingAllocationRecord>>();
        private Dictionary<Pawn, DataProcessingAllocationRecord> recordByTarget =
            new Dictionary<Pawn, DataProcessingAllocationRecord>();

        // 顶置状态独立于分配记录，分配降到 0% 时仍需保留。
        private List<DataProcessingAllocationPinRecord> pinRecords =
            new List<DataProcessingAllocationPinRecord>();
        private int nextPinOrder;

        // 特化配置记录独立于分配档数；分配归零时仍需保留已选特化。
        private List<DataProcessingSpecializationRecord> specializationRecords =
            new List<DataProcessingSpecializationRecord>();
        private Dictionary<Pawn, DataProcessingSpecializationRecord> specializationRecordByTarget =
            new Dictionary<Pawn, DataProcessingSpecializationRecord>();

        // 动态分配开关独立于分配档数与特化；开启后按机械体类型自动校正特化（不改档数）。
        private List<DataProcessingDynamicAllocationRecord> dynamicAllocationRecords =
            new List<DataProcessingDynamicAllocationRecord>();
        private Dictionary<Pawn, DataProcessingDynamicAllocationRecord> dynamicAllocationRecordByOverseer =
            new Dictionary<Pawn, DataProcessingDynamicAllocationRecord>();

        // 单体动态配置：常态额度、默认模式、最高额度、优先级、检查间隔与规则开关。
        private List<DataProcessingDynamicTargetRecord> dynamicTargetRecords =
            new List<DataProcessingDynamicTargetRecord>();
        private Dictionary<Pawn, DataProcessingDynamicTargetRecord> dynamicTargetRecordByTarget =
            new Dictionary<Pawn, DataProcessingDynamicTargetRecord>();

        private const int DynamicAllocationLogKeyBase = 0x4D415044; // "MAPD"

        private int protectionTickCounter;

        // 轻量调度：每 60 tick 检查到期单体目标，独立检查间隔默认 600 tick（最低 60）。
        private int dynamicSchedulerTickCounter;

        // 运行时下一次检查 tick（不写存档）。首次启用立即执行，之后按各自间隔错峰。
        private Dictionary<Pawn, int> nextDynamicCheckTickByTarget =
            new Dictionary<Pawn, int>();

        // 运行时评估结果缓存（不写存档）：key 为目标，避免重复评估与 UI/对话框复用开销。
        private Dictionary<Pawn, DynamicEvaluation> cachedDynamicEvaluationByTarget =
            new Dictionary<Pawn, DynamicEvaluation>();

        // 读档后统一动态校正标记（不写存档）。
        private bool pendingPostLoadDynamicReconciliation;
        private int postLoadReconciliationEarliestTick;

        // 全局开关切换（开启/关闭）失败后的重试队列（不写存档），按引用比较避免误判。
        private HashSet<Pawn> pendingGlobalTransitionOverseers =
            new HashSet<Pawn>(new ReferencePawnEqualityComparer());
        private int globalTransitionRetryEarliestTick;

        public static GameComponent_DataProcessingAllocationRegistry? CurrentRegistry
        {
            get
            {
                if (Current.Game == null)
                {
                    return null;
                }

                return Current.Game.GetComponent<GameComponent_DataProcessingAllocationRegistry>();
            }
        }

        public GameComponent_DataProcessingAllocationRegistry(Game game)
        {
        }

        public int GetStepsForTarget(Pawn? target)
        {
            if (target == null
                || !ResearchFeatureUnlockUtility.IsDataProcessingAllocationUnlocked())
            {
                return 0;
            }

            return recordByTarget.TryGetValue(target, out DataProcessingAllocationRecord? record)
                ? Mathf.Max(0, record.steps)
                : 0;
        }

        public int GetStepsForOverseerTarget(Pawn? overseer, Pawn? target)
        {
            if (overseer == null
                || target == null
                || !ResearchFeatureUnlockUtility.IsDataProcessingAllocationUnlocked())
            {
                return 0;
            }

            DataProcessingAllocationRecord? record = FindRecord(overseer, target);
            return record != null ? Mathf.Max(0, record.steps) : 0;
        }

        public int GetTotalStepsForOverseer(Pawn? overseer)
        {
            if (overseer == null
                || !ResearchFeatureUnlockUtility.IsDataProcessingAllocationUnlocked()
                || !recordsByOverseer.TryGetValue(overseer, out List<DataProcessingAllocationRecord>? overseerRecords))
            {
                return 0;
            }

            int total = 0;
            for (int i = 0; i < overseerRecords.Count; i++)
            {
                DataProcessingAllocationRecord record = overseerRecords[i];
                if (record != null)
                {
                    total += Mathf.Max(0, record.steps);
                }
            }

            return total;
        }

        public bool HasAtLeast(Pawn? target, int requiredSteps)
        {
            return requiredSteps <= 0 || GetStepsForTarget(target) >= requiredSteps;
        }

        /// <summary>
        /// 仅当注册表记录中 overseer 与 target 为同一 Pawn 时视为自我分配。
        /// </summary>
        public bool IsSelfAllocationTarget(Pawn? target)
        {
            if (target == null
                || !recordByTarget.TryGetValue(
                    target,
                    out DataProcessingAllocationRecord? record))
            {
                return false;
            }

            return ReferenceEquals(record.overseer, target)
                && ReferenceEquals(record.target, target);
        }

        public DataProcessingSpecialization GetSpecializationForOverseerTarget(
            Pawn? overseer,
            Pawn? target)
        {
            if (overseer == null || target == null)
            {
                return DataProcessingSpecialization.GeneralTuning;
            }

            DataProcessingSpecializationRecord? record = FindSpecializationRecord(overseer, target);
            return record != null
                ? DataProcessingAllocationUtility.NormalizeSpecialization(record.specialization)
                : DataProcessingSpecialization.GeneralTuning;
        }

        public DataProcessingSpecialization GetSpecializationForTarget(Pawn? target)
        {
            if (target == null)
            {
                return DataProcessingSpecialization.GeneralTuning;
            }

            DataProcessingSpecializationRecord? record = null;
            if (specializationRecordByTarget.TryGetValue(target, out DataProcessingSpecializationRecord? cached)
                && cached != null)
            {
                record = cached;
            }

            // 优先从当前正数分配记录取得 overseer，校验特化配置是否仍对应同一监管者。
            if (recordByTarget.TryGetValue(target, out DataProcessingAllocationRecord? allocRecord)
                && allocRecord != null
                && allocRecord.overseer != null
                && (record == null || !ReferenceEquals(record.overseer, allocRecord.overseer)))
            {
                record = FindSpecializationRecord(allocRecord.overseer, target);
            }

            return record != null
                ? DataProcessingAllocationUtility.NormalizeSpecialization(record.specialization)
                : DataProcessingSpecialization.GeneralTuning;
        }

        public bool TrySetSpecialization(
            Pawn? overseer,
            Pawn? target,
            DataProcessingSpecialization specialization)
        {
            specialization = DataProcessingAllocationUtility.NormalizeSpecialization(specialization);

            if (!DataProcessingAllocationUtility.IsValidAllocationPair(overseer, target))
            {
                return false;
            }

            int steps = GetStepsForTarget(target);

            // 事务式：先安全建立新 Hediff，成功后才更新记录；失败则保留旧 Hediff 与旧记录。
            if (steps > 0
                && !TryApplyCommandFocusHediffForTarget(
                    target!,
                    specialization,
                    steps,
                    cleanupOtherCommandFocusHediffs: true))
            {
                return false;
            }

            // 分配为 0：仅确保记录正确（无需 Hediff）。
            DataProcessingSpecializationRecord? existing = FindSpecializationRecord(overseer, target);
            if (existing != null)
            {
                existing.specialization = specialization;
            }
            else
            {
                specializationRecords.Add(
                    new DataProcessingSpecializationRecord(overseer!, target!, specialization));
            }

            RebuildSpecializationCaches();
            return true;
        }

        /// <summary>
        /// 主窗口手动设置模式：先应用特化，再把该模式写回单体配置的默认模式，
        /// 使玩家手动选择成为后续动态回落的默认值。动态计划内部仍使用
        /// <see cref="TrySetSpecialization"/>，避免临时动态模式覆盖玩家默认模式。
        /// </summary>
        public bool TrySetManualSpecialization(
            Pawn? overseer,
            Pawn? target,
            DataProcessingSpecialization specialization)
        {
            if (!TrySetSpecialization(
                    overseer,
                    target,
                    specialization))
            {
                return false;
            }

            if (overseer == null || target == null)
            {
                return true;
            }

            DataProcessingDynamicTargetRecord? config =
                FindDynamicTargetRecord(
                    overseer,
                    target);

            if (config != null)
            {
                config.defaultSpecialization =
                    DataProcessingAllocationUtility
                        .NormalizeSpecialization(
                            specialization);

                config.Normalize();
                RebuildDynamicTargetCaches();
            }

            return true;
        }

        public bool TryAddStep(Pawn? overseer, Pawn? target)
        {
            if (!ResearchFeatureUnlockUtility.IsDataProcessingAllocationUnlocked()
                || !DataProcessingAllocationUtility.IsValidAllocationPair(
                    overseer,
                    target))
            {
                return false;
            }

            bool dynamicManaged =
                IsDynamicAllocationEnabledForTarget(
                    overseer,
                    target);

            if (dynamicManaged)
            {
                DataProcessingDynamicTargetRecord config =
                    GetOrCreateDynamicTargetRecord(
                        overseer!,
                        target!);

                // 动态开启时，加号只修改常态额度。
                // 绝对不能先修改实际 DataProcessingAllocationRecord。
                config.normalSteps++;
                config.Normalize();

                TriggerDynamicBudgetRecompute(overseer!);
                return true;
            }

            // 非动态目标仍使用原来的绝对55%安全限制。
            if (!DataProcessingAllocationUtility.CanAddStep(overseer))
            {
                return false;
            }

            Pawn? replacedOverseer = null;
            DataProcessingAllocationRecord? record =
                FindRecord(overseer!, target!);

            if (record == null)
            {
                record = new DataProcessingAllocationRecord(
                    overseer!,
                    target!,
                    1);

                replacedOverseer = AddRecord(record);
            }
            else
            {
                record.steps++;
            }

            // 加档顺序：先目标正面，再监管者负面。
            SyncHediffForTarget(target!);
            SyncHediffsForOverseer(overseer!);
            SyncReplacedOverseerIfNeeded(
                replacedOverseer,
                overseer!);

            SyncNormalStepsFromManualActual(
                overseer!,
                target!);

            return true;
        }

        public bool TryRemoveStep(
            Pawn? overseer,
            Pawn? target)
        {
            if (overseer == null
                || target == null
                || !DataProcessingAllocationUtility
                    .IsValidAllocationPair(
                        overseer,
                        target))
            {
                return false;
            }

            bool dynamicManaged =
                IsDynamicAllocationEnabledForTarget(
                    overseer,
                    target);

            if (dynamicManaged)
            {
                DataProcessingDynamicTargetRecord config =
                    GetOrCreateDynamicTargetRecord(
                        overseer,
                        target);

                if (config.normalSteps <= 0)
                {
                    return false;
                }

                config.normalSteps--;
                config.Normalize();

                TriggerDynamicBudgetRecompute(overseer);
                return true;
            }

            DataProcessingAllocationRecord? record =
                FindRecord(overseer, target);

            if (record == null || record.steps <= 0)
            {
                return false;
            }

            record.steps--;

            if (record.steps <= 0)
            {
                RemoveRecord(record);
            }

            // 减档顺序：先监管者负面，再目标正面。
            SyncHediffsForOverseer(overseer);
            SyncHediffForTarget(target);

            SyncNormalStepsFromManualActual(
                overseer,
                target);

            return true;
        }

        public void SetSteps(
            Pawn? overseer,
            Pawn? target,
            int steps)
        {
            if (overseer == null || target == null)
            {
                return;
            }

            steps = Mathf.Max(0, steps);

            if (steps > 0
                && (!ResearchFeatureUnlockUtility
                        .IsDataProcessingAllocationUnlocked()
                    || !DataProcessingAllocationUtility
                        .IsValidAllocationPair(
                            overseer,
                            target)))
            {
                return;
            }

            if (IsDynamicAllocationEnabledForTarget(
                    overseer,
                    target))
            {
                DataProcessingDynamicTargetRecord config =
                    GetOrCreateDynamicTargetRecord(
                        overseer,
                        target);

                config.normalSteps = steps;
                config.Normalize();

                TriggerDynamicBudgetRecompute(overseer);
                return;
            }

            int oldSteps =
                GetStepsForOverseerTarget(
                    overseer,
                    target);

            SetActualStepsWithoutSync(
                overseer,
                target,
                steps);

            if (steps < oldSteps)
            {
                SyncHediffsForOverseer(overseer);
                SyncHediffForTarget(target);
            }
            else if (steps > oldSteps)
            {
                SyncHediffForTarget(target);
                SyncHediffsForOverseer(overseer);
            }
            else
            {
                SyncHediffForTarget(target);
            }

            SyncNormalStepsFromManualActual(
                overseer,
                target);
        }

        /// <summary>
        /// 非动态手动目标修改实际档数后，把单体配置中的常态额度同步为当前实际档数，
        /// 保持两者一致。动态托管目标不应调用（常态额度由玩家在设置中控制）。
        /// </summary>
        private void SyncNormalStepsFromManualActual(
            Pawn overseer,
            Pawn target)
        {
            DataProcessingDynamicTargetRecord? config =
                FindDynamicTargetRecord(overseer, target);

            if (config == null)
            {
                return;
            }

            config.normalSteps =
                GetStepsForOverseerTarget(
                    overseer,
                    target);

            config.Normalize();
        }

        public void ClearTarget(Pawn? target)
        {
            if (target == null)
            {
                return;
            }

            if (!recordByTarget.TryGetValue(target, out DataProcessingAllocationRecord? record))
            {
                RemoveAllCommandFocusHediffs(target);
                RemoveSpecializationRecordsForTarget(target);
                return;
            }

            Pawn? overseer = record.overseer;
            RemoveRecord(record);
            if (overseer != null && !overseer.Destroyed)
            {
                // 先降低监管者负面数据流分发，再清理目标正面指令聚焦。
                SyncHediffsForOverseer(overseer);
            }

            RemoveAllCommandFocusHediffs(target);
            RemoveSpecializationRecordsForTarget(target);
        }

        public void ClearOverseer(Pawn? overseer)
        {
            if (overseer == null)
            {
                return;
            }

            if (recordsByOverseer.TryGetValue(
                    overseer,
                    out List<DataProcessingAllocationRecord>? overseerRecords))
            {
                List<DataProcessingAllocationRecord> toRemove =
                    new List<DataProcessingAllocationRecord>(overseerRecords);

                Pawn? selfTarget = null;
                List<Pawn> otherTargets = new List<Pawn>();
                for (int i = 0; i < toRemove.Count; i++)
                {
                    DataProcessingAllocationRecord record = toRemove[i];
                    if (record?.target != null)
                    {
                        if (ReferenceEquals(record.target, overseer))
                        {
                            selfTarget = record.target;
                        }
                        else
                        {
                            otherTargets.Add(record.target);
                        }
                    }
                }

                // 先批量移除正数分配记录（仅清记录，不立即删目标正面）。
                for (int i = 0; i < toRemove.Count; i++)
                {
                    RemoveRecord(toRemove[i]);
                }

                // 先同步监管者负面数据流分发归零。
                RemoveHediff(
                    overseer,
                    DataProcessingAllocationUtility.DataStreamDistributionDef);

                // 再清理各目标正面指令聚焦（自身体指令聚焦最后清，确保半额返还先到位）。
                for (int i = 0; i < otherTargets.Count; i++)
                {
                    Pawn? target = otherTargets[i];
                    if (target != null && !target.Destroyed)
                    {
                        RemoveAllCommandFocusHediffs(target);
                    }
                }

                if (selfTarget != null && !selfTarget.Destroyed)
                {
                    RemoveAllCommandFocusHediffs(selfTarget);
                }
            }
            else
            {
                RemoveHediff(
                    overseer,
                    DataProcessingAllocationUtility.DataStreamDistributionDef);
            }

            // 无论是否存在正数分配，都统一清理该监管者的全部特化配置（含 0% 预选）。
            RemoveSpecializationRecordsForOverseer(overseer);

            // 监管者被移除时一并清理其动态分配开关与单体配置。
            RemoveDynamicAllocationRecordForOverseer(overseer);
            RemoveDynamicTargetRecordsForOverseer(overseer);
        }

        public bool IsDynamicAllocationEnabled(Pawn? overseer)
        {
            DataProcessingDynamicAllocationRecord? record = FindDynamicAllocationRecord(overseer);
            return record != null && record.enabled;
        }

        public bool TrySetDynamicAllocationEnabled(Pawn? overseer, bool enabled)
        {
            if (overseer == null
                || !ResearchFeatureUnlockUtility.IsDataProcessingAllocationUnlocked()
                || !IsDynamicAllocationOverseerValid(overseer)
                || overseer.mechanitor == null)
            {
                return false;
            }

            DataProcessingDynamicAllocationRecord? existing = FindDynamicAllocationRecord(overseer);
            if (existing != null)
            {
                if (existing.enabled == enabled)
                {
                    return true;
                }

                existing.enabled = enabled;
            }
            else
            {
                dynamicAllocationRecords.Add(
                    new DataProcessingDynamicAllocationRecord(overseer, enabled));
            }

            RebuildDynamicAllocationCaches();

            if (enabled)
            {
                // 开启后清空该监管者运行时缓存，立即对所有目标执行一次完整动态评估与计划。
                ForceEvaluateAllDynamicTargets(overseer);
            }
            else
            {
                // 关闭全局动态：所有目标实际额度恢复 normalSteps，特化安全恢复默认模式。
                // 失败时保留开关状态并返回 true，清空运行时状态并安排后续周期重试。
                if (!RestoreDefaultsForOverseer(overseer))
                {
                    ClearDynamicRuntimeStateForOverseer(overseer);
                    QueueGlobalTransitionRetry(overseer);
                }
            }

            return true;
        }

        /// <summary>
        /// 清空指定监管者所有目标的动态运行时评估缓存与下次检查时间（不触碰 Hediff/记录）。
        /// 用于全局开关关闭失败时放弃本轮部分状态，待重试重新评估。
        /// </summary>
        private void ClearDynamicRuntimeStateForOverseer(Pawn overseer)
        {
            if (overseer == null || dynamicTargetRecords == null)
            {
                return;
            }

            for (int i = 0; i < dynamicTargetRecords.Count; i++)
            {
                Pawn? target = dynamicTargetRecords[i]?.target;
                if (target != null
                    && FindDynamicTargetRecord(overseer, target) != null)
                {
                    cachedDynamicEvaluationByTarget.Remove(target);
                    nextDynamicCheckTickByTarget.Remove(target);
                }
            }
        }

        /// <summary>
        /// 排队全局开关切换失败的重试（关闭失败需重跑默认恢复，开启失败需重跑动态计划）。
        /// </summary>
        private void QueueGlobalTransitionRetry(Pawn overseer)
        {
            if (overseer == null)
            {
                return;
            }

            pendingGlobalTransitionOverseers.Add(overseer);
            globalTransitionRetryEarliestTick =
                Mathf.Min(
                    globalTransitionRetryEarliestTick == 0
                        ? int.MaxValue
                        : globalTransitionRetryEarliestTick,
                    Find.TickManager.TicksGame + 60);
        }

        /// <summary>
        /// 清空指定监管者所有目标的动态评估结果缓存，强制下一周期对每个目标重新评估真实状态。
        /// 用于全局动态开启、读档校正等需要立即刷新的场景，不修改 Hediff 或记录。
        /// </summary>
        private void ForceEvaluateAllDynamicTargets(Pawn overseer)
        {
            if (overseer == null || dynamicTargetRecords == null)
            {
                return;
            }

            for (int i = 0; i < dynamicTargetRecords.Count; i++)
            {
                Pawn? target = dynamicTargetRecords[i]?.target;
                if (target != null
                    && FindDynamicTargetRecord(overseer, target) != null)
                {
                    cachedDynamicEvaluationByTarget.Remove(target);
                    nextDynamicCheckTickByTarget.Remove(target);
                }
            }

            // 缓存清空后由下一周期调度器或即时计划重新评估；此处不立即同步 Hediff。
            if (IsDynamicAllocationEnabled(overseer))
            {
                RunDynamicPlanForOverseer(overseer);
            }
        }

        /// <summary>
        /// 全局关闭动态分配：复用统一安全计划，将所有目标视为固定手动请求
        /// （desiredSpecialization=defaultSpecialization，requestedSteps=normalSteps），
        /// 在 50% 绝对安全线内尽量恢复常态额度，所有当前模式恢复玩家默认模式。
        /// 不运行状态判断，不重新初始化默认模式，即使额度为 0 也更新特化记录为默认模式。
        /// </summary>
        private bool RestoreDefaultsForOverseer(Pawn overseer)
        {
            if (overseer == null || overseer.Destroyed)
            {
                return false;
            }

            List<Pawn> targets = new List<Pawn>();
            CollectDynamicAllocationTargets(overseer, targets);

            List<PlanEntry> entries = new List<PlanEntry>();
            for (int i = 0; i < targets.Count; i++)
            {
                Pawn target = targets[i];
                if (target == null || target.Destroyed)
                {
                    continue;
                }

                DataProcessingDynamicTargetRecord? config =
                    FindDynamicTargetRecord(overseer, target);
                if (config == null)
                {
                    config = GetOrCreateDynamicTargetRecord(overseer, target);
                }

                entries.Add(new PlanEntry
                {
                    target = target,
                    config = config,
                    desiredSpecialization = config.defaultSpecialization,
                    requestedSteps = config.normalSteps,
                    currentSteps = GetStepsForOverseerTarget(overseer, target),
                    currentSpecialization = GetSpecializationForOverseerTarget(overseer, target)
                });
            }

            if (entries.Count == 0)
            {
                return true;
            }

            // 全局关闭：所有目标视为固定手动请求，在 50% 绝对安全线内尽量恢复常态额度，
            // 所有模式恢复默认模式，保存的动态设置全部保留。
            return ApplyDynamicPlan(
                overseer,
                entries,
                treatAllEntriesAsFixed: true);
        }

        public DataProcessingDynamicAllocationRecord? FindDynamicAllocationRecordForUI(Pawn? overseer)
        {
            return FindDynamicAllocationRecord(overseer);
        }

        public bool IsValidAllocationPairForList(Pawn? overseer, Pawn? target)
        {
            return DataProcessingAllocationUtility.IsValidAllocationPair(overseer, target);
        }

        public bool SetDynamicMinConsciousnessPercent(Pawn? overseer, int percent)
        {
            if (overseer == null)
            {
                return false;
            }

            DataProcessingDynamicAllocationRecord? record =
                FindDynamicAllocationRecord(overseer);
            if (record == null)
            {
                return false;
            }

            record.minConsciousnessPercent = Mathf.Clamp(percent, 55, 1000);
            RebuildDynamicAllocationCaches();
            TriggerDynamicBudgetRecompute(overseer);
            return true;
        }

        public bool IsDynamicAllocationOverseerValid(Pawn? overseer)
        {
            return overseer != null
                && !overseer.Dead
                && !overseer.Destroyed
                && MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(overseer)
                && overseer.mechanitor != null
                && overseer.Faction != null
                && overseer.Faction.IsPlayerSafe();
        }

        public void RunDynamicAllocation()
        {
            if (!ResearchFeatureUnlockUtility
                    .IsDataProcessingAllocationUnlocked())
            {
                return;
            }

            CleanupInvalidDynamicAllocationRecords();

            if (dynamicAllocationRecords == null
                || dynamicAllocationRecords.Count == 0)
            {
                return;
            }

            List<DataProcessingDynamicAllocationRecord> snapshot =
                new List<DataProcessingDynamicAllocationRecord>(
                    dynamicAllocationRecords);

            for (int i = 0;
                 i < snapshot.Count;
                 i++)
            {
                DataProcessingDynamicAllocationRecord? record =
                    snapshot[i];

                if (record?.enabled != true
                    || record.overseer == null)
                {
                    continue;
                }

                try
                {
                    RunDynamicAllocationForOverseer(
                        record.overseer);
                }
                catch (Exception ex)
                {
                    Log.ErrorOnce(
                        "[MAP-机械族机械师] 执行监管者动态分配失败：" +
                        $"overseer={record.overseer.LabelShort}" +
                        $"（{record.overseer.ThingID}）：{ex}",
                        unchecked(
                            DynamicAllocationLogKeyBase
                            + record.overseer.thingIDNumber));
                }
            }
        }

        private void RunDynamicAllocationForOverseer(Pawn overseer)
        {
            RunDynamicPlanForOverseer(overseer);
        }

        private static int BuildDynamicAllocationLogKey(
            Pawn overseer,
            Pawn target)
        {
            return unchecked(
                DynamicAllocationLogKeyBase
                + overseer.thingIDNumber * 397
                + target.thingIDNumber);
        }

        private void CollectDynamicAllocationTargets(Pawn overseer, List<Pawn> outTargets)
        {
            outTargets.Clear();
            if (DataProcessingAllocationUtility.IsValidAllocationPair(overseer, overseer))
            {
                outTargets.Add(overseer);
            }

            if (overseer.mechanitor == null)
            {
                return;
            }

            List<Pawn> overseen = overseer.mechanitor.OverseenPawns;
            for (int i = 0; i < overseen.Count; i++)
            {
                Pawn target = overseen[i];
                if (!ReferenceEquals(target, overseer)
                    && DataProcessingAllocationUtility.IsValidAllocationPair(overseer, target))
                {
                    outTargets.Add(target);
                }
            }
        }

        /// <summary>
        /// 为所有启用动态分配监管者的目标建立/补全动态单体配置，并确保运行时评估缓存就绪。
        /// 读档校正前调用，避免下一周期首次评估时目标成员关系或评估缓存缺失。
        /// </summary>
        private void EnsureDynamicTargetsAndEvaluations()
        {
            if (dynamicAllocationRecords == null)
            {
                return;
            }

            for (int i = 0; i < dynamicAllocationRecords.Count; i++)
            {
                DataProcessingDynamicAllocationRecord? rec = dynamicAllocationRecords[i];
                if (rec?.enabled != true || rec.overseer == null || rec.overseer.Destroyed)
                {
                    continue;
                }

                List<Pawn> targets = new List<Pawn>();
                CollectDynamicAllocationTargets(rec.overseer, targets);
                for (int j = 0; j < targets.Count; j++)
                {
                    Pawn? target = targets[j];
                    if (target == null || target.Destroyed)
                    {
                        continue;
                    }

                    GetOrCreateDynamicTargetRecord(rec.overseer, target);

                    // 预置评估缓存的下一检查时间，使其在当前 tick 之后立即触发首次重评估。
                    if (target != null)
                    {
                        nextDynamicCheckTickByTarget[target] =
                            Math.Min(
                                nextDynamicCheckTickByTarget.TryGetValue(target, int.MaxValue),
                                Find.TickManager.TicksGame + 1);
                    }
                }
            }
        }

        private DataProcessingDynamicAllocationRecord? FindDynamicAllocationRecord(Pawn? overseer)
        {
            if (overseer == null || dynamicAllocationRecords == null)
            {
                return null;
            }

            for (int i = 0; i < dynamicAllocationRecords.Count; i++)
            {
                DataProcessingDynamicAllocationRecord? record = dynamicAllocationRecords[i];
                if (record != null && ReferenceEquals(record.overseer, overseer))
                {
                    return record;
                }
            }

            return null;
        }

        private void RebuildDynamicAllocationCaches()
        {
            dynamicAllocationRecordByOverseer =
                new Dictionary<Pawn, DataProcessingDynamicAllocationRecord>();
            if (dynamicAllocationRecords == null)
            {
                return;
            }

            for (int i = 0; i < dynamicAllocationRecords.Count; i++)
            {
                DataProcessingDynamicAllocationRecord? record = dynamicAllocationRecords[i];
                if (record?.overseer != null)
                {
                    dynamicAllocationRecordByOverseer[record.overseer] = record;
                }
            }
        }

        private void CleanupInvalidDynamicAllocationRecords()
        {
            dynamicAllocationRecords ??=
                new List<DataProcessingDynamicAllocationRecord>();

            HashSet<Pawn> seen =
                new HashSet<Pawn>();

            for (int i = dynamicAllocationRecords.Count - 1;
                 i >= 0;
                 i--)
            {
                DataProcessingDynamicAllocationRecord? record =
                    dynamicAllocationRecords[i];

                if (record == null
                    || record.overseer == null
                    || !IsDynamicAllocationOverseerValid(
                        record.overseer)
                    || !seen.Add(record.overseer))
                {
                    dynamicAllocationRecords.RemoveAt(i);
                }
            }

            RebuildDynamicAllocationCaches();
        }

        private void RemoveDynamicAllocationRecordForOverseer(Pawn? overseer)
        {
            if (overseer == null || dynamicAllocationRecords == null)
            {
                return;
            }

            for (int i = dynamicAllocationRecords.Count - 1; i >= 0; i--)
            {
                DataProcessingDynamicAllocationRecord? record = dynamicAllocationRecords[i];
                if (record != null && ReferenceEquals(record.overseer, overseer))
                {
                    dynamicAllocationRecords.RemoveAt(i);
                }
            }

            RebuildDynamicAllocationCaches();
        }

        // ===== 单体动态目标配置 =====

        public DataProcessingDynamicTargetRecord? GetDynamicTargetRecord(
            Pawn? overseer,
            Pawn? target)
        {
            return FindDynamicTargetRecord(overseer, target);
        }

        public bool IsDynamicAllocationEnabledForTarget(Pawn? overseer, Pawn? target)
        {
            if (!IsDynamicAllocationEnabled(overseer))
            {
                return false;
            }

            DataProcessingDynamicTargetRecord? record =
                FindDynamicTargetRecord(overseer, target);
            return record == null || record.enabled;
        }

        public int GetDynamicTargetNormalSteps(Pawn? overseer, Pawn? target)
        {
            DataProcessingDynamicTargetRecord? record =
                FindDynamicTargetRecord(overseer, target);
            return record != null ? Mathf.Max(0, record.normalSteps) : GetStepsForOverseerTarget(overseer, target);
        }

        /// <summary>
        /// 读取目标最近一次缓存的运行时动态状态；若无缓存则返回 Idle。
        /// 供 UI 与子对话框展示，避免重复评估。
        /// </summary>
        public DataProcessingDynamicState GetCachedDynamicStateForTarget(Pawn? target)
        {
            if (target != null
                && cachedDynamicEvaluationByTarget.TryGetValue(target, out DynamicEvaluation? cached)
                && cached != null)
            {
                return cached.state;
            }

            return DataProcessingDynamicState.Idle;
        }

        /// <summary>
        /// 读取目标最近一次缓存的运行时动态状态对应的统一展示标签。
        /// 所有 UI（主窗口、详情窗口）共用此入口，避免各处重复拼接翻译键导致不一致。
        /// </summary>
        public string GetCachedDynamicStateLabelForUI(Pawn? target)
        {
            DataProcessingDynamicState state = GetCachedDynamicStateForTarget(target);
            return ("MAP_DataProcessingAllocation_DynamicState" + state).Translate();
        }

        public DataProcessingDynamicTargetRecord GetOrCreateDynamicTargetRecord(
            Pawn overseer,
            Pawn target)
        {
            DataProcessingDynamicTargetRecord? existing =
                FindDynamicTargetRecord(overseer, target);
            if (existing != null)
            {
                return existing;
            }

            // 首次创建单体配置时，默认模式优先使用已有特化记录，避免覆盖玩家选择。
            int currentSteps = GetStepsForOverseerTarget(overseer, target);
            DataProcessingSpecialization existingSpecialization =
                GetSpecializationForOverseerTarget(overseer, target);
            DataProcessingSpecialization initialDefault =
                existingSpecialization != DataProcessingSpecialization.GeneralTuning
                    || FindSpecializationRecord(overseer, target) != null
                        ? existingSpecialization
                        : DataProcessingDynamicAllocationUtility
                            .DetermineInitialDefaultSpecialization(target);

            DataProcessingDynamicTargetRecord record =
                new DataProcessingDynamicTargetRecord(
                    overseer,
                    target,
                    currentSteps,
                    initialDefault);

            dynamicTargetRecords.Add(record);
            RebuildDynamicTargetCaches();
            ScheduleDynamicTargetCheck(target, record.checkIntervalTicks, immediate: true);
            return record;
        }

        private void ScheduleDynamicTargetCheck(
            Pawn target,
            int intervalTicks,
            bool immediate)
        {
            if (target == null)
            {
                return;
            }

            nextDynamicCheckTickByTarget[target] =
                immediate
                    ? Find.TickManager.TicksGame
                    : Find.TickManager.TicksGame + Mathf.Max(60, intervalTicks);
        }

        public bool SetDynamicAllocationEnabledForTarget(
            Pawn? overseer,
            Pawn? target,
            bool enabled)
        {
            if (overseer == null || target == null)
            {
                return false;
            }

            if (!IsDynamicAllocationEnabled(overseer))
            {
                return false;
            }

            DataProcessingDynamicTargetRecord? record =
                FindDynamicTargetRecord(overseer, target);

            if (record == null)
            {
                // 首次访问按需创建配置。
                record = GetOrCreateDynamicTargetRecord(overseer, target);
            }

            if (record.enabled == enabled)
            {
                return true;
            }

            record.enabled = enabled;
            RebuildDynamicTargetCaches();
            TriggerDynamicStateReevaluation(overseer, target);
            return true;
        }

        public bool SetDynamicTargetNormalSteps(
            Pawn? overseer,
            Pawn? target,
            int normalSteps)
        {
            if (overseer == null || target == null)
            {
                return false;
            }

            DataProcessingDynamicTargetRecord? record =
                FindDynamicTargetRecord(overseer, target);
            if (record == null)
            {
                record = GetOrCreateDynamicTargetRecord(overseer, target);
            }

            record.normalSteps = Mathf.Max(0, normalSteps);
            record.Normalize();
            TriggerDynamicBudgetRecompute(overseer);
            return true;
        }

        /// <summary>
        /// 仅更新默认模式（玩家设置），不立即切换当前实际模式：
        /// 当前模式由运行时节点的重新评估（TriggerDynamicStateReevaluation）在下一检查周期生效，
        /// 避免与调度器实时判定冲突。手动调整也可经由 TrySetManualSpecialization 同步默认模式。
        /// </summary>
        public bool SetDynamicTargetDefaultSpecialization(
            Pawn? overseer,
            Pawn? target,
            DataProcessingSpecialization specialization)
        {
            if (overseer == null || target == null)
            {
                return false;
            }

            DataProcessingDynamicTargetRecord? record =
                FindDynamicTargetRecord(overseer, target);
            if (record == null)
            {
                record = GetOrCreateDynamicTargetRecord(overseer, target);
            }

            record.defaultSpecialization =
                DataProcessingAllocationUtility
                    .NormalizeSpecialization(specialization);

            record.Normalize();
            RebuildDynamicTargetCaches();

            TriggerDynamicStateReevaluation(
                overseer,
                target);

            return true;
        }

        public bool SetDynamicTargetPriority(Pawn? overseer, Pawn? target, int priority)
        {
            if (overseer == null || target == null)
            {
                return false;
            }

            DataProcessingDynamicTargetRecord? record =
                FindDynamicTargetRecord(overseer, target);
            if (record == null)
            {
                record = GetOrCreateDynamicTargetRecord(overseer, target);
            }

            record.priority = Mathf.Clamp(priority, 1, 4);
            RebuildDynamicTargetCaches();
            TriggerDynamicBudgetRecompute(overseer);
            return true;
        }

        public bool SetDynamicTargetCheckInterval(
            Pawn? overseer,
            Pawn? target,
            int seconds)
        {
            if (overseer == null || target == null)
            {
                return false;
            }

            DataProcessingDynamicTargetRecord? record =
                FindDynamicTargetRecord(overseer, target);
            if (record == null)
            {
                record = GetOrCreateDynamicTargetRecord(overseer, target);
            }

            int ticks = Mathf.Max(60, seconds * 60);
            record.checkIntervalTicks = ticks;
            RebuildDynamicTargetCaches();
            TriggerDynamicStateReevaluation(overseer, target);
            return true;
        }

        public bool SetDynamicTargetCommonMaxSteps(
            Pawn? overseer,
            Pawn? target,
            int commonMaxSteps)
        {
            if (overseer == null || target == null)
            {
                return false;
            }

            DataProcessingDynamicTargetRecord? record =
                FindDynamicTargetRecord(overseer, target);
            if (record == null)
            {
                record = GetOrCreateDynamicTargetRecord(overseer, target);
            }

            record.commonMaxSteps = commonMaxSteps;
            record.Normalize();
            RebuildDynamicTargetCaches();
            TriggerDynamicBudgetRecompute(overseer);
            return true;
        }

        public bool SetDynamicTargetAdvancedMaxEnabled(
            Pawn? overseer,
            Pawn? target,
            bool advanced)
        {
            if (overseer == null || target == null)
            {
                return false;
            }

            DataProcessingDynamicTargetRecord? record =
                FindDynamicTargetRecord(overseer, target);
            if (record == null)
            {
                record = GetOrCreateDynamicTargetRecord(overseer, target);
            }

            record.advancedMaxEnabled = advanced;
            RebuildDynamicTargetCaches();
            TriggerDynamicBudgetRecompute(overseer);
            return true;
        }

        public bool SetDynamicTargetMaxStepsForSpecialization(
            Pawn? overseer,
            Pawn? target,
            DataProcessingSpecialization specialization,
            int maxSteps)
        {
            if (overseer == null || target == null)
            {
                return false;
            }

            DataProcessingDynamicTargetRecord? record =
                FindDynamicTargetRecord(overseer, target);
            if (record == null)
            {
                record = GetOrCreateDynamicTargetRecord(overseer, target);
            }

            specialization =
                DataProcessingAllocationUtility.NormalizeSpecialization(specialization);
            switch (specialization)
            {
                case DataProcessingSpecialization.GeneralTuning:
                    record.generalMaxSteps = maxSteps;
                    break;
                case DataProcessingSpecialization.ProductionCoordination:
                    record.productionMaxSteps = maxSteps;
                    break;
                case DataProcessingSpecialization.FireControlCalculation:
                    record.fireControlMaxSteps = maxSteps;
                    break;
                case DataProcessingSpecialization.AssaultProtocol:
                    record.assaultMaxSteps = maxSteps;
                    break;
            }

            record.Normalize();
            RebuildDynamicTargetCaches();
            TriggerDynamicBudgetRecompute(overseer);
            return true;
        }

        public bool SetDynamicTargetRule(
            Pawn? overseer,
            Pawn? target,
            string rule,
            bool value)
        {
            if (overseer == null || target == null)
            {
                return false;
            }

            DataProcessingDynamicTargetRecord? record =
                FindDynamicTargetRecord(overseer, target);
            if (record == null)
            {
                record = GetOrCreateDynamicTargetRecord(overseer, target);
            }

            switch (rule)
            {
                case "Work":
                    record.switchForWork = value;
                    break;
                case "DraftedWeapon":
                    record.switchForDraftedWeapon = value;
                    break;
                case "CloseMelee":
                    record.switchForCloseMelee = value;
                    break;
                case "UndraftedFallback":
                    record.applyUndraftedFallback = value;
                    break;
                default:
                    return false;
            }

            RebuildDynamicTargetCaches();
            TriggerDynamicStateReevaluation(overseer, target);
            return true;
        }

        /// <summary>
        /// 仅重算预算：应用于常态额度、最高额度、优先级、监管者最低数据处理阈值等
        /// 不影响目标实时状态判定的设置。
        /// </summary>
        private void TriggerDynamicBudgetRecompute(
            Pawn overseer)
        {
            if (IsDynamicAllocationEnabled(overseer))
            {
                RunDynamicPlanForOverseer(overseer);
            }
        }

        /// <summary>
        /// 立即重新评估目标状态（应用于单体开关、默认模式、检查间隔与切换规则），
        /// 评估后顺带重算预算。动态临时状态不会写回玩家默认模式。
        /// </summary>
        private void TriggerDynamicStateReevaluation(
            Pawn overseer,
            Pawn target)
        {
            DataProcessingDynamicTargetRecord? config =
                FindDynamicTargetRecord(
                    overseer,
                    target);

            if (config == null)
            {
                return;
            }

            cachedDynamicEvaluationByTarget[target] =
                EvaluateTarget(config, target);

            nextDynamicCheckTickByTarget[target] =
                Find.TickManager.TicksGame
                + Mathf.Max(
                    60,
                    config.checkIntervalTicks);

            TriggerDynamicBudgetRecompute(overseer);
        }

        private DataProcessingDynamicTargetRecord? FindDynamicTargetRecord(
            Pawn? overseer,
            Pawn? target)
        {
            if (target == null || dynamicTargetRecords == null)
            {
                return null;
            }

            if (dynamicTargetRecordByTarget.TryGetValue(target, out DataProcessingDynamicTargetRecord? cached)
                && cached != null
                && (overseer == null || ReferenceEquals(cached.overseer, overseer)))
            {
                return cached;
            }

            for (int i = 0; i < dynamicTargetRecords.Count; i++)
            {
                DataProcessingDynamicTargetRecord? record = dynamicTargetRecords[i];
                if (record != null
                    && ReferenceEquals(record.target, target)
                    && (overseer == null || ReferenceEquals(record.overseer, overseer)))
                {
                    return record;
                }
            }

            return null;
        }

        private void RebuildDynamicTargetCaches()
        {
            dynamicTargetRecordByTarget =
                new Dictionary<Pawn, DataProcessingDynamicTargetRecord>();
            if (dynamicTargetRecords == null)
            {
                return;
            }

            for (int i = 0; i < dynamicTargetRecords.Count; i++)
            {
                DataProcessingDynamicTargetRecord? record = dynamicTargetRecords[i];
                if (record?.target != null)
                {
                    dynamicTargetRecordByTarget[record.target] = record;
                }
            }
        }

        private void CleanupInvalidDynamicTargetRecords(
            HashSet<Pawn>? affectedOverseers = null)
        {
            dynamicTargetRecords ??=
                new List<DataProcessingDynamicTargetRecord>();

            // 配置有效性不取决于全局动态开关是否开启；全局关闭也必须保留单体配置。
            Dictionary<Pawn, DataProcessingDynamicTargetRecord> retainedByTarget =
                new Dictionary<Pawn, DataProcessingDynamicTargetRecord>();

            for (int i = dynamicTargetRecords.Count - 1; i >= 0; i--)
            {
                DataProcessingDynamicTargetRecord? record = dynamicTargetRecords[i];

                Pawn? target = record?.target;

                if (record == null
                    || target == null
                    || target.Dead
                    || target.Destroyed
                    || target.Faction == null
                    || !target.Faction.IsPlayerSafe())
                {
                    // 失效记录删除：旧监管者需重跑计划以同步仍有效的目标。
                    if (affectedOverseers != null && record?.overseer != null)
                    {
                        affectedOverseers.Add(record.overseer);
                    }

                    RemoveDynamicTargetRecordAt(i);
                    continue;
                }

                Pawn? currentOverseer = FindOverseerGoverningTarget(target);

                if (currentOverseer != null
                    && !ReferenceEquals(record.overseer, currentOverseer))
                {
                    Pawn? oldOverseer = record.overseer;

                    record.overseer = currentOverseer;

                    cachedDynamicEvaluationByTarget.Remove(target);
                    nextDynamicCheckTickByTarget.Remove(target);

                    // 迁移：旧监管者（失去目标）与新监管者（接管目标）都需重跑。
                    if (affectedOverseers != null)
                    {
                        if (oldOverseer != null)
                        {
                            affectedOverseers.Add(oldOverseer);
                        }

                        affectedOverseers.Add(currentOverseer);
                    }
                }

                // 暂时没有监管者时保留配置，以便以后重新接管时继续使用。
                record.Normalize();

                if (retainedByTarget.ContainsKey(target))
                {
                    // 同一 target 出现重复配置（如监管者切换未完成），保留最新有效项，清理重复。
                    // 不清除该目标运行时缓存，保留项仍复用之。
                    if (affectedOverseers != null && record.overseer != null)
                    {
                        affectedOverseers.Add(record.overseer);
                    }

                    RemoveDynamicTargetRecordAt(i, clearRuntimeState: false);
                    continue;
                }

                retainedByTarget[target] = record;
            }

            RebuildDynamicTargetCaches();
        }

        private void RemoveDynamicTargetRecordAt(int index, bool clearRuntimeState = true)
        {
            DataProcessingDynamicTargetRecord? record = dynamicTargetRecords[index];
            Pawn? target = record?.target;

            dynamicTargetRecords.RemoveAt(index);

            if (target != null && clearRuntimeState)
            {
                nextDynamicCheckTickByTarget.Remove(target);
                cachedDynamicEvaluationByTarget.Remove(target);
            }
        }

        /// <summary>
        /// 查找当前实际管辖指定目标的监管者（自身分配或机械师监管），用于动态配置迁移。
        /// </summary>
        private Pawn? FindOverseerGoverningTarget(Pawn target)
        {
            if (target == null)
            {
                return null;
            }

            // 优先查外部监管者，避免把本身也是机械族机械师、但实际正被其他监管的目标误判给自己。
            Pawn? externalOverseer = target.GetOverseer();
            if (externalOverseer != null
                && DataProcessingAllocationUtility.IsValidAllocationPair(
                    externalOverseer,
                    target))
            {
                return externalOverseer;
            }

            // 无外部监管者时，才考虑自身分配（target 自身即监管者）。
            if (DataProcessingAllocationUtility.IsValidAllocationPair(target, target))
            {
                return target;
            }

            return null;
        }

        /// <summary>
        /// 按引用（而非 Equals）比较 Pawn 的相等性，用于以 Pawn 为键的字典，
        /// 避免依赖 Pawn 的值相等实现导致同一实体的不同实例被误判为相同。
        /// </summary>
        private sealed class ReferencePawnEqualityComparer : IEqualityComparer<Pawn>
        {
            public bool Equals(Pawn? x, Pawn? y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(Pawn obj)
            {
                return obj == null ? 0 : obj.thingIDNumber;
            }
        }

        private void RemoveDynamicTargetRecordsForOverseer(Pawn? overseer)
        {
            if (overseer == null || dynamicTargetRecords == null)
            {
                return;
            }

            for (int i = dynamicTargetRecords.Count - 1; i >= 0; i--)
            {
                DataProcessingDynamicTargetRecord? record = dynamicTargetRecords[i];
                if (record != null && ReferenceEquals(record.overseer, overseer))
                {
                    if (record.target != null)
                    {
                        nextDynamicCheckTickByTarget.Remove(record.target);
                        cachedDynamicEvaluationByTarget.Remove(record.target);
                    }

                    dynamicTargetRecords.RemoveAt(i);
                }
            }

            RebuildDynamicTargetCaches();
        }

        /// <summary>
        /// 监管者替换时迁移目标单体动态配置：将旧监管者名下的动态目标配置重新指向新监管者，
        /// 保留玩家已设的常态额度、最高额度、优先级、检查间隔与规则开关；若新监管者已存在
        /// 同名目标配置，则保留已有配置不改写。不新增/删除任何规则或 Hediff。
        /// </summary>
        private void MigrateDynamicTargetConfigToNewOverseer(
            Pawn oldOverseer,
            Pawn newOverseer,
            Pawn target)
        {
            if (oldOverseer == null
                || newOverseer == null
                || target == null
                || dynamicTargetRecords == null)
            {
                return;
            }

            // 新监管者已存在该目标配置则无需迁移，避免覆盖。
            if (FindDynamicTargetRecord(newOverseer, target) != null)
            {
                return;
            }

            DataProcessingDynamicTargetRecord? existing =
                FindDynamicTargetRecord(oldOverseer, target);
            if (existing == null)
            {
                return;
            }

            existing.overseer = newOverseer;
            existing.Normalize();

            cachedDynamicEvaluationByTarget.Remove(target);
            nextDynamicCheckTickByTarget.Remove(target);

            RebuildDynamicTargetCaches();
        }

        // ===== 轻量调度与批量计划 =====

        private void TickDynamicScheduler()
        {
            int now = Find.TickManager.TicksGame;

            List<Pawn> dueTargets =
                new List<Pawn>();

            foreach (KeyValuePair<Pawn, int> pair
                     in nextDynamicCheckTickByTarget)
            {
                Pawn target = pair.Key;

                if (target == null
                    || target.Destroyed)
                {
                    continue;
                }

                if (pair.Value <= now)
                {
                    dueTargets.Add(target);
                }
            }

            if (dueTargets.Count == 0)
            {
                return;
            }

            HashSet<Pawn> affectedOverseers =
                new HashSet<Pawn>();

            for (int i = 0;
                 i < dueTargets.Count;
                 i++)
            {
                Pawn target = dueTargets[i];

                DataProcessingDynamicTargetRecord? config =
                    FindDynamicTargetRecord(
                        overseer: null,
                        target);

                if (config?.overseer == null
                    || !config.enabled
                    || !IsDynamicAllocationEnabled(
                        config.overseer))
                {
                    nextDynamicCheckTickByTarget.Remove(
                        target);
                    cachedDynamicEvaluationByTarget.Remove(
                        target);
                    continue;
                }

                cachedDynamicEvaluationByTarget[target] =
                    EvaluateTarget(config, target);

                nextDynamicCheckTickByTarget[target] =
                    now + Mathf.Max(
                        60,
                        config.checkIntervalTicks);

                affectedOverseers.Add(
                    config.overseer);
            }

            foreach (Pawn overseer
                     in affectedOverseers)
            {
                RunDynamicPlanForOverseer(overseer);
            }
        }

        /// <summary>
        /// 单体目标评估：生成运行时状态、期望特化与是否为任务状态。
        /// 结果会被缓存到 cachedDynamicEvaluationByTarget，仅在目标下次检查到期时由调度器刷新。
        /// </summary>
        private DynamicEvaluation EvaluateTarget(
            DataProcessingDynamicTargetRecord config,
            Pawn target)
        {
            DynamicEvaluation result =
                new DynamicEvaluation
                {
                    overseer = config.overseer,
                    state = DataProcessingDynamicState.Idle,
                    specialization =
                        config.defaultSpecialization,
                    taskActive = false
                };

            // 1. 确认近战接战。
            if (config.switchForCloseMelee
                && DataProcessingDynamicAllocationUtility
                    .IsConfirmedCloseMeleeEngagement(
                        target,
                        config.checkIntervalTicks))
            {
                result.state =
                    DataProcessingDynamicState.CloseMelee;
                result.specialization =
                    DataProcessingSpecialization
                        .AssaultProtocol;
                result.taskActive = true;
                return result;
            }

            // 2. 征召武器判断。
            if (target.Drafted
                && config.switchForDraftedWeapon)
            {
                Verb? verb =
                    DataProcessingDynamicAllocationUtility
                        .GetCurrentAttackVerb(target);

                if (verb?.verbProps != null)
                {
                    if (verb.IsMeleeAttack
                        || verb.verbProps.IsMeleeAttack)
                    {
                        result.state =
                            DataProcessingDynamicState
                                .DraftedMelee;
                        result.specialization =
                            DataProcessingSpecialization
                                .AssaultProtocol;
                        result.taskActive = true;
                        return result;
                    }

                    if (verb.verbProps.violent
                        && verb.verbProps.ai_IsWeapon
                        && verb.verbProps.range > 1.42f)
                    {
                        result.state =
                            DataProcessingDynamicState
                                .DraftedRanged;
                        result.specialization =
                            DataProcessingSpecialization
                                .FireControlCalculation;
                        result.taskActive = true;
                        return result;
                    }
                }

                bool hasRanged =
                    DataProcessingDynamicAllocationUtility
                        .HasRangedAttackVerb(target);

                bool hasMelee =
                    DataProcessingDynamicAllocationUtility
                        .IsMeleeCombatMech(target);

                // 同时拥有远程和近战能力时，
                // 在没有确认近战接战的情况下优先远程。
                if (hasRanged)
                {
                    result.state =
                        DataProcessingDynamicState
                            .DraftedRanged;
                    result.specialization =
                        DataProcessingSpecialization
                            .FireControlCalculation;
                    result.taskActive = true;
                    return result;
                }

                if (hasMelee)
                {
                    result.state =
                        DataProcessingDynamicState
                            .DraftedMelee;
                    result.specialization =
                        DataProcessingSpecialization
                            .AssaultProtocol;
                    result.taskActive = true;
                    return result;
                }

                // 已征召但无法识别攻击方式：
                // 使用默认模式，但不视为成功触发动态任务状态。
                return result;
            }

            // 3. 只有未征召时才能识别工作。
            if (!target.Drafted
                && config.switchForWork
                && IsPawnDoingWork(target))
            {
                result.state =
                    DataProcessingDynamicState.Working;
                result.specialization =
                    DataProcessingSpecialization
                        .ProductionCoordination;
                result.taskActive = true;
                return result;
            }

            // 4. 未征召自动回落。
            if (!target.Drafted
                && config.applyUndraftedFallback)
            {
                if (DataProcessingDynamicAllocationUtility
                        .HasEnabledMechWorkTypes(target))
                {
                    result.specialization =
                        DataProcessingSpecialization
                            .ProductionCoordination;
                    return result;
                }

                bool hasRanged =
                    DataProcessingDynamicAllocationUtility
                        .HasRangedAttackVerb(target);

                bool hasMelee =
                    DataProcessingDynamicAllocationUtility
                        .IsMeleeCombatMech(target);

                if (hasRanged)
                {
                    result.specialization =
                        DataProcessingSpecialization
                            .FireControlCalculation;
                    return result;
                }

                if (hasMelee)
                {
                    result.specialization =
                        DataProcessingSpecialization
                            .AssaultProtocol;
                    return result;
                }
            }

            return result;
        }

        /// <summary>
        /// 单体目标运行时评估结果缓存：包含状态、期望特化与是否处于任务状态。
        /// 不在此缓存过期时间；下一次检查时间只由 nextDynamicCheckTickByTarget 管理。
        /// </summary>
        private sealed class DynamicEvaluation
        {
            public Pawn? overseer;
            public DataProcessingDynamicState state;
            public DataProcessingSpecialization specialization;
            public bool taskActive;
        }

        /// <summary>
        /// 获取目标评估：已有同监管者缓存直接复用，否则评估并缓存，
        /// 并在缺少检查时间时安排下一次检查。
        /// </summary>
        private DynamicEvaluation GetOrEvaluateTarget(
            DataProcessingDynamicTargetRecord config,
            Pawn target)
        {
            if (cachedDynamicEvaluationByTarget.TryGetValue(
                    target,
                    out DynamicEvaluation? cached)
                && cached != null
                && ReferenceEquals(
                    cached.overseer,
                    config.overseer))
            {
                return cached;
            }

            DynamicEvaluation evaluation =
                EvaluateTarget(config, target);

            cachedDynamicEvaluationByTarget[target] =
                evaluation;

            if (!nextDynamicCheckTickByTarget
                    .ContainsKey(target))
            {
                nextDynamicCheckTickByTarget[target] =
                    Find.TickManager.TicksGame
                    + Mathf.Max(
                        60,
                        config.checkIntervalTicks);
            }

            return evaluation;
        }

        private static bool IsDynamicCloseMeleeEngagement(
            Pawn target,
            int checkIntervalTicks)
        {
            return DataProcessingDynamicAllocationUtility.IsConfirmedCloseMeleeEngagement(
                target,
                checkIntervalTicks);
        }

        private static bool IsPawnDoingWork(Pawn target)
        {
            if (target == null || target.CurJob == null)
            {
                return false;
            }

            Job job = target.CurJob;
            if (job.workGiverDef != null)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 批量计算并安全应用某个监管者的完整动态计划。
        /// 返回是否成功（基础意识读取失败时返回 false，不修改任何记录或 Hediff）。
        /// 预算重算不会重置所有目标的检查时间（由调度器单独维护）。
        /// </summary>
        private bool RunDynamicPlanForOverseer(Pawn overseer)
        {
            if (overseer == null
                || overseer.Destroyed
                || !IsDynamicAllocationOverseerValid(overseer)
                || !IsDynamicAllocationEnabled(overseer))
            {
                return false;
            }

            List<Pawn> targets = new List<Pawn>();
            CollectDynamicAllocationTargets(overseer, targets);

            // 收集并评估所有目标。
            List<PlanEntry> entries = new List<PlanEntry>();
            for (int i = 0; i < targets.Count; i++)
            {
                Pawn target = targets[i];
                if (target == null || target.Destroyed)
                {
                    continue;
                }

                DataProcessingDynamicTargetRecord? config =
                    FindDynamicTargetRecord(overseer, target);
                if (config == null)
                {
                    config = GetOrCreateDynamicTargetRecord(overseer, target);
                }

                // 单体动态关闭：不运行状态评估，实际额度固定为常态额度，模式恢复默认。
                // 跳过 GetOrEvaluateTarget，避免无谓的实时检查与缓存刷新。
                if (!config.enabled)
                {
                    entries.Add(new PlanEntry
                    {
                        target = target,
                        config = config,
                        desiredSpecialization = config.defaultSpecialization,
                        requestedSteps = config.normalSteps,
                        currentSteps = GetStepsForOverseerTarget(overseer, target),
                        currentSpecialization =
                            GetSpecializationForOverseerTarget(overseer, target)
                    });
                    continue;
                }

                DynamicEvaluation evaluation =
                    GetOrEvaluateTarget(config, target);

                DataProcessingSpecialization desiredSpecialization =
                    evaluation.specialization;
                int requestedSteps = evaluation.taskActive
                    ? config.GetMaxStepsForSpecialization(evaluation.specialization)
                    : config.normalSteps;

                entries.Add(new PlanEntry
                {
                    target = target,
                    config = config,
                    desiredSpecialization = desiredSpecialization,
                    requestedSteps = requestedSteps,
                    currentSteps = GetStepsForOverseerTarget(overseer, target),
                    currentSpecialization = GetSpecializationForOverseerTarget(overseer, target)
                });
            }

            if (entries.Count == 0)
            {
                return true;
            }

            return ApplyDynamicPlan(overseer, entries);
        }

        /// <summary>
        /// 计算预算并生成最终实际档数。
        /// 固定（单体动态关闭）目标先受 50% 绝对安全线约束分配；动态目标从零档起步，
        /// 按优先级 1→4 分两阶段（先同级轮流满足常态额度，再同级轮流满足任务增量）分配。
        /// 基础意识读取失败时必须返回 false，不得修改记录或 Hediff。
        /// </summary>
        private bool ApplyDynamicPlan(
            Pawn overseer,
            List<PlanEntry> entries,
            bool treatAllEntriesAsFixed = false)
        {
            if (overseer == null || entries == null)
            {
                return false;
            }

            DataProcessingDynamicAllocationRecord? overseerRecord =
                FindDynamicAllocationRecord(overseer);

            // 可靠读取基础数据处理；读取失败则本轮放弃。
            if (!TryCalculateAllocationFreeConsciousness(overseer, out float baseConsciousness))
            {
                return false;
            }

            float dynamicThreshold =
                treatAllEntriesAsFixed
                    ? DataProcessingAllocationUtility.MinReservedConsciousness
                    : Mathf.Max(
                        0.55f,
                        overseerRecord != null
                            ? overseerRecord.minConsciousnessPercent / 100f
                            : 1f);

            float absoluteThreshold =
                DataProcessingAllocationUtility
                    .MinReservedConsciousness;

            // 原始容量（保留语义，不参与扣减）。
            int dynamicBudgetCapacityUnits =
                Mathf.FloorToInt(
                    Mathf.Max(
                        0f,
                        baseConsciousness - dynamicThreshold)
                    / 0.025f
                    + 0.0001f);

            int absoluteBudgetCapacityUnits =
                Mathf.FloorToInt(
                    Mathf.Max(
                        0f,
                        baseConsciousness - absoluteThreshold)
                    / 0.025f
                    + 0.0001f);

            // 剩余预算（仅用于扣减）。
            int remainingAbsoluteBudgetUnits =
                absoluteBudgetCapacityUnits;

            // 最终实际档数字典：所有条目一律从 0 起步，绝不在预算前预填常态额度。
            Dictionary<PlanEntry, int> finalSteps = new Dictionary<PlanEntry, int>();

            // 固定（单体动态关闭，或全局关闭视为固定）目标分组，先受 50% 绝对安全线保护。
            List<PlanEntry> fixedEntries = new List<PlanEntry>();
            List<PlanEntry> dynamicEntries = new List<PlanEntry>();

            for (int i = 0; i < entries.Count; i++)
            {
                PlanEntry entry = entries[i];

                finalSteps[entry] = 0;

                bool fixedRequest =
                    treatAllEntriesAsFixed
                    || !entry.config.enabled;

                if (fixedRequest)
                {
                    fixedEntries.Add(entry);
                }
                else
                {
                    dynamicEntries.Add(entry);
                }
            }

            // 固定目标按优先级 1→4、同级逐档轮流分配到绝对安全剩余预算。
            List<PlanEntry>[] fixedByPriority =
                GetDynamicEntriesForPriority(overseer, fixedEntries);
            int fixedUsedUnits = 0;
            for (int p = 0; p < fixedByPriority.Length; p++)
            {
                AllocateRoundRobinToGoal(
                    overseer,
                    fixedByPriority[p],
                    finalSteps,
                    entry => Mathf.Max(0, entry.config.normalSteps),
                    ref remainingAbsoluteBudgetUnits);

                for (int i = 0; i < fixedByPriority[p].Count; i++)
                {
                    fixedUsedUnits +=
                        GetBudgetCostUnits(overseer, fixedByPriority[p][i].target, 1)
                        * finalSteps[fixedByPriority[p][i]];
                }
            }

            // 计算固定目标的原始请求总量，比较原始容量（不比较剩余预算）。
            int fixedRequestedUnits = 0;
            for (int i = 0; i < fixedEntries.Count; i++)
            {
                fixedRequestedUnits +=
                    GetBudgetCostUnits(overseer, fixedEntries[i].target, 1)
                    * Mathf.Max(0, fixedEntries[i].config.normalSteps);
            }

            if (fixedRequestedUnits > absoluteBudgetCapacityUnits)
            {
                // 固定请求超过 50% 绝对安全预算：输出一次警告；不强行突破 50%，
                // 实际额度可能低于常态请求，但不修改其保存的 normalSteps。
                Log.WarningOnce(
                    "[MAP-机械族机械师] 动态固定分配请求超过 50% 绝对安全线，" +
                    "已临时限制实际额度以保护监管者意识：" +
                    $"overseer={overseer.LabelShort}（{overseer.ThingID}）。",
                    unchecked(
                        DynamicAllocationLogKeyBase
                        + overseer.thingIDNumber
                        + 0x100000));
            }

            // 动态目标可用预算 = 动态容量 - 固定目标真实占用（不重复扣除，不被绝对剩余污染）。
            int remainingDynamicBudgetUnits =
                Mathf.Max(
                    0,
                    dynamicBudgetCapacityUnits - fixedUsedUnits);

            // 动态目标按优先级 1→4 分两阶段：先同级轮流满足常态额度，再同级轮流满足任务增量。
            List<PlanEntry>[] dynamicByPriority =
                GetDynamicEntriesForPriority(overseer, dynamicEntries);
            for (int priority = 1;
                 priority <= 4
                 && remainingDynamicBudgetUnits > 0;
                 priority++)
            {
                List<PlanEntry> group = dynamicByPriority[priority - 1];
                SortDynamicEntriesForStableAllocation(overseer, group);

                // 第一阶段：同级先轮流满足常态额度。
                AllocateRoundRobinToGoal(
                    overseer,
                    group,
                    finalSteps,
                    entry => Mathf.Min(
                        entry.config.normalSteps,
                        entry.requestedSteps),
                    ref remainingDynamicBudgetUnits);

                // 第二阶段：同级再轮流满足任务增量。
                AllocateRoundRobinToGoal(
                    overseer,
                    group,
                    finalSteps,
                    entry => entry.requestedSteps,
                    ref remainingDynamicBudgetUnits);
            }

            return BuildAndApplyPlan(overseer, entries, finalSteps);
        }

        /// <summary>
        /// 可靠读取基础数据处理：当前意识 + 数据流分发负面完整贡献 - 自我指令聚焦返还（0.5×）。
        /// 读取失败返回 false，不得用于后续计算。
        /// </summary>
        private bool TryCalculateAllocationFreeConsciousness(
            Pawn overseer,
            out float baseConsciousness)
        {
            baseConsciousness = 0f;

            if (!DataProcessingAllocationUtility
                    .TryGetCurrentConsciousness(
                        overseer,
                        out float current))
            {
                return false;
            }

            baseConsciousness = current;

            // 数据流分发存在时，它按注册表总档数提供完整负面。
            HediffDef? streamDef =
                DataProcessingAllocationUtility
                    .DataStreamDistributionDef;

            bool hasStream =
                streamDef != null
                && overseer.health?.hediffSet?
                    .HasHediff(streamDef) == true;

            if (hasStream)
            {
                baseConsciousness +=
                    GetTotalStepsForOverseer(overseer)
                    * DataProcessingAllocationUtility
                        .StepPercent;
            }

            // 只有监管者自身的指令聚焦会影响监管者意识，
            // 且自我返还倍率为0.5。
            DataProcessingAllocationRecord? selfRecord =
                FindRecord(overseer, overseer);

            if (selfRecord != null
                && selfRecord.steps > 0
                && HasAnyCommandFocusHediff(overseer))
            {
                baseConsciousness -=
                    selfRecord.steps
                    * DataProcessingAllocationUtility
                        .StepPercent
                    * 0.5f;
            }

            return !float.IsNaN(baseConsciousness)
                && !float.IsInfinity(baseConsciousness);
        }

        /// <summary>
        /// 同级内逐档轮流分配，直到该组所有目标达到目标档数或预算耗尽。
        /// </summary>
        private void AllocateRoundRobinToGoal(
            Pawn overseer,
            List<PlanEntry> group,
            Dictionary<PlanEntry, int> finalSteps,
            Func<PlanEntry, int> goalSelector,
            ref int remainingBudget)
        {
            if (group == null
                || group.Count == 0
                || remainingBudget <= 0)
            {
                return;
            }

            bool changed;

            do
            {
                changed = false;

                for (int i = 0;
                     i < group.Count;
                     i++)
                {
                    PlanEntry entry = group[i];

                    int current =
                        finalSteps[entry];

                    int goal =
                        Mathf.Max(
                            0,
                            goalSelector(entry));

                    if (current >= goal)
                    {
                        continue;
                    }

                    int cost =
                        GetBudgetCostUnits(
                            overseer,
                            entry.target,
                            1);

                    if (remainingBudget < cost)
                    {
                        continue;
                    }

                    finalSteps[entry] =
                        current + 1;

                    remainingBudget -= cost;
                    changed = true;
                }
            }
            while (changed
                && remainingBudget > 0);
        }

        /// <summary>
        /// 单个目标提升 1 档所需的预算单位。自身净消耗半额（1 单位），其他 2 单位。
        /// </summary>
        private static int GetBudgetCostUnits(Pawn overseer, Pawn target, int steps)
        {
            bool selfPair = DataProcessingAllocationUtility.IsSelfAllocationPair(
                overseer,
                target);
            return steps * (selfPair ? 1 : 2);
        }

        /// <summary>
        /// 将动态目标按优先级分组并各自排序为稳定顺序。
        /// </summary>
        private List<PlanEntry>[] GetDynamicEntriesForPriority(
            Pawn overseer,
            List<PlanEntry> dynamicEntries)
        {
            List<PlanEntry>[] byPriority =
            {
                new List<PlanEntry>(),
                new List<PlanEntry>(),
                new List<PlanEntry>(),
                new List<PlanEntry>()
            };

            foreach (PlanEntry entry in dynamicEntries)
            {
                int idx = Mathf.Clamp(entry.config.priority - 1, 0, 3);
                byPriority[idx].Add(entry);
            }

            for (int p = 0; p < byPriority.Length; p++)
            {
                SortDynamicEntriesForStableAllocation(overseer, byPriority[p]);
            }

            return byPriority;
        }

        /// <summary>
        /// 同级稳定顺序：顶置优先 → pinOrder → thingIDNumber。
        /// </summary>
        private void SortDynamicEntriesForStableAllocation(
            Pawn overseer,
            List<PlanEntry> entries)
        {
            if (entries == null)
            {
                return;
            }

            entries.Sort((left, right) =>
            {
                bool leftPinned = IsPinned(overseer, left.target);
                bool rightPinned = IsPinned(overseer, right.target);
                if (leftPinned != rightPinned)
                {
                    return leftPinned ? -1 : 1;
                }

                int lo = leftPinned ? GetPinOrder(overseer, left.target) : int.MaxValue;
                int ro = rightPinned ? GetPinOrder(overseer, right.target) : int.MaxValue;
                int po = lo.CompareTo(ro);
                if (po != 0)
                {
                    return po;
                }

                return left.target.thingIDNumber.CompareTo(right.target.thingIDNumber);
            });
        }

        /// <summary>
        /// 批量安全提交动态计划：先批量减档（记录→监管者负面→目标正面），
        /// 再安全切换特化，最后批量加档（记录→目标正面→监管者负面）。
        /// 特化切换失败的目标不提高其额度。返回是否全部特化切换成功。
        /// </summary>
        private bool BuildAndApplyPlan(
            Pawn overseer,
            List<PlanEntry> entries,
            Dictionary<PlanEntry, int> finalSteps)
        {
            List<PlanEntry> toReduce = new List<PlanEntry>();
            List<PlanEntry> toRaise = new List<PlanEntry>();
            List<PlanEntry> toSwitch = new List<PlanEntry>();

            foreach (PlanEntry entry in entries)
            {
                int desired =
                    finalSteps.TryGetValue(
                        entry,
                        out int value)
                        ? Mathf.Max(0, value)
                        : 0;

                int current =
                    Mathf.Max(
                        0,
                        entry.currentSteps);

                if (desired < current)
                {
                    toReduce.Add(entry);
                }
                else if (desired > current)
                {
                    toRaise.Add(entry);
                }

                // 即使最终档数为0，也要更新保存的当前模式记录。
                if (entry.desiredSpecialization
                    != entry.currentSpecialization)
                {
                    toSwitch.Add(entry);
                }
            }

            // 第一阶段：批量减档（先改记录，再统一负面，再统一正面）。
            foreach (PlanEntry entry in toReduce)
            {
                SetActualStepsWithoutSync(
                    overseer,
                    entry.target,
                    finalSteps[entry]);
            }

            if (toReduce.Count > 0)
            {
                SyncHediffsForOverseer(overseer);

                foreach (PlanEntry entry in toReduce)
                {
                    SyncHediffForTarget(entry.target);
                }
            }

            // 第二阶段：安全切换模式。
            bool allSucceeded = true;
            HashSet<PlanEntry> failedSwitches =
                new HashSet<PlanEntry>();

            foreach (PlanEntry entry in toSwitch)
            {
                if (!TrySetSpecialization(
                        overseer,
                        entry.target,
                        entry.desiredSpecialization))
                {
                    allSucceeded = false;
                    failedSwitches.Add(entry);

                    Log.ErrorOnce(
                        "[MAP-机械族机械师] 动态计划切换特化失败：" +
                        $"overseer={overseer.LabelShort}（{overseer.ThingID}），" +
                        $"target={entry.target.LabelShort}（{entry.target.ThingID}）。",
                        BuildDynamicAllocationLogKey(
                            overseer,
                            entry.target));
                }
            }

            // 第三阶段：批量加档（先改记录，再统一正面，再统一负面）。
            List<PlanEntry> actualRaises =
                new List<PlanEntry>();

            foreach (PlanEntry entry in toRaise)
            {
                if (failedSwitches.Contains(entry))
                {
                    continue;
                }

                SetActualStepsWithoutSync(
                    overseer,
                    entry.target,
                    finalSteps[entry]);

                actualRaises.Add(entry);
            }

            foreach (PlanEntry entry in actualRaises)
            {
                SyncHediffForTarget(entry.target);
            }

            if (actualRaises.Count > 0)
            {
                SyncHediffsForOverseer(overseer);
            }

            // ===== 最终一致性阶段 =====
            // 即使本轮无减档/加档/切换，也需确保最终正面状态存在（恢复读档被安全移除或丢失的 Hediff）。
            bool allPositiveReady = true;
            foreach (PlanEntry entry in entries)
            {
                int desired =
                    finalSteps.TryGetValue(
                        entry,
                        out int value)
                        ? Mathf.Max(0, value)
                        : 0;

                if (desired <= 0)
                {
                    continue;
                }

                if (!TryApplyCommandFocusHediffForTarget(
                        entry.target,
                        entry.desiredSpecialization,
                        desired,
                        cleanupOtherCommandFocusHediffs: true))
                {
                    allPositiveReady = false;
                    allSucceeded = false;
                }
            }

            // 正面不完整时不得施加负面：直接移除负面并返回失败。
            if (!allPositiveReady)
            {
                RemoveHediff(
                    overseer,
                    DataProcessingAllocationUtility
                        .DataStreamDistributionDef);

                return false;
            }

            // 只有所有最终正面都成功后，才执行一次安全负面同步（无论本轮是否有加/减/切换）。
            // 可恢复读档被移除/丢失、Severity 过时或档数未变化的负面不一致。
            SyncHediffsForOverseer(overseer);

            // 最后对最终档数为 0 的目标执行安全正面同步（清理自我等零档正面）；
            // 此时负面已回到正确值，不会在旧高值时误删自我正面。
            foreach (PlanEntry entry in entries)
            {
                if (finalSteps.TryGetValue(entry, out int v) && v <= 0)
                {
                    SyncHediffForTarget(entry.target);
                }
            }

            return allSucceeded;
        }

        /// <summary>
        /// 仅修改实际档数（增删/调整记录），不触发任何 Hediff 同步。
        /// 供调度器先批量评估、最后统一 Sync 的场景使用，避免每档一次同步循环。
        /// </summary>
        private void SetActualStepsWithoutSync(Pawn overseer, Pawn? target, int steps)
        {
            if (target == null)
            {
                return;
            }

            steps = Mathf.Max(0, steps);
            DataProcessingAllocationRecord? record = FindRecord(overseer, target);
            if (steps <= 0)
            {
                if (record != null)
                {
                    RemoveRecord(record);
                }
            }
            else if (record == null)
            {
                AddRecord(new DataProcessingAllocationRecord(overseer, target, steps));
            }
            else
            {
                record.steps = steps;
            }
        }

        private sealed class PlanEntry
        {
            public Pawn target = null!;
            public DataProcessingDynamicTargetRecord config = null!;
            public DataProcessingSpecialization desiredSpecialization;
            public int requestedSteps;
            public int currentSteps;
            public DataProcessingSpecialization currentSpecialization;
        }

        public bool IsPinned(Pawn? overseer, Pawn? target)
        {
            if (ReferenceEquals(overseer, target))
            {
                return false;
            }

            return FindPinRecord(overseer, target) != null;
        }

        public int GetPinOrder(Pawn? overseer, Pawn? target)
        {
            DataProcessingAllocationPinRecord? pin = FindPinRecord(overseer, target);
            return pin != null ? pin.pinOrder : int.MaxValue;
        }

        public bool TryPinTarget(Pawn? overseer, Pawn? target)
        {
            if (!ResearchFeatureUnlockUtility.IsDataProcessingAllocationUnlocked())
            {
                return false;
            }

            if (ReferenceEquals(overseer, target))
            {
                return false;
            }

            if (!DataProcessingAllocationUtility.IsValidAllocationPair(overseer, target))
            {
                return false;
            }

            if (FindPinRecord(overseer, target) != null)
            {
                return false;
            }

            EnsureNextPinOrderValid();
            pinRecords.Add(new DataProcessingAllocationPinRecord(
                overseer!,
                target!,
                nextPinOrder++));
            return true;
        }

        public bool TryUnpinTarget(Pawn? overseer, Pawn? target)
        {
            DataProcessingAllocationPinRecord? pin = FindPinRecord(overseer, target);
            if (pin == null)
            {
                return false;
            }

            pinRecords.Remove(pin);
            return true;
        }

        public void CleanupInvalidPinRecords()
        {
            pinRecords ??= new List<DataProcessingAllocationPinRecord>();

            HashSet<(int overseerId, int targetId)> seenPairs = new HashSet<(int, int)>();
            for (int i = pinRecords.Count - 1; i >= 0; i--)
            {
                DataProcessingAllocationPinRecord? pin = pinRecords[i];
                if (pin == null
                    || ReferenceEquals(pin.overseer, pin.target)
                    || !DataProcessingAllocationUtility.IsValidAllocationPair(
                        pin.overseer,
                        pin.target))
                {
                    pinRecords.RemoveAt(i);
                    continue;
                }

                int overseerId = pin.overseer!.thingIDNumber;
                int targetId = pin.target!.thingIDNumber;
                if (!seenPairs.Add((overseerId, targetId)))
                {
                    pinRecords.RemoveAt(i);
                }
            }

            EnsureNextPinOrderValid();
        }

        public void CleanupInvalidRecords(
            bool synchronizeHediffs = true)
        {
            HashSet<Pawn> affectedTargets = new HashSet<Pawn>();
            HashSet<Pawn> affectedOverseers = new HashSet<Pawn>();

            for (int i = records.Count - 1; i >= 0; i--)
            {
                DataProcessingAllocationRecord? record = records[i];
                if (record == null
                    || record.steps <= 0
                    || !IsRecordValid(record))
                {
                    if (record?.target != null)
                    {
                        affectedTargets.Add(record.target);
                    }

                    if (record?.overseer != null)
                    {
                        affectedOverseers.Add(record.overseer);
                    }

                    if (record != null)
                    {
                        RemoveRecord(record);
                    }
                }
            }

            if (synchronizeHediffs)
            {
                // 批处理：先同步所有受影响监管者负面，再同步目标正面。
                foreach (Pawn overseer in affectedOverseers)
                {
                    if (overseer != null && !overseer.Destroyed)
                    {
                        SyncHediffsForOverseer(overseer);
                    }
                }

                foreach (Pawn target in affectedTargets)
                {
                    if (target == null || target.Destroyed)
                    {
                        continue;
                    }

                    // 仍存在合法记录时重新同步正面，不要仅仅“什么也不做”；
                    // 仅当目标不再有任何分配记录时才清理其正面指令聚焦，
                    // 避免误删仍被其他监管者/关系保有的目标状态（例如自身同时是监管者的情形）。
                    if (HasAnyAllocationRecordForTarget(target))
                    {
                        SyncHediffForTarget(target);
                    }
                    else
                    {
                        RemoveAllCommandFocusHediffs(target);
                    }
                }
            }

            CleanupInvalidPinRecords();
            CleanupInvalidSpecializationRecords();
            CleanupInvalidDynamicAllocationRecords();
            CleanupInvalidDynamicTargetRecords();
        }

        public void SyncHediffsForOverseer(Pawn? overseer)
        {
            if (overseer == null || overseer.Destroyed || overseer.health?.hediffSet == null)
            {
                return;
            }

            HediffDef? def = DataProcessingAllocationUtility.DataStreamDistributionDef;
            if (def == null)
            {
                return;
            }

            int totalSteps = GetTotalStepsForOverseer(overseer);
            if (totalSteps <= 0)
            {
                RemoveHediff(overseer, def);
                return;
            }

            Hediff hediff = overseer.health.GetOrAddHediff(def);
            hediff.Severity = totalSteps * DataProcessingAllocationUtility.StepPercent;
        }

        public void SyncHediffForTarget(Pawn? target)
        {
            if (target == null || target.Destroyed || target.health?.hediffSet == null)
            {
                return;
            }

            int steps = GetStepsForTarget(target);
            if (steps <= 0)
            {
                // 归零：移除所有指令聚焦类健康状态，但保留特化配置记录。
                RemoveAllCommandFocusHediffs(target);
                return;
            }

            // 已存在任意指令聚焦类 Hediff：仅刷新数值，不替换 HediffDef。
            Hediff? existing = FindAnyCommandFocusHediff(target);
            if (existing != null)
            {
                existing.Severity = steps * DataProcessingAllocationUtility.StepPercent;
                return;
            }

            // 首次创建：依据保存的特化配置生成正确的 HediffDef。
            DataProcessingSpecialization specialization = GetSpecializationForTarget(target);
            HediffDef? def = DataProcessingAllocationUtility.GetCommandFocusDef(specialization);
            if (def == null)
            {
                return;
            }

            Hediff hediff = target.health.GetOrAddHediff(def);
            hediff.Severity = steps * DataProcessingAllocationUtility.StepPercent;
        }

        public void SyncAllHediffs()
        {
            CleanupInvalidRecords();

            HashSet<Pawn> syncedOverseers = new HashSet<Pawn>();
            for (int i = 0; i < records.Count; i++)
            {
                DataProcessingAllocationRecord record = records[i];
                Pawn? target = record.target;
                if (target != null && !target.Destroyed)
                {
                    SyncHediffForTarget(target);
                }

                Pawn? overseer = record.overseer;
                if (overseer != null && !overseer.Destroyed && syncedOverseers.Add(overseer))
                {
                    SyncHediffsForOverseer(overseer);
                }
            }
        }

        /// <summary>
        /// 数据处理分配科研关闭时清空全部记录、顶置与相关健康状态及特化配置。
        /// </summary>
        public bool ClearAllAllocationsAndEffects()
        {
            HashSet<Pawn> overseers = new HashSet<Pawn>();
            HashSet<Pawn> targets = new HashSet<Pawn>();

            for (int i = 0; i < records.Count; i++)
            {
                DataProcessingAllocationRecord? record = records[i];
                if (record == null)
                {
                    continue;
                }

                if (record.overseer != null)
                {
                    overseers.Add(record.overseer);
                }

                if (record.target != null)
                {
                    targets.Add(record.target);
                }
            }

            for (int i = 0; i < pinRecords.Count; i++)
            {
                DataProcessingAllocationPinRecord? pin = pinRecords[i];
                if (pin?.overseer != null)
                {
                    overseers.Add(pin.overseer);
                }

                if (pin?.target != null)
                {
                    targets.Add(pin.target);
                }
            }

            for (int i = 0; i < specializationRecords.Count; i++)
            {
                DataProcessingSpecializationRecord? spec = specializationRecords[i];
                if (spec?.overseer != null)
                {
                    overseers.Add(spec.overseer);
                }

                if (spec?.target != null)
                {
                    targets.Add(spec.target);
                }
            }

            records.Clear();
            recordsByOverseer.Clear();
            recordByTarget.Clear();
            pinRecords.Clear();
            nextPinOrder = 0;
            specializationRecords.Clear();
            specializationRecordByTarget.Clear();
            dynamicAllocationRecords.Clear();
            dynamicAllocationRecordByOverseer.Clear();
            dynamicTargetRecords.Clear();
            dynamicTargetRecordByTarget.Clear();
            nextDynamicCheckTickByTarget.Clear();
            cachedDynamicEvaluationByTarget.Clear();
            pendingPostLoadDynamicReconciliation = false;

            bool allSucceeded = true;

            foreach (Pawn overseer in overseers)
            {
                try
                {
                    if (overseer != null && !overseer.Destroyed)
                    {
                        RemoveHediff(
                            overseer,
                            DataProcessingAllocationUtility.DataStreamDistributionDef);
                        RemoveAllCommandFocusHediffs(overseer);
                    }
                }
                catch (Exception ex)
                {
                    allSucceeded = false;
                    Log.Error(
                        "[MAP-机械族机械师] 清空数据处理分配时清理 overseer 失败：" +
                        $"pawn={overseer?.LabelShort ?? "null"}" +
                        $"（{overseer?.ThingID ?? "null"}）：{ex}");
                }
            }

            foreach (Pawn target in targets)
            {
                try
                {
                    if (target != null && !target.Destroyed)
                    {
                        RemoveAllCommandFocusHediffs(target);
                        RemoveHediff(
                            target,
                            DataProcessingAllocationUtility.DataStreamDistributionDef);
                    }
                }
                catch (Exception ex)
                {
                    allSucceeded = false;
                    Log.Error(
                        "[MAP-机械族机械师] 清空数据处理分配时清理 target 失败：" +
                        $"pawn={target?.LabelShort ?? "null"}" +
                        $"（{target?.ThingID ?? "null"}）：{ex}");
                }
            }

            bool residualScrubSucceeded = ScrubResidualDataProcessingHediffs();
            return allSucceeded && residualScrubSucceeded;
        }

        /// <summary>
        /// 自我指令聚焦科研关闭时，仅清除 overseer==target 的自身分配与特化配置。
        /// </summary>
        public bool ClearSelfAllocationsAndEffects()
        {
            HashSet<Pawn> affected = new HashSet<Pawn>();
            List<DataProcessingAllocationRecord> toRemove =
                new List<DataProcessingAllocationRecord>();

            for (int i = 0; i < records.Count; i++)
            {
                DataProcessingAllocationRecord? record = records[i];
                if (record == null
                    || !DataProcessingAllocationUtility.IsSelfAllocationPair(
                        record.overseer,
                        record.target))
                {
                    continue;
                }

                toRemove.Add(record);
                if (record.overseer != null)
                {
                    affected.Add(record.overseer);
                }
            }

            bool allSucceeded = true;

            // 先批量移除自我记录（仅清记录），再统一同步监管者负面、最后清目标正面。
            for (int i = 0; i < toRemove.Count; i++)
            {
                DataProcessingAllocationRecord record = toRemove[i];
                try
                {
                    RemoveRecord(record);
                }
                catch (Exception ex)
                {
                    allSucceeded = false;
                    Log.Error(
                        "[MAP-机械族机械师] 清除自我指令聚焦分配失败：" +
                        $"pawn={record.target?.LabelShort ?? "null"}" +
                        $"（{record.target?.ThingID ?? "null"}）：{ex}");
                }
            }

            // 无论当前是否存在正数自我分配，都清除 0% 与正数的自我特化配置。
            RemoveSelfSpecializationRecords();

            CollectOverseersNeedingDataStreamResync(affected);
            foreach (Pawn pawn in affected)
            {
                try
                {
                    if (pawn != null && !pawn.Destroyed)
                    {
                        // 先降低监管者负面数据流分发。
                        SyncHediffsForOverseer(pawn);
                    }
                }
                catch (Exception ex)
                {
                    allSucceeded = false;
                    Log.Error(
                        "[MAP-机械族机械师] 自我指令聚焦清理后重算数据流分发失败：" +
                        $"pawn={pawn?.LabelShort ?? "null"}" +
                        $"（{pawn?.ThingID ?? "null"}）：{ex}");
                }
            }

            // 所有监管者负面已同步后，再清理各目标正面指令聚焦（半额返还先到位）。
            for (int i = 0; i < toRemove.Count; i++)
            {
                DataProcessingAllocationRecord record = toRemove[i];
                if (record.target != null && !record.target.Destroyed)
                {
                    try
                    {
                        RemoveAllCommandFocusHediffs(record.target);
                        RemoveSpecializationRecordsForTarget(record.target);
                    }
                    catch (Exception ex)
                    {
                        allSucceeded = false;
                        Log.Error(
                            "[MAP-机械族机械师] 清除自我指令聚焦健康状态失败：" +
                            $"pawn={record.target.LabelShort}（{record.target.ThingID}）：{ex}");
                    }
                }
            }

            bool residualScrubSucceeded = ScrubResidualOrphanCommandFocusHediffs();
            return allSucceeded && residualScrubSucceeded;
        }

        /// <summary>
        /// 把仍持有下属记录或残留 DataStreamDistribution 的监督者并入重算集合，
        /// 保证前一轮记录已删但重算失败时，下次重试仍能校正数值。
        /// </summary>
        private void CollectOverseersNeedingDataStreamResync(HashSet<Pawn> overseers)
        {
            foreach (Pawn overseer in recordsByOverseer.Keys)
            {
                if (overseer != null)
                {
                    overseers.Add(overseer);
                }
            }

            if (Current.Game == null || Find.World == null)
            {
                return;
            }

            HediffDef? streamDef = DataProcessingAllocationUtility.DataStreamDistributionDef;
            if (streamDef == null)
            {
                return;
            }

            List<Pawn> playerFactionPawns =
                PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_OfPlayerFaction;
            for (int i = 0; i < playerFactionPawns.Count; i++)
            {
                Pawn pawn = playerFactionPawns[i];
                if (pawn == null
                    || pawn.Destroyed
                    || pawn.health?.hediffSet == null
                    || !pawn.health.hediffSet.HasHediff(streamDef))
                {
                    continue;
                }

                overseers.Add(pawn);
            }
        }

        private bool ScrubResidualDataProcessingHediffs()
        {
            if (Current.Game == null || Find.World == null)
            {
                return true;
            }

            HediffDef? streamDef = DataProcessingAllocationUtility.DataStreamDistributionDef;
            if (streamDef == null)
            {
                return true;
            }

            bool allSucceeded = true;
            List<Pawn> playerFactionPawns =
                PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_OfPlayerFaction;
            for (int i = 0; i < playerFactionPawns.Count; i++)
            {
                Pawn pawn = playerFactionPawns[i];
                if (pawn == null || pawn.Destroyed || pawn.health?.hediffSet == null)
                {
                    continue;
                }

                try
                {
                    RemoveHediff(pawn, streamDef);
                    RemoveAllCommandFocusHediffs(pawn);
                }
                catch (Exception ex)
                {
                    allSucceeded = false;
                    Log.Error(
                        "[MAP-机械族机械师] 清理残留数据处理分配健康状态失败：" +
                        $"pawn={pawn.LabelShort}（{pawn.ThingID}）：{ex}");
                }
            }

            return allSucceeded;
        }

        /// <summary>
        /// 清理“已无任何分配记录却仍残留指令聚焦类健康状态”的状态，不误删合法下属。
        /// </summary>
        private bool ScrubResidualOrphanCommandFocusHediffs()
        {
            if (Current.Game == null || Find.World == null)
            {
                return true;
            }

            bool allSucceeded = true;
            List<Pawn> playerFactionPawns =
                PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_OfPlayerFaction;
            for (int i = 0; i < playerFactionPawns.Count; i++)
            {
                Pawn pawn = playerFactionPawns[i];
                if (pawn == null
                    || pawn.Destroyed
                    || pawn.health?.hediffSet == null
                    || !HasAnyCommandFocusHediff(pawn))
                {
                    continue;
                }

                if (HasAnyAllocationRecordForTarget(pawn))
                {
                    continue;
                }

                try
                {
                    RemoveAllCommandFocusHediffs(pawn);
                }
                catch (Exception ex)
                {
                    allSucceeded = false;
                    Log.Error(
                        "[MAP-机械族机械师] 清理残留指令聚焦健康状态失败：" +
                        $"pawn={pawn.LabelShort}（{pawn.ThingID}）：{ex}");
                }
            }

            return allSucceeded;
        }

        private bool HasAnyAllocationRecordForTarget(Pawn pawn)
        {
            return recordByTarget.ContainsKey(pawn);
        }

        private static bool HasAnyCommandFocusHediff(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null)
            {
                return false;
            }

            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                if (hediffs[i] != null
                    && DataProcessingAllocationUtility.IsAnyCommandFocusDef(hediffs[i].def))
                {
                    return true;
                }
            }

            return false;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref records, "records", LookMode.Deep);
            Scribe_Collections.Look(ref pinRecords, "pinRecords", LookMode.Deep);
            Scribe_Values.Look(ref nextPinOrder, "nextPinOrder", 0);
            Scribe_Collections.Look(
                ref specializationRecords,
                "specializationRecords",
                LookMode.Deep);
            Scribe_Collections.Look(
                ref dynamicAllocationRecords,
                "dynamicAllocationRecords",
                LookMode.Deep);
            Scribe_Collections.Look(
                ref dynamicTargetRecords,
                "dynamicTargetRecords",
                LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                records ??= new List<DataProcessingAllocationRecord>();
                pinRecords ??= new List<DataProcessingAllocationPinRecord>();
                specializationRecords ??= new List<DataProcessingSpecializationRecord>();
                dynamicAllocationRecords ??= new List<DataProcessingDynamicAllocationRecord>();
                dynamicTargetRecords ??= new List<DataProcessingDynamicTargetRecord>();
                records.RemoveAll(record => record == null || record.steps <= 0);
                specializationRecords.RemoveAll(record => record == null);
                dynamicAllocationRecords.RemoveAll(record => record == null);
                dynamicTargetRecords.RemoveAll(record => record == null);
                RebuildCaches();
                CleanupInvalidRecords(
                    synchronizeHediffs: false);
                CleanupInvalidSpecializationRecords();
                CleanupInvalidDynamicAllocationRecords();
                CleanupInvalidDynamicTargetRecords();
                RebuildCaches();
            }
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();

            // 轻量调度：每 60 tick 检查到期单体目标。
            dynamicSchedulerTickCounter++;
            if (dynamicSchedulerTickCounter >= 60)
            {
                dynamicSchedulerTickCounter = 0;
                TickDynamicScheduler();
            }

            // 读档后统一动态校正（下一实际 tick 执行，不立即静态分类覆盖玩家选择）。
            if (pendingPostLoadDynamicReconciliation
                && Find.TickManager.TicksGame
                    >= postLoadReconciliationEarliestTick)
            {
                if (TryRunPostLoadDynamicReconciliation())
                {
                    pendingPostLoadDynamicReconciliation = false;
                }
                else
                {
                    // 失败：每 60 tick 重试一次，避免每 tick 反复执行完整恢复与计划。
                    postLoadReconciliationEarliestTick =
                        Find.TickManager.TicksGame + 60;
                }
            }

            // 全局开关切换（开启/关闭）失败后的真实重试：每 60 tick 触发一次，
            // 在意识保护早返回之前处理，确保切换失败不静默卡死。
            if (pendingGlobalTransitionOverseers.Count > 0
                && Find.TickManager.TicksGame >= globalTransitionRetryEarliestTick)
            {
                globalTransitionRetryEarliestTick = 0;
                HashSet<Pawn> retryOverseers =
                    new HashSet<Pawn>(pendingGlobalTransitionOverseers,
                        new ReferencePawnEqualityComparer());
                pendingGlobalTransitionOverseers.Clear();

                foreach (Pawn overseer in retryOverseers)
                {
                    if (overseer == null || overseer.Destroyed)
                    {
                        continue;
                    }

                    bool ok;
                    if (IsDynamicAllocationEnabled(overseer))
                    {
                        // 开启后重试：立即重新评估所有目标。
                        ForceEvaluateAllDynamicTargets(overseer);
                        ok = true;
                    }
                    else
                    {
                        // 关闭后重试：重新恢复默认常态额度与默认模式。
                        ok = RestoreDefaultsForOverseer(overseer);
                    }

                    if (!ok)
                    {
                        QueueGlobalTransitionRetry(overseer);
                    }
                }
            }

            protectionTickCounter++;
            if (protectionTickCounter < 600)
            {
                return;
            }

            protectionTickCounter = 0;
            // 动态分配不再每 600 tick 全量重跑，改由轻量调度器按各自检查间隔到期触发，
            // 避免整图重复评估。意识保护周期仍保留。
            RunConsciousnessProtection();

            // 每 600 tick 真实重新扫描动态分配成员关系，纳入新生产/新发现的机械族。
            RefreshDynamicTargetMembership();
        }

        /// <summary>
        /// 重新扫描所有启用动态分配监管者的目标成员关系：为新增机械族目标接入动态单体配置，
        /// 但若目标已有旧监管者配置则迁移（重指 overseer）而非重建，避免原设置丢失。
        /// 不扫描全局关闭监管者的新增目标，但其已有配置不得被删除。最后清理失效配置并
        /// 对受影响监管者重跑动态计划。
        /// </summary>
        private void RefreshDynamicTargetMembership()
        {
            if (dynamicAllocationRecords == null || dynamicTargetRecords == null)
            {
                return;
            }

            HashSet<Pawn> affectedOverseers = new HashSet<Pawn>();

            for (int i = 0; i < dynamicAllocationRecords.Count; i++)
            {
                DataProcessingDynamicAllocationRecord? global = dynamicAllocationRecords[i];
                Pawn? overseer = global?.overseer;

                if (global?.enabled != true
                    || overseer == null
                    || overseer.Destroyed)
                {
                    continue;
                }

                List<Pawn> targets = new List<Pawn>();
                CollectDynamicAllocationTargets(overseer, targets);

                for (int j = 0; j < targets.Count; j++)
                {
                    Pawn target = targets[j];

                    DataProcessingDynamicTargetRecord? existing =
                        FindDynamicTargetRecord(overseer: null, target);

                    if (existing == null)
                    {
                        existing = GetOrCreateDynamicTargetRecord(overseer, target);
                    }
                    else if (!ReferenceEquals(existing.overseer, overseer))
                    {
                        Pawn? oldOverseer = existing.overseer;

                        existing.overseer = overseer;
                        existing.Normalize();

                        cachedDynamicEvaluationByTarget.Remove(target);
                        nextDynamicCheckTickByTarget.Remove(target);

                        if (oldOverseer != null)
                        {
                            affectedOverseers.Add(oldOverseer);
                        }
                    }

                    // 仅对“配置为新接入 / 监管者变更 / 评估缓存缺失”的目标立即重算评估，
                    // 未变化的目标保留原评估缓存与独立检查间隔，不打断各自错峰调度。
                    bool needsEvaluate =
                        !ReferenceEquals(existing.overseer, overseer)
                        || !cachedDynamicEvaluationByTarget.ContainsKey(target);
                    if (needsEvaluate)
                    {
                        cachedDynamicEvaluationByTarget[target] =
                            EvaluateTarget(existing, target);
                        nextDynamicCheckTickByTarget[target] =
                            Find.TickManager.TicksGame
                            + Mathf.Max(60, existing.checkIntervalTicks);
                    }

                    affectedOverseers.Add(overseer);
                }
            }

            // 清理失效配置（不依据全局开关是否开启；全局关闭的配置被保留）。
            // 传入受影响监管者，使其在删除/迁移后仍同步保留有效目标的运行时缓存。
            CleanupInvalidDynamicTargetRecords(affectedOverseers);

            foreach (Pawn overseer in affectedOverseers)
            {
                if (IsDynamicAllocationEnabled(overseer))
                {
                    RunDynamicPlanForOverseer(overseer);
                }
            }
        }

        private bool TryRunPostLoadDynamicReconciliation()
        {
            // 每次重试都先确保正面存在，同时保持所有负面暂时移除。
            if (!RestoreCommandFocusHediffsAfterLoad())
            {
                return false;
            }

            HashSet<Pawn> overseers = CollectPostLoadReconciliationOverseers();

            bool allSucceeded = true;

            foreach (Pawn overseer in overseers)
            {
                if (overseer == null || overseer.Destroyed)
                {
                    continue;
                }

                // 意识读取失败则本轮跳过该监管者，留待下次重试。
                if (!DataProcessingAllocationUtility.TryGetCurrentConsciousness(
                        overseer,
                        out _))
                {
                    allSucceeded = false;
                    continue;
                }

                try
                {
                    bool succeeded;

                    if (IsDynamicAllocationEnabled(overseer))
                    {
                        ForceEvaluateAllDynamicTargets(overseer);

                        succeeded = RunDynamicPlanForOverseer(overseer);
                    }
                    else
                    {
                        succeeded = TryReconcileNonDynamicOverseerAfterLoad(overseer);
                    }

                    if (!succeeded)
                    {
                        allSucceeded = false;
                    }
                }
                catch (Exception ex)
                {
                    allSucceeded = false;
                    Log.ErrorOnce(
                        "[MAP-机械族机械师] 读档后数据处理校正失败：" +
                        $"overseer={overseer.LabelShort}" +
                        $"（{overseer.ThingID}）：{ex}",
                        unchecked(
                            DynamicAllocationLogKeyBase
                            + overseer.thingIDNumber));
                }
            }

            return allSucceeded;
        }

        /// <summary>
        /// 收集所有需要读档后校正的监管者：包含有分配记录的监管者、有动态全局记录的监管者，
        /// 以及仍残留 DataStreamDistribution 的玩家机械族机械师（可能已无记录）。
        /// </summary>
        private HashSet<Pawn> CollectPostLoadReconciliationOverseers()
        {
            HashSet<Pawn> result =
                new HashSet<Pawn>(new ReferencePawnEqualityComparer());

            for (int i = 0; i < records.Count; i++)
            {
                Pawn? overseer = records[i]?.overseer;
                if (overseer != null && !overseer.Destroyed)
                {
                    result.Add(overseer);
                }
            }

            for (int i = 0; i < dynamicAllocationRecords.Count; i++)
            {
                Pawn? overseer = dynamicAllocationRecords[i]?.overseer;
                if (overseer != null && !overseer.Destroyed)
                {
                    result.Add(overseer);
                }
            }

            // 把仍残留 DataStreamDistribution 但可能已经没有记录的监管者也纳入。
            CollectOverseersNeedingDataStreamResync(result);

            return result;
        }

        /// <summary>
        /// 处理未开启动态分配（纯手动或全局动态关闭）的监管者：
        /// 以保存的单体动态配置（normalSteps / defaultSpecialization / priority）或当前实际档数与特化
        /// 作为固定请求，在 50% 绝对安全线内安全恢复正面、负面、实际额度与默认模式。
        /// 临时配置不写入存档，也不调用 GetOrCreateDynamicTargetRecord（除非本就存在配置）。
        /// </summary>
        private bool TryReconcileNonDynamicOverseerAfterLoad(Pawn overseer)
        {
            if (overseer == null || overseer.Destroyed)
            {
                return false;
            }

            HashSet<Pawn> targets =
                new HashSet<Pawn>(new ReferencePawnEqualityComparer());

            if (recordsByOverseer.TryGetValue(
                    overseer,
                    out List<DataProcessingAllocationRecord>? overseerRecords))
            {
                for (int i = 0; i < overseerRecords.Count; i++)
                {
                    Pawn? target = overseerRecords[i]?.target;
                    if (target != null && !target.Destroyed)
                    {
                        targets.Add(target);
                    }
                }
            }

            for (int i = 0; i < dynamicTargetRecords.Count; i++)
            {
                DataProcessingDynamicTargetRecord? config = dynamicTargetRecords[i];
                if (config?.target != null
                    && ReferenceEquals(config.overseer, overseer)
                    && !config.target.Destroyed
                    && DataProcessingAllocationUtility.IsValidAllocationPair(
                        overseer,
                        config.target))
                {
                    targets.Add(config.target);
                }
            }

            if (targets.Count == 0)
            {
                RemoveHediff(
                    overseer,
                    DataProcessingAllocationUtility.DataStreamDistributionDef);
                return true;
            }

            List<PlanEntry> entries = new List<PlanEntry>();

            foreach (Pawn target in targets)
            {
                int currentSteps = GetStepsForOverseerTarget(overseer, target);
                DataProcessingSpecialization currentSpecialization =
                    GetSpecializationForOverseerTarget(overseer, target);

                DataProcessingDynamicTargetRecord? savedConfig =
                    FindDynamicTargetRecord(overseer, target);

                DataProcessingDynamicTargetRecord planConfig;

                if (savedConfig != null)
                {
                    planConfig = savedConfig;
                }
                else
                {
                    // 仅用于这次计划的临时配置，不加入 dynamicTargetRecords，不持久化。
                    planConfig = new DataProcessingDynamicTargetRecord(
                        overseer,
                        target,
                        currentSteps,
                        currentSpecialization);
                    planConfig.enabled = false;
                    planConfig.priority = 3;
                    planConfig.Normalize();
                }

                entries.Add(new PlanEntry
                {
                    target = target,
                    config = planConfig,
                    desiredSpecialization =
                        savedConfig != null
                            ? savedConfig.defaultSpecialization
                            : currentSpecialization,
                    requestedSteps =
                        savedConfig != null
                            ? savedConfig.normalSteps
                            : currentSteps,
                    currentSteps = currentSteps,
                    currentSpecialization = currentSpecialization
                });
            }

            return ApplyDynamicPlan(
                overseer,
                entries,
                treatAllEntriesAsFixed: true);
        }

        public override void LoadedGame()
        {
            base.LoadedGame();

            // 仅清理失效记录，不触碰 Hediff（同步交由 RestoreCommandFocusHediffsAfterLoad）。
            CleanupInvalidRecords(synchronizeHediffs: false);
            CleanupInvalidSpecializationRecords();
            CleanupInvalidDynamicAllocationRecords();
            CleanupInvalidDynamicTargetRecords();
            RebuildCaches();

            // 读档阶段统一暂时移除所有相关负面（安全），再恢复正面指令聚焦。
            // 失败时延迟更久重试，避免读档瞬间因负值返还缺失而致死。
            bool initialRestoreSucceeded = RestoreCommandFocusHediffsAfterLoad();

            // 读档后清空运行时评估缓存，强制下一周期按各自检查间隔重新评估真实状态。
            cachedDynamicEvaluationByTarget.Clear();
            nextDynamicCheckTickByTarget.Clear();

            // 设置读档后统一动态校正标记：下一实际 tick 再批量校正，
            // 避免立即旧版静态分类覆盖玩家选择。
            pendingPostLoadDynamicReconciliation = true;
            postLoadReconciliationEarliestTick =
                Find.TickManager.TicksGame + (initialRestoreSucceeded ? 1 : 60);
        }

        /// <summary>
        /// 仅在载入存档流程中调用：安全恢复指令聚焦。
        /// 读档阶段统一暂时移除所有相关监管者的负面数据流分发（移除只会提高意识，绝对安全），
        /// 再恢复各目标的正面指令聚焦。本方法绝对不得重新添加或提高负面，
        /// 完整负面由下一实际 tick 的安全计划恢复。返回是否所有正面都成功恢复。
        /// </summary>
        private bool RestoreCommandFocusHediffsAfterLoad()
        {
            if (records == null)
            {
                return false;
            }

            // 收集所有有效分配记录中的监管者，读档阶段统一暂时移除其负面。
            HashSet<Pawn> overseers =
                new HashSet<Pawn>(new ReferencePawnEqualityComparer());
            for (int i = 0; i < records.Count; i++)
            {
                Pawn? overseer = records[i]?.overseer;
                if (overseer != null && !overseer.Destroyed)
                {
                    overseers.Add(overseer);
                }
            }

            // 移除负面只会提高意识，不会导致读档死亡；下一 tick 再按安全预算恢复。
            foreach (Pawn overseer in overseers)
            {
                RemoveHediff(
                    overseer,
                    DataProcessingAllocationUtility
                        .DataStreamDistributionDef);
            }

            bool allSucceeded = true;

            for (int i = 0; i < records.Count; i++)
            {
                DataProcessingAllocationRecord? record = records[i];

                if (record == null
                    || record.steps <= 0
                    || record.overseer == null
                    || record.target == null
                    || record.target.Destroyed)
                {
                    continue;
                }

                Pawn overseer = record.overseer;
                Pawn target = record.target;

                bool restored =
                    TryApplyCommandFocusHediffForTarget(
                        target,
                        GetSpecializationForTarget(target),
                        record.steps,
                        cleanupOtherCommandFocusHediffs: true);

                if (!restored
                    && !HasAnyCommandFocusHediff(target))
                {
                    allSucceeded = false;

                    Log.ErrorOnce(
                        "[MAP-机械族机械师] 读档恢复目标指令聚焦失败：" +
                        $"overseer={overseer.LabelShort}" +
                        $"（{overseer.ThingID}），" +
                        $"target={target.LabelShort}" +
                        $"（{target.ThingID}）。",
                        BuildDynamicAllocationLogKey(overseer, target));
                }
            }

            ScrubResidualOrphanCommandFocusHediffs();

            // 此方法绝对不得重新添加负面。
            return allSucceeded;
        }

        private static bool IsRecordValid(DataProcessingAllocationRecord record)
        {
            return DataProcessingAllocationUtility.IsValidAllocationPair(
                record.overseer,
                record.target);
        }

        private DataProcessingAllocationPinRecord? FindPinRecord(Pawn? overseer, Pawn? target)
        {
            if (overseer == null || target == null || pinRecords == null)
            {
                return null;
            }

            for (int i = 0; i < pinRecords.Count; i++)
            {
                DataProcessingAllocationPinRecord pin = pinRecords[i];
                if (pin != null
                    && ReferenceEquals(pin.overseer, overseer)
                    && ReferenceEquals(pin.target, target))
                {
                    return pin;
                }
            }

            return null;
        }

        private void EnsureNextPinOrderValid()
        {
            pinRecords ??= new List<DataProcessingAllocationPinRecord>();
            int maxOrder = -1;
            for (int i = 0; i < pinRecords.Count; i++)
            {
                DataProcessingAllocationPinRecord? pin = pinRecords[i];
                if (pin != null && pin.pinOrder > maxOrder)
                {
                    maxOrder = pin.pinOrder;
                }
            }

            if (nextPinOrder <= maxOrder)
            {
                nextPinOrder = maxOrder + 1;
            }
        }

        private Pawn? AddRecord(DataProcessingAllocationRecord record)
        {
            if (record.overseer == null || record.target == null)
            {
                return null;
            }

            Pawn? replacedOverseer = null;
            if (recordByTarget.TryGetValue(
                    record.target, out DataProcessingAllocationRecord? existing)
                && existing != null)
            {
                replacedOverseer = existing.overseer;

                bool overseerChanged =
                    replacedOverseer != null
                    && record.overseer != null
                    && !ReferenceEquals(replacedOverseer, record.overseer);

                RemoveRecord(existing);

                if (overseerChanged)
                {
                    // 仅删除旧监管者与目标之间的特化配置，保留新监管者已保存的预选。
                    RemoveSpecializationRecordsForOverseerTarget(
                        replacedOverseer!,
                        record.target);

                    // 监管者替换：迁移该目标的动态单体配置到新监管者，保留玩家已设的常态额度、
                    // 最高额度、优先级、检查间隔与规则开关，仅重新指向新监管者。
                    MigrateDynamicTargetConfigToNewOverseer(
                        replacedOverseer!,
                        record.overseer!,
                        record.target);

                    // 监管者替换：先同步旧监管者负面数据流分发，保留目标正面，
                    // 待新关系建立后再按新特化首次创建。避免先删正面导致自我分配致死。
                    SyncHediffsForOverseer(replacedOverseer);
                    // 不在此删除目标指令聚焦；后续 SyncHediffForTarget 会按新特化安全建立。
                }
            }

            records.Add(record);
            AddToCaches(record);
            return replacedOverseer;
        }

        private void SyncReplacedOverseerIfNeeded(Pawn? oldOverseer, Pawn newOverseer)
        {
            if (oldOverseer == null
                || oldOverseer.Destroyed
                || ReferenceEquals(oldOverseer, newOverseer))
            {
                return;
            }

            SyncHediffsForOverseer(oldOverseer);
        }

        private void RemoveRecord(DataProcessingAllocationRecord record)
        {
            records.Remove(record);
            RemoveFromCaches(record);
        }

        private void AddToCaches(DataProcessingAllocationRecord record)
        {
            Pawn overseer = record.overseer!;
            Pawn target = record.target!;

            if (!recordsByOverseer.TryGetValue(overseer, out List<DataProcessingAllocationRecord>? overseerRecords))
            {
                overseerRecords = new List<DataProcessingAllocationRecord>();
                recordsByOverseer[overseer] = overseerRecords;
            }

            if (!overseerRecords.Contains(record))
            {
                overseerRecords.Add(record);
            }

            recordByTarget[target] = record;
        }

        private void RemoveFromCaches(DataProcessingAllocationRecord record)
        {
            RemoveFromOverseerCache(record);

            if (record.target != null
                && recordByTarget.TryGetValue(record.target, out DataProcessingAllocationRecord? indexed)
                && ReferenceEquals(indexed, record))
            {
                recordByTarget.Remove(record.target);
            }
        }

        private void RemoveFromOverseerCache(DataProcessingAllocationRecord record)
        {
            if (record.overseer == null)
            {
                return;
            }

            if (recordsByOverseer.TryGetValue(record.overseer, out List<DataProcessingAllocationRecord>? overseerRecords))
            {
                overseerRecords.Remove(record);
                if (overseerRecords.Count == 0)
                {
                    recordsByOverseer.Remove(record.overseer);
                }
            }
        }

        private void RebuildCaches()
        {
            recordsByOverseer = new Dictionary<Pawn, List<DataProcessingAllocationRecord>>();
            recordByTarget = new Dictionary<Pawn, DataProcessingAllocationRecord>();

            for (int i = 0; i < records.Count; i++)
            {
                DataProcessingAllocationRecord record = records[i];
                if (record?.overseer != null && record.target != null && record.steps > 0)
                {
                    AddToCaches(record);
                }
            }

            RebuildSpecializationCaches();
            RebuildDynamicAllocationCaches();
            RebuildDynamicTargetCaches();
        }

        private void RebuildSpecializationCaches()
        {
            specializationRecordByTarget = new Dictionary<Pawn, DataProcessingSpecializationRecord>();
            if (specializationRecords == null)
            {
                return;
            }

            for (int i = 0; i < specializationRecords.Count; i++)
            {
                DataProcessingSpecializationRecord record = specializationRecords[i];
                if (record?.target != null)
                {
                    specializationRecordByTarget[record.target] = record;
                }
            }
        }

        private DataProcessingAllocationRecord? FindRecord(Pawn overseer, Pawn target)
        {
            if (!recordsByOverseer.TryGetValue(overseer, out List<DataProcessingAllocationRecord>? overseerRecords))
            {
                return null;
            }

            for (int i = 0; i < overseerRecords.Count; i++)
            {
                DataProcessingAllocationRecord record = overseerRecords[i];
                if (ReferenceEquals(record.target, target))
                {
                    return record;
                }
            }

            return null;
        }

        private DataProcessingSpecializationRecord? FindSpecializationRecord(
            Pawn? overseer,
            Pawn? target)
        {
            if (overseer == null || target == null || specializationRecords == null)
            {
                return null;
            }

            for (int i = 0; i < specializationRecords.Count; i++)
            {
                DataProcessingSpecializationRecord record = specializationRecords[i];
                if (record != null
                    && ReferenceEquals(record.overseer, overseer)
                    && ReferenceEquals(record.target, target))
                {
                    return record;
                }
            }

            return null;
        }

        /// <summary>
        /// 安全地建立目标在某特化下的指令聚焦 Hediff：先确保新 Hediff 存在并刷新 Severity，
        /// 成功后再清理旧版/重复指令聚焦。避免“先删后加”导致自我分配在读档/切换时
        /// 正面返还瞬时消失而致死。返回是否成功建立目标状态。
        /// </summary>
        private bool TryApplyCommandFocusHediffForTarget(
            Pawn target,
            DataProcessingSpecialization specialization,
            int steps,
            bool cleanupOtherCommandFocusHediffs)
        {
            if (target == null
                || target.Destroyed
                || target.health?.hediffSet == null)
            {
                return false;
            }

            if (steps <= 0)
            {
                // 只有在监管者负面数据流分发已经先完成降低后，才允许走到这里删除全部指令聚焦。
                RemoveAllCommandFocusHediffs(target);
                return true;
            }

            HediffDef? desiredDef =
                DataProcessingAllocationUtility.GetCommandFocusDef(specialization);

            if (desiredDef == null)
            {
                return false;
            }

            Hediff? desired = null;
            List<Hediff> current = target.health.hediffSet.hediffs;
            for (int i = 0; i < current.Count; i++)
            {
                Hediff candidate = current[i];
                if (candidate != null && candidate.def == desiredDef)
                {
                    desired = candidate;
                    break;
                }
            }

            if (desired == null)
            {
                try
                {
                    desired = target.health.AddHediff(desiredDef);
                }
                catch (Exception ex)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 添加指令聚焦健康状态失败：" +
                        $"target={target.LabelShort}（{target.ThingID}），" +
                        $"def={desiredDef.defName}：{ex}");
                    return false;
                }
            }

            if (desired == null)
            {
                return false;
            }

            desired.Severity = steps * DataProcessingAllocationUtility.StepPercent;

            if (!cleanupOtherCommandFocusHediffs)
            {
                return true;
            }

            // 新状态已成功建立后，才允许清理旧状态。
            List<Hediff> snapshot =
                new List<Hediff>(target.health.hediffSet.hediffs);
            for (int i = 0; i < snapshot.Count; i++)
            {
                Hediff old = snapshot[i];
                if (old == null
                    || ReferenceEquals(old, desired)
                    || !DataProcessingAllocationUtility.IsAnyCommandFocusDef(old.def))
                {
                    continue;
                }

                target.health.RemoveHediff(old);
            }

            return true;
        }

        private static void RemoveAllCommandFocusHediffs(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null)
            {
                return;
            }

            List<Hediff> snapshot = new List<Hediff>(pawn.health.hediffSet.hediffs);
            for (int i = 0; i < snapshot.Count; i++)
            {
                Hediff hediff = snapshot[i];
                if (hediff != null
                    && DataProcessingAllocationUtility.IsAnyCommandFocusDef(hediff.def))
                {
                    pawn.health.RemoveHediff(hediff);
                }
            }
        }

        private static Hediff? FindAnyCommandFocusHediff(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null)
            {
                return null;
            }

            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff hediff = hediffs[i];
                if (hediff != null
                    && DataProcessingAllocationUtility.IsAnyCommandFocusDef(hediff.def))
                {
                    return hediff;
                }
            }

            return null;
        }

        private void RemoveSpecializationRecord(DataProcessingSpecializationRecord record)
        {
            if (record == null)
            {
                return;
            }

            specializationRecords.Remove(record);
            if (record.target != null
                && specializationRecordByTarget.TryGetValue(record.target, out DataProcessingSpecializationRecord? cached)
                && ReferenceEquals(cached, record))
            {
                specializationRecordByTarget.Remove(record.target);
            }
        }

        private void RemoveSpecializationRecordsForTarget(Pawn target)
        {
            if (target == null || specializationRecords == null)
            {
                return;
            }

            for (int i = specializationRecords.Count - 1; i >= 0; i--)
            {
                DataProcessingSpecializationRecord? record = specializationRecords[i];
                if (record != null && ReferenceEquals(record.target, target))
                {
                    specializationRecords.RemoveAt(i);
                }
            }

            specializationRecordByTarget.Remove(target);
        }

        private void RemoveSpecializationRecordsForOverseer(Pawn overseer)
        {
            if (overseer == null || specializationRecords == null)
            {
                return;
            }

            for (int i = specializationRecords.Count - 1; i >= 0; i--)
            {
                DataProcessingSpecializationRecord? record = specializationRecords[i];
                if (record != null && ReferenceEquals(record.overseer, overseer))
                {
                    specializationRecords.RemoveAt(i);
                }
            }

            RebuildSpecializationCaches();
        }

        /// <summary>
        /// 仅删除指定 overseer 与 target 组合的特化配置，不动其他监管者已保存的配置。
        /// 用于监管者替换时精确清理旧关系。
        /// </summary>
        private void RemoveSpecializationRecordsForOverseerTarget(
            Pawn overseer,
            Pawn target)
        {
            if (overseer == null
                || target == null
                || specializationRecords == null)
            {
                return;
            }

            bool changed = false;
            for (int i = specializationRecords.Count - 1; i >= 0; i--)
            {
                DataProcessingSpecializationRecord? record =
                    specializationRecords[i];

                if (record != null
                    && ReferenceEquals(record.overseer, overseer)
                    && ReferenceEquals(record.target, target))
                {
                    specializationRecords.RemoveAt(i);
                    changed = true;
                }
            }

            if (changed)
            {
                RebuildSpecializationCaches();
            }
        }

        /// <summary>
        /// 清除所有 overseer == target 的自我特化配置，包括 0% 预选配置。
        /// 不依赖 records（其仅保存正数分配）。
        /// </summary>
        private void RemoveSelfSpecializationRecords()
        {
            if (specializationRecords == null)
            {
                return;
            }

            bool changed = false;
            for (int i = specializationRecords.Count - 1; i >= 0; i--)
            {
                DataProcessingSpecializationRecord? record =
                    specializationRecords[i];

                if (record != null
                    && record.overseer != null
                    && ReferenceEquals(record.overseer, record.target))
                {
                    specializationRecords.RemoveAt(i);
                    changed = true;
                }
            }

            if (changed)
            {
                RebuildSpecializationCaches();
            }
        }

        private void CleanupInvalidSpecializationRecords()
        {
            specializationRecords ??= new List<DataProcessingSpecializationRecord>();

            Dictionary<Pawn, DataProcessingSpecializationRecord> validByTarget =
                new Dictionary<Pawn, DataProcessingSpecializationRecord>();
            for (int i = specializationRecords.Count - 1; i >= 0; i--)
            {
                DataProcessingSpecializationRecord? record = specializationRecords[i];
                if (record == null
                    || record.overseer == null
                    || record.target == null
                    || !DataProcessingAllocationUtility.IsValidAllocationPair(
                        record.overseer,
                        record.target))
                {
                    specializationRecords.RemoveAt(i);
                    continue;
                }

                // 同一 target 只保留一个有效记录，其余视为重复配置清理。
                if (validByTarget.ContainsKey(record.target))
                {
                    specializationRecords.RemoveAt(i);
                    continue;
                }

                validByTarget[record.target] = record;
            }

            RebuildSpecializationCaches();
        }

        private static void RemoveHediff(Pawn pawn, HediffDef? def)
        {
            if (pawn?.health?.hediffSet == null || def == null)
            {
                return;
            }

            Hediff? hediff = pawn.health.hediffSet.GetFirstHediffOfDef(def);
            if (hediff != null)
            {
                pawn.health.RemoveHediff(hediff);
            }
        }

        private void RunConsciousnessProtection()
        {
            if (!ModsConfig.BiotechActive)
            {
                return;
            }

            // 分配与顶置均为空时，无需清理或读取机械师注册表。
            if (records.Count == 0 && pinRecords.Count == 0)
            {
                return;
            }

            // 全表清理每轮只执行一次；即使没有机械师也要清理无效记录。
            CleanupInvalidRecords();

            // 清理后已无分配记录：顶置可能仍在，但意识保护只依赖分配档数。
            if (records.Count == 0)
            {
                return;
            }

            IReadOnlyList<Pawn> mechanitors =
                GameComponent_MechanoidMechanitorRegistry.CurrentRegisteredMechanitors;
            for (int i = 0; i < mechanitors.Count; i++)
            {
                Pawn overseer = mechanitors[i];
                if (overseer == null || overseer.Destroyed)
                {
                    continue;
                }

                ProtectOverseerConsciousness(overseer);
            }
        }

        private void ProtectOverseerConsciousness(Pawn overseer)
        {
            if (GetTotalStepsForOverseer(overseer) <= 0)
            {
                return;
            }

            if (!DataProcessingAllocationUtility.TryGetCurrentConsciousness(
                    overseer,
                    out float consciousness))
            {
                // 读取失败不得误判为意识归零；等待下一周期重试。
                return;
            }

            if (consciousness >= DataProcessingAllocationUtility.MinReservedConsciousness)
            {
                return;
            }

            // 每名机械师最多两轮批量规划/提交，禁止逐档同步循环。
            for (int round = 0; round < 2; round++)
            {
                if (GetTotalStepsForOverseer(overseer) <= 0)
                {
                    return;
                }

                if (consciousness >= DataProcessingAllocationUtility.MinReservedConsciousness)
                {
                    return;
                }

                Dictionary<DataProcessingAllocationRecord, int> plan =
                    BuildProtectionReductionPlan(overseer, consciousness);
                if (plan.Count == 0)
                {
                    return;
                }

                ApplyProtectionReductionPlan(overseer, plan);

                if (!DataProcessingAllocationUtility.TryGetCurrentConsciousness(
                        overseer,
                        out consciousness))
                {
                    return;
                }
            }
        }

        /// <summary>
        /// 规划阶段：只计算每条记录应减少的档数，不修改真实记录，也不同步 Hediff。
        /// 减档顺序按动态优先级 4（最低）→ 3 → 2 → 1（最高）依次进行：
        /// 优先削减优先级最低的目标，越重要的目标越晚被削减。
        /// </summary>
        private Dictionary<DataProcessingAllocationRecord, int> BuildProtectionReductionPlan(
            Pawn overseer,
            float currentConsciousness)
        {
            Dictionary<DataProcessingAllocationRecord, int> plannedReduction =
                new Dictionary<DataProcessingAllocationRecord, int>();

            if (!recordsByOverseer.TryGetValue(
                    overseer,
                    out List<DataProcessingAllocationRecord>? overseerRecords)
                || overseerRecords == null
                || overseerRecords.Count == 0)
            {
                return plannedReduction;
            }

            // 按保护优先级升序排列：优先削减 priority 数值大的（最低优先级）。
            List<DataProcessingAllocationRecord> orderedRecords =
                new List<DataProcessingAllocationRecord>(overseerRecords);
            orderedRecords.Sort((left, right) =>
            {
                int lp = GetProtectionPriority(overseer, left);
                int rp = GetProtectionPriority(overseer, right);
                if (lp != rp)
                {
                    return rp.CompareTo(lp); // priority 大的排前面（先减）
                }

                int ls = left?.steps ?? 0;
                int rs = right?.steps ?? 0;
                if (ls != rs)
                {
                    return rs.CompareTo(ls); // 同优先级档数大的先减
                }

                int ln = left?.target?.thingIDNumber ?? 0;
                int rn = right?.target?.thingIDNumber ?? 0;
                return ln.CompareTo(rn);
            });

            Dictionary<DataProcessingAllocationRecord, int> plannedRemaining =
                new Dictionary<DataProcessingAllocationRecord, int>();
            for (int i = 0; i < orderedRecords.Count; i++)
            {
                DataProcessingAllocationRecord? record = orderedRecords[i];
                if (record == null || record.steps <= 0)
                {
                    continue;
                }

                plannedRemaining[record] = record.steps;
                plannedReduction[record] = 0;
            }

            if (plannedRemaining.Count == 0)
            {
                return plannedReduction;
            }

            float projectedConsciousness = currentConsciousness;
            while (projectedConsciousness
                < DataProcessingAllocationUtility.MinReservedConsciousness)
            {
                DataProcessingAllocationRecord? next =
                    FindNextProtectionReductionPlan(
                        overseer,
                        orderedRecords,
                        plannedRemaining);
                if (next == null)
                {
                    break;
                }

                plannedRemaining[next]--;
                plannedReduction[next]++;
                projectedConsciousness += GetProtectionRecoveryPerReducedStep(next);
            }

            // 去掉零减档条目，便于提交阶段快速判断空计划。
            List<DataProcessingAllocationRecord> zeroKeys =
                new List<DataProcessingAllocationRecord>();
            foreach (KeyValuePair<DataProcessingAllocationRecord, int> pair in plannedReduction)
            {
                if (pair.Value <= 0)
                {
                    zeroKeys.Add(pair.Key);
                }
            }

            for (int i = 0; i < zeroKeys.Count; i++)
            {
                plannedReduction.Remove(zeroKeys[i]);
            }

            return plannedReduction;
        }

        /// <summary>
        /// 取某记录在意识保护减档时的优先级：动态目标取其单体优先级，
        /// 无动态配置者视为默认优先级（3），优先于最高优先级但低于显式低优先级。
        /// </summary>
        private int GetProtectionPriority(
            Pawn overseer,
            DataProcessingAllocationRecord? record)
        {
            if (record == null || record.target == null)
            {
                return 3;
            }

            DataProcessingDynamicTargetRecord? config =
                FindDynamicTargetRecord(overseer, record.target);
            if (config == null)
            {
                return 3;
            }

            return Mathf.Clamp(config.priority, 1, 4);
        }

        /// <summary>
        /// 严格按优先级 4（最低）→ 3 → 2 → 1（最高）选择下一个应削减的记录：
        /// 先取所有仍有剩余档数记录中的最大优先级数字（即最该被削减的优先级），
        /// 仅在该优先级内选择；同级始终削减当前剩余档数较高者，相同时按 thingIDNumber 稳定排序。
        /// 这样优先级 4 未耗尽前不会触碰 3，且同级不会一次抽干单个目标。
        /// </summary>
        private DataProcessingAllocationRecord? FindNextProtectionReductionPlan(
            Pawn overseer,
            List<DataProcessingAllocationRecord> orderedRecords,
            Dictionary<DataProcessingAllocationRecord, int> plannedRemaining)
        {
            int activePriority = -1;

            for (int i = 0; i < orderedRecords.Count; i++)
            {
                DataProcessingAllocationRecord record = orderedRecords[i];

                if (!plannedRemaining.TryGetValue(
                        record,
                        out int remaining)
                    || remaining <= 0)
                {
                    continue;
                }

                activePriority =
                    Mathf.Max(
                        activePriority,
                        GetProtectionPriority(
                            overseer,
                            record));
            }

            if (activePriority < 0)
            {
                return null;
            }

            DataProcessingAllocationRecord? best = null;
            int bestRemaining = -1;
            int bestThingId = int.MaxValue;

            for (int i = 0; i < orderedRecords.Count; i++)
            {
                DataProcessingAllocationRecord record = orderedRecords[i];

                if (GetProtectionPriority(
                        overseer,
                        record)
                    != activePriority)
                {
                    continue;
                }

                if (!plannedRemaining.TryGetValue(
                        record,
                        out int remaining)
                    || remaining <= 0)
                {
                    continue;
                }

                int thingId =
                    record.target?.thingIDNumber
                    ?? int.MaxValue;

                if (remaining > bestRemaining
                    || (remaining == bestRemaining
                        && thingId < bestThingId))
                {
                    best = record;
                    bestRemaining = remaining;
                    bestThingId = thingId;
                }
            }

            return best;
        }

        private static float GetProtectionRecoveryPerReducedStep(
            DataProcessingAllocationRecord record)
        {
            if (DataProcessingAllocationUtility.IsSelfAllocationPair(
                    record.overseer,
                    record.target))
            {
                return DataProcessingAllocationUtility.StepPercent * 0.5f;
            }

            return DataProcessingAllocationUtility.StepPercent;
        }

        /// <summary>
        /// 提交阶段：按计划一次性减档，再统一同步受影响目标与监督者 Hediff。
        /// 减档只刷新数值，不替换健康状态种类。
        /// </summary>
        private void ApplyProtectionReductionPlan(
            Pawn overseer,
            Dictionary<DataProcessingAllocationRecord, int> plannedReduction)
        {
            if (plannedReduction.Count == 0)
            {
                return;
            }

            HashSet<Pawn> affectedTargets = new HashSet<Pawn>();
            List<KeyValuePair<DataProcessingAllocationRecord, int>> snapshot =
                new List<KeyValuePair<DataProcessingAllocationRecord, int>>(plannedReduction);
            bool anyChanged = false;

            for (int i = 0; i < snapshot.Count; i++)
            {
                DataProcessingAllocationRecord record = snapshot[i].Key;
                int reduction = snapshot[i].Value;
                if (record == null || reduction <= 0)
                {
                    continue;
                }

                Pawn? target = record.target;
                record.steps -= reduction;
                anyChanged = true;

                if (record.steps <= 0)
                {
                    RemoveRecord(record);
                }

                if (target != null && !target.Destroyed)
                {
                    affectedTargets.Add(target);
                }
            }

            if (!anyChanged)
            {
                return;
            }

            // 减档安全顺序：先降监管者负面数据流分发，再降各目标正面指令聚焦。
            SyncHediffsForOverseer(overseer);
            foreach (Pawn target in affectedTargets)
            {
                SyncHediffForTarget(target);
            }
        }
    }
}
