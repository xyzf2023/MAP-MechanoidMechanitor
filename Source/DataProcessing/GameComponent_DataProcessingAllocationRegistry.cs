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

        // 读档后统一动态校正标记（不写存档）。
        private bool pendingPostLoadDynamicReconciliation;
        private int postLoadReconciliationEarliestTick;

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

        public bool TryAddStep(Pawn? overseer, Pawn? target)
        {
            if (!ResearchFeatureUnlockUtility.IsDataProcessingAllocationUnlocked()
                || !DataProcessingAllocationUtility.IsValidAllocationPair(overseer, target)
                || !DataProcessingAllocationUtility.CanAddStep(overseer))
            {
                return false;
            }

            Pawn? replacedOverseer = null;
            DataProcessingAllocationRecord? record = FindRecord(overseer!, target!);
            if (record == null)
            {
                record = new DataProcessingAllocationRecord(overseer!, target!, 1);
                replacedOverseer = AddRecord(record);
            }
            else
            {
                record.steps++;
            }

            // 全局动态且单体动态开启时，加减修改常态额度并立即重算；
            // 否则同步 normalSteps 保持两者一致。
            if (IsDynamicAllocationEnabledForTarget(overseer!, target!))
            {
                DataProcessingDynamicTargetRecord? config =
                    FindDynamicTargetRecord(overseer!, target!);
                if (config != null)
                {
                    config.normalSteps = GetStepsForOverseerTarget(overseer!, target!);
                    config.Normalize();
                    RebuildDynamicTargetCaches();
                }

                TriggerSafeDynamicRecomputeForOverseer(overseer!);
            }
            else
            {
                SyncHediffForTarget(target!);
                SyncHediffsForOverseer(overseer!);
                SyncReplacedOverseerIfNeeded(replacedOverseer, overseer!);
            }

            return true;
        }

        public bool TryRemoveStep(Pawn? overseer, Pawn? target)
        {
            if (overseer == null || target == null)
            {
                return false;
            }

            DataProcessingAllocationRecord? record = FindRecord(overseer, target);
            if (record == null || record.steps <= 0)
            {
                return false;
            }

            record.steps--;
            if (record.steps <= 0)
            {
                RemoveRecord(record);
                // 分配归零仅移除健康状态，保留特化配置记录。
            }

            if (IsDynamicAllocationEnabledForTarget(overseer, target))
            {
                DataProcessingDynamicTargetRecord? config =
                    FindDynamicTargetRecord(overseer, target);
                if (config != null)
                {
                    config.normalSteps = Mathf.Max(0, record.steps);
                    config.Normalize();
                    RebuildDynamicTargetCaches();
                }

                TriggerSafeDynamicRecomputeForOverseer(overseer);
            }
            else
            {
                // 减档安全顺序：先降监管者负面数据流分发，再降目标正面指令聚焦。
                SyncHediffsForOverseer(overseer);
                SyncHediffForTarget(target);
            }

            return true;
        }

        public void SetSteps(Pawn? overseer, Pawn? target, int steps)
        {
            if (overseer == null || target == null)
            {
                return;
            }

            steps = Mathf.Max(0, steps);
            if (steps > 0
                && (!ResearchFeatureUnlockUtility.IsDataProcessingAllocationUnlocked()
                    || !DataProcessingAllocationUtility.IsValidAllocationPair(overseer, target)))
            {
                return;
            }

            Pawn? replacedOverseer = null;
            DataProcessingAllocationRecord? record = FindRecord(overseer, target);
            int oldSteps = record?.steps ?? 0;
            if (steps <= 0)
            {
                if (record != null)
                {
                    RemoveRecord(record);
                    // 分配归零仅移除健康状态，保留特化配置记录。
                }
            }
            else if (record == null)
            {
                replacedOverseer = AddRecord(
                    new DataProcessingAllocationRecord(overseer, target, steps));
            }
            else
            {
                record.steps = steps;
            }

            // 全局动态且单体动态开启时，由动态计划接管；否则同步 normalSteps 并保持一致。
            if (IsDynamicAllocationEnabledForTarget(overseer, target))
            {
                DataProcessingDynamicTargetRecord? config =
                    FindDynamicTargetRecord(overseer, target);
                if (config != null)
                {
                    config.normalSteps = Mathf.Max(0, steps);
                    config.Normalize();
                    RebuildDynamicTargetCaches();
                }

                TriggerSafeDynamicRecomputeForOverseer(overseer);
            }
            else
            {
                // 安全同步顺序：减档先负面后正面；加档先正面后负面。
                if (steps < oldSteps)
                {
                    SyncHediffsForOverseer(overseer);
                    SyncHediffForTarget(target);
                }
                else
                {
                    SyncHediffForTarget(target);
                    SyncHediffsForOverseer(overseer);
                }

                SyncReplacedOverseerIfNeeded(replacedOverseer, overseer);
            }
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
                // 开启后立即对所有目标执行一次完整动态计划（含特化校正与额度计算）。
                RunDynamicPlanForOverseer(overseer);
            }
            else
            {
                // 关闭全局动态：所有目标实际额度恢复 normalSteps，特化安全恢复默认模式。
                RestoreDefaultsForOverseer(overseer);
            }

            return true;
        }

        private void RestoreDefaultsForOverseer(Pawn overseer)
        {
            if (overseer == null || overseer.Destroyed)
            {
                return;
            }

            List<Pawn> targets = new List<Pawn>();
            CollectDynamicAllocationTargets(overseer, targets);
            foreach (Pawn target in targets)
            {
                if (target == null || target.Destroyed)
                {
                    continue;
                }

                DataProcessingDynamicTargetRecord? record =
                    FindDynamicTargetRecord(overseer, target);
                if (record == null)
                {
                    continue;
                }

                int normalSteps = record.normalSteps;
                int currentSteps = GetStepsForOverseerTarget(overseer, target);
                if (normalSteps < currentSteps)
                {
                    SyncHediffsForOverseer(overseer);
                }

                SetStepsInternal(overseer, target, normalSteps);

                if (normalSteps < currentSteps)
                {
                    SyncHediffForTarget(target);
                }
                else
                {
                    SyncHediffForTarget(target);
                    SyncHediffsForOverseer(overseer);
                }

                DataProcessingSpecialization desired = record.defaultSpecialization;
                if (desired != GetSpecializationForOverseerTarget(overseer, target)
                    && normalSteps > 0)
                {
                    TrySetSpecialization(overseer, target, desired);
                }
            }

            SyncHediffsForOverseer(overseer);
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
            TriggerSafeDynamicRecomputeForOverseer(overseer);
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
            TriggerSafeDynamicRecomputeForOverseer(overseer);
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
            TriggerSafeDynamicRecomputeForOverseer(overseer);
            return true;
        }

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
                DataProcessingAllocationUtility.NormalizeSpecialization(specialization);
            RebuildDynamicTargetCaches();

            // 若目标当前处于闲置/回落状态（当前特化已等于默认模式），立即安全应用；
            // 若处于工作/战斗状态，保留当前临时模式，回落后再使用新默认模式。
            DataProcessingSpecialization current =
                GetSpecializationForOverseerTarget(overseer, target);
            if (current == record.defaultSpecialization)
            {
                return true;
            }

            int steps = GetStepsForOverseerTarget(overseer, target);
            if (steps <= 0)
            {
                return true;
            }

            TrySetSpecialization(overseer, target, record.defaultSpecialization);
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
            TriggerSafeDynamicRecomputeForOverseer(overseer);
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
            ScheduleDynamicTargetCheck(target, ticks, immediate: false);
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
            TriggerSafeDynamicRecomputeForOverseer(overseer);
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
            TriggerSafeDynamicRecomputeForOverseer(overseer);
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
            TriggerSafeDynamicRecomputeForOverseer(overseer);
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
            TriggerSafeDynamicRecomputeForOverseer(overseer);
            return true;
        }

        private void TriggerSafeDynamicRecomputeForOverseer(Pawn overseer)
        {
            if (!IsDynamicAllocationEnabled(overseer))
            {
                return;
            }

            RunDynamicPlanForOverseer(overseer);
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

        private void CleanupInvalidDynamicTargetRecords()
        {
            dynamicTargetRecords ??=
                new List<DataProcessingDynamicTargetRecord>();

            HashSet<Pawn> seenTargets = new HashSet<Pawn>();
            for (int i = dynamicTargetRecords.Count - 1; i >= 0; i--)
            {
                DataProcessingDynamicTargetRecord? record = dynamicTargetRecords[i];
                if (record == null
                    || record.target == null
                    || record.overseer == null
                    || !DataProcessingAllocationUtility.IsValidAllocationPair(
                        record.overseer,
                        record.target)
                    || !seenTargets.Add(record.target))
                {
                    dynamicTargetRecords.RemoveAt(i);
                    if (record?.target != null)
                    {
                        nextDynamicCheckTickByTarget.Remove(record.target);
                    }
                }
            }

            RebuildDynamicTargetCaches();
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
                    }

                    dynamicTargetRecords.RemoveAt(i);
                }
            }

            RebuildDynamicTargetCaches();
        }

        // ===== 轻量调度与批量计划 =====

        private void TickDynamicScheduler()
        {
            int now = Find.TickManager.TicksGame;
            if (nextDynamicCheckTickByTarget.Count == 0)
            {
                return;
            }

            List<Pawn> due = new List<Pawn>();
            foreach (KeyValuePair<Pawn, int> pair in nextDynamicCheckTickByTarget)
            {
                if (pair.Key != null && !pair.Key.Destroyed && pair.Value <= now)
                {
                    due.Add(pair.Key);
                }
            }

            if (due.Count == 0)
            {
                return;
            }

            // 按监管者分组，每个监管者至少一个目标到期才整体重算一次。
            HashSet<Pawn> overseersToRecompute = new HashSet<Pawn>();
            for (int i = 0; i < due.Count; i++)
            {
                Pawn target = due[i];
                DataProcessingDynamicTargetRecord? record =
                    FindDynamicTargetRecord(overseer: null, target);
                if (record?.overseer != null)
                {
                    overseersToRecompute.Add(record.overseer);
                }
            }

            foreach (Pawn overseer in overseersToRecompute)
            {
                if (overseer != null && !overseer.Destroyed)
                {
                    RunDynamicPlanForOverseer(overseer);
                }
            }
        }

        /// <summary>
        /// 单体目标评估：生成运行时状态与期望模式。
        /// </summary>
        private DataProcessingDynamicState EvaluateTargetState(
            DataProcessingDynamicTargetRecord config,
            Pawn target)
        {
            int checkInterval = config.checkIntervalTicks;

            // 1. 近战接战（最高优先级）。
            if (config.switchForCloseMelee
                && IsDynamicCloseMeleeEngagement(target, checkInterval))
            {
                return DataProcessingDynamicState.CloseMelee;
            }

            bool drafted = target.Drafted;
            bool switchWeapon = config.switchForDraftedWeapon;

            if (drafted && switchWeapon)
            {
                Verb? verb = DataProcessingDynamicAllocationUtility.GetCurrentAttackVerb(target);
                bool hasAnyRanged = DataProcessingDynamicAllocationUtility.HasRangedAttackVerb(target);
                bool hasAnyMelee = DataProcessingDynamicAllocationUtility.IsMeleeCombatMech(target);

                // 2/3. 已征召且根据武器切换。
                if (verb != null && verb.verbProps != null && !verb.IsMeleeAttack
                    && (verb.verbProps.violent || verb.verbProps.IsMeleeAttack))
                {
                    if (verb.verbProps.range > 1.42f
                        && verb.verbProps.ai_IsWeapon)
                    {
                        return DataProcessingDynamicState.DraftedRanged;
                    }

                    if (verb.verbProps.IsMeleeAttack)
                    {
                        return DataProcessingDynamicState.DraftedMelee;
                    }
                }

                // 没有明确当前 Verb 时，按武器能力回退。
                if (hasAnyRanged && !hasAnyMelee)
                {
                    return DataProcessingDynamicState.DraftedRanged;
                }

                if (hasAnyMelee && !hasAnyRanged)
                {
                    return DataProcessingDynamicState.DraftedMelee;
                }
            }

            // 4. 未征召且正在执行工作。
            if (config.switchForWork && IsPawnDoingWork(target))
            {
                return DataProcessingDynamicState.Working;
            }

            // 5. 未征召回落。
            if (config.applyUndraftedFallback)
            {
                return DataProcessingDynamicState.Idle;
            }

            // 6. 不回落则保持默认模式（视为 Idle 回落处理）。
            return DataProcessingDynamicState.Idle;
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
        /// </summary>
        private void RunDynamicPlanForOverseer(Pawn overseer)
        {
            if (overseer == null
                || overseer.Destroyed
                || !IsDynamicAllocationOverseerValid(overseer)
                || !IsDynamicAllocationEnabled(overseer))
            {
                return;
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

                bool targetDynamicEnabled = config.enabled;
                DataProcessingSpecialization desiredSpecialization;
                int requestedSteps;

                if (!targetDynamicEnabled)
                {
                    // 单体动态关闭：实际额度固定为常态额度，当前模式恢复默认模式。
                    desiredSpecialization = config.defaultSpecialization;
                    requestedSteps = config.normalSteps;
                }
                else
                {
                    DataProcessingDynamicState state = EvaluateTargetState(config, target);
                    desiredSpecialization = ResolveDesiredSpecialization(
                        config,
                        state,
                        target);
                    bool taskActive = state != DataProcessingDynamicState.Idle;
                    requestedSteps = taskActive
                        ? config.GetMaxStepsForSpecialization(desiredSpecialization)
                        : config.normalSteps;
                }

                entries.Add(new PlanEntry
                {
                    target = target,
                    config = config,
                    desiredSpecialization = desiredSpecialization,
                    requestedSteps = requestedSteps,
                    currentSteps = GetStepsForOverseerTarget(overseer, target),
                    currentSpecialization = GetSpecializationForOverseerTarget(overseer, target)
                });

                // 安排下一次检查时间（错峰）。
                int interval = config.checkIntervalTicks;
                nextDynamicCheckTickByTarget[target] =
                    Find.TickManager.TicksGame + Mathf.Max(60, interval);
            }

            if (entries.Count == 0)
            {
                return;
            }

            ApplyDynamicPlan(overseer, entries);
        }

        private DataProcessingSpecialization ResolveDesiredSpecialization(
            DataProcessingDynamicTargetRecord config,
            DataProcessingDynamicState state,
            Pawn target)
        {
            switch (state)
            {
                case DataProcessingDynamicState.CloseMelee:
                case DataProcessingDynamicState.DraftedMelee:
                    return DataProcessingSpecialization.AssaultProtocol;

                case DataProcessingDynamicState.DraftedRanged:
                    return DataProcessingSpecialization.FireControlCalculation;

                case DataProcessingDynamicState.Working:
                    return DataProcessingSpecialization.ProductionCoordination;

                case DataProcessingDynamicState.Idle:
                default:
                    return config.defaultSpecialization;
            }
        }

        /// <summary>
        /// 计算预算并生成最终实际档数，按优先级与同级轮流分配。
        /// </summary>
        private void ApplyDynamicPlan(Pawn overseer, List<PlanEntry> entries)
        {
            if (overseer == null || entries == null)
            {
                return;
            }

            DataProcessingDynamicAllocationRecord? overseerRecord =
                FindDynamicAllocationRecord(overseer);
            float threshold = overseerRecord != null
                ? overseerRecord.minConsciousnessPercent / 100f
                : 1f;

            // 计算固定分配（单体动态关闭者）与可用预算。
            int fixedBudgetUnits = 0;
            int dynamicRequestUnits = 0;
            List<PlanEntry> dynamicEntries = new List<PlanEntry>();

            for (int i = 0; i < entries.Count; i++)
            {
                PlanEntry entry = entries[i];
                int steps = Mathf.Max(0, entry.requestedSteps);
                int units = steps * 2; // 1 档 = 2 预算单位（非自身）。

                bool selfPair = DataProcessingAllocationUtility.IsSelfAllocationPair(
                    overseer,
                    entry.target);
                if (selfPair)
                {
                    units = steps; // 自身净消耗半额：1 档 = 1 预算单位。
                }

                if (!entry.config.enabled)
                {
                    // 单体动态关闭：固定分配，先扣预算。
                    fixedBudgetUnits += units;
                }
                else
                {
                    dynamicEntries.Add(entry);
                    dynamicRequestUnits += units;
                }
            }

            // 反推基础意识（不含当前分配）。
            float currentConsciousness =
                DataProcessingAllocationUtility.GetCurrentConsciousness(overseer);
            float baseConsciousness = currentConsciousness;
            List<DataProcessingAllocationRecord> overseerRecords =
                recordsByOverseer.TryGetValue(overseer, out List<DataProcessingAllocationRecord>? list)
                    ? list
                    : new List<DataProcessingAllocationRecord>();

            foreach (DataProcessingAllocationRecord record in overseerRecords)
            {
                if (record == null)
                {
                    continue;
                }

                float costPerStep =
                    DataProcessingAllocationUtility.IsSelfAllocationPair(
                        record.overseer,
                        record.target)
                        ? DataProcessingAllocationUtility.StepPercent * 0.5f
                        : DataProcessingAllocationUtility.StepPercent;

                baseConsciousness += record.steps * costPerStep;
            }

            float availableConsciousness =
                Mathf.Max(0f, baseConsciousness - threshold);
            int availableBudgetUnits =
                Mathf.FloorToInt((availableConsciousness + 0.0001f) / 0.025f);

            int remainingBudget = availableBudgetUnits - fixedBudgetUnits;
            if (remainingBudget < 0)
            {
                remainingBudget = 0;
                Log.Warning(
                    "[MAP-机械族机械师] 动态预算不足：固定分配已超过动态阈值，" +
                    $"overseer={overseer.LabelShort}（{overseer.ThingID}）。");
                // 仍按 fixedBudget 应用固定分配，dynamicEntries 回落到其 normalSteps。
            }

            // 按优先级分组：1 最高 → 4 最低。
            List<List<PlanEntry>> byPriority = new List<List<PlanEntry>>
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

            // 每个 entry 的当前已分配档数。
            Dictionary<PlanEntry, int> allocated =
                new Dictionary<PlanEntry, int>();
            foreach (PlanEntry entry in dynamicEntries)
            {
                allocated[entry] = Mathf.Max(0, entry.currentSteps);
            }

            // 同级稳定顺序：顶置优先 → pinOrder → thingIDNumber。
            for (int p = 0; p < byPriority.Count; p++)
            {
                byPriority[p].Sort((left, right) =>
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

            // 轮流逐档分配直到请求满足或预算耗尽。
            bool changed;
            do
            {
                changed = false;
                for (int p = 0; p < byPriority.Count; p++)
                {
                    for (int i = 0; i < byPriority[p].Count; i++)
                    {
                        PlanEntry entry = byPriority[p][i];
                        int current = allocated[entry];
                        int desired = Mathf.Max(0, entry.requestedSteps);
                        if (current >= desired)
                        {
                            continue;
                        }

                        bool selfPair = DataProcessingAllocationUtility.IsSelfAllocationPair(
                            overseer,
                            entry.target);
                        int costUnits = selfPair ? 1 : 2;
                        if (remainingBudget < costUnits)
                        {
                            continue;
                        }

                        allocated[entry] = current + 1;
                        remainingBudget -= costUnits;
                        changed = true;
                    }
                }
            }
            while (changed && remainingBudget > 0);

            // 生成最终计划并安全应用。
            BuildAndApplyPlan(overseer, entries, allocated);
        }

        private void BuildAndApplyPlan(
            Pawn overseer,
            List<PlanEntry> entries,
            Dictionary<PlanEntry, int> allocated)
        {
            // 阶段一：降低额度（先监管者负面，再目标正面）。
            List<PlanEntry> toReduce = new List<PlanEntry>();
            List<PlanEntry> toRaise = new List<PlanEntry>();
            List<PlanEntry> toSwitch = new List<PlanEntry>();

            foreach (PlanEntry entry in entries)
            {
                int desired = allocated.TryGetValue(entry, out int v) ? v : Mathf.Max(0, entry.currentSteps);
                int current = Mathf.Max(0, entry.currentSteps);

                if (desired < current)
                {
                    toReduce.Add(entry);
                }
                else if (desired > current)
                {
                    toRaise.Add(entry);
                }

                if (entry.desiredSpecialization != entry.currentSpecialization
                    && desired > 0)
                {
                    toSwitch.Add(entry);
                }
            }

            // 先同步所有受降档影响的监管者负面（此处仅 overseer）。
            SyncHediffsForOverseer(overseer);
            foreach (PlanEntry entry in toReduce)
            {
                int desired = allocated[entry];
                SetStepsInternal(overseer, entry.target, desired);
            }

            // 阶段二：安全切换特化。
            foreach (PlanEntry entry in toSwitch)
            {
                TrySetSpecialization(overseer, entry.target, entry.desiredSpecialization);
            }

            // 阶段三：提高额度（先目标正面，再监管者负面）。
            foreach (PlanEntry entry in toRaise)
            {
                int desired = allocated[entry];
                SetStepsInternal(overseer, entry.target, desired);
            }

            SyncHediffsForOverseer(overseer);
        }

        /// <summary>
        /// 仅修改实际档数，不触碰 normalSteps，供动态计划内部使用。
        /// </summary>
        private void SetStepsInternal(Pawn overseer, Pawn? target, int steps)
        {
            if (target == null)
            {
                return;
            }

            steps = Mathf.Max(0, steps);
            int oldSteps = GetStepsForOverseerTarget(overseer, target);
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

            if (steps < oldSteps)
            {
                SyncHediffsForOverseer(overseer);
                SyncHediffForTarget(target);
            }
            else
            {
                SyncHediffForTarget(target);
                SyncHediffsForOverseer(overseer);
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

        public void CleanupInvalidRecords()
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

            // 批处理：先同步所有受影响监管者负面，再清理目标正面。
            foreach (Pawn overseer in affectedOverseers)
            {
                if (overseer != null && !overseer.Destroyed)
                {
                    SyncHediffsForOverseer(overseer);
                }
            }

            foreach (Pawn target in affectedTargets)
            {
                if (target != null && !target.Destroyed)
                {
                    RemoveAllCommandFocusHediffs(target);
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
                CleanupInvalidRecords();
                CleanupInvalidSpecializationRecords();
                CleanupInvalidDynamicAllocationRecords();
                CleanupInvalidDynamicTargetRecords();
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
            }

            protectionTickCounter++;
            if (protectionTickCounter < 600)
            {
                return;
            }

            protectionTickCounter = 0;
            RunDynamicAllocation();
            RunConsciousnessProtection();
        }

        private bool TryRunPostLoadDynamicReconciliation()
        {
            // 确认容量与组件可读，避免不安全状态下增删 Hediff。
            bool anyEnabled = false;
            for (int i = 0; i < dynamicAllocationRecords.Count; i++)
            {
                if (dynamicAllocationRecords[i]?.enabled == true
                    && dynamicAllocationRecords[i]!.overseer != null
                    && !dynamicAllocationRecords[i]!.overseer!.Destroyed)
                {
                    anyEnabled = true;
                    break;
                }
            }

            if (!anyEnabled)
            {
                return true;
            }

            // 为每个启用动态分配的目标建立/迁移单体配置；保留已有特化作为默认模式。
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
                }

                try
                {
                    RunDynamicPlanForOverseer(rec.overseer);
                }
                catch (Exception ex)
                {
                    Log.ErrorOnce(
                        "[MAP-机械族机械师] 读档后动态校正失败：" +
                        $"overseer={rec.overseer.LabelShort}（{rec.overseer.ThingID}）：{ex}",
                        unchecked(DynamicAllocationLogKeyBase + rec.overseer.thingIDNumber));
                }
            }

            return true;
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            CleanupInvalidRecords();
            CleanupInvalidSpecializationRecords();
            RebuildCaches();
            RestoreCommandFocusHediffsAfterLoad();

            // 设置读档后统一动态校正标记，下一实际 tick 再批量校正，避免立即旧版静态分类覆盖。
            pendingPostLoadDynamicReconciliation = true;
            postLoadReconciliationEarliestTick = Find.TickManager.TicksGame + 1;
        }

        /// <summary>
        /// 仅在载入存档流程中调用：安全恢复指令聚焦。
        /// 先确保每个正数目标拥有正确指令聚焦，再同步监管者负面数据流分发，最后清理孤儿状态。
        /// 不根据身份重新判断正义/隐者/恋人的默认模式。
        /// </summary>
        private void RestoreCommandFocusHediffsAfterLoad()
        {
            HashSet<Pawn> overseers = new HashSet<Pawn>();
            for (int i = 0; i < records.Count; i++)
            {
                DataProcessingAllocationRecord? record = records[i];
                if (record == null || record.steps <= 0)
                {
                    continue;
                }

                Pawn? target = record.target;
                if (target != null && !target.Destroyed)
                {
                    DataProcessingSpecialization specialization = GetSpecializationForTarget(target);
                    TryApplyCommandFocusHediffForTarget(
                        target,
                        specialization,
                        record.steps,
                        cleanupOtherCommandFocusHediffs: true);
                }

                if (record.overseer != null)
                {
                    overseers.Add(record.overseer);
                }
            }

            // 所有目标正面状态建立后，再同步监管者负面数据流分发。
            foreach (Pawn overseer in overseers)
            {
                if (overseer != null && !overseer.Destroyed)
                {
                    SyncHediffsForOverseer(overseer);
                }
            }

            ScrubResidualOrphanCommandFocusHediffs();
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

            // 快照列表顺序，保证档数相同时取先出现者（严格大于比较）。
            List<DataProcessingAllocationRecord> orderedRecords =
                new List<DataProcessingAllocationRecord>(overseerRecords);
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
                DataProcessingAllocationRecord? largest =
                    FindLargestPlannedRemainingRecord(orderedRecords, plannedRemaining);
                if (largest == null)
                {
                    break;
                }

                plannedRemaining[largest]--;
                plannedReduction[largest]++;
                projectedConsciousness += GetProtectionRecoveryPerReducedStep(largest);
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

        private static DataProcessingAllocationRecord? FindLargestPlannedRemainingRecord(
            List<DataProcessingAllocationRecord> orderedRecords,
            Dictionary<DataProcessingAllocationRecord, int> plannedRemaining)
        {
            DataProcessingAllocationRecord? largest = null;
            int largestRemaining = 0;
            for (int i = 0; i < orderedRecords.Count; i++)
            {
                DataProcessingAllocationRecord record = orderedRecords[i];
                if (!plannedRemaining.TryGetValue(record, out int remaining) || remaining <= 0)
                {
                    continue;
                }

                // 严格大于：档数相同时保留先出现的记录。
                if (largest == null || remaining > largestRemaining)
                {
                    largest = record;
                    largestRemaining = remaining;
                }
            }

            return largest;
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
