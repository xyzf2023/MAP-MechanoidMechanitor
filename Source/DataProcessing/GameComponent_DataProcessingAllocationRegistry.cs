using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

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

        private int protectionTickCounter;

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

            DataProcessingSpecializationRecord? existing = FindSpecializationRecord(overseer, target);
            if (existing != null)
            {
                if (existing.specialization == specialization)
                {
                    // 新旧特化相同，不重复替换 Hediff，仅确认成功。
                    return true;
                }

                existing.specialization = specialization;
            }
            else
            {
                specializationRecords.Add(
                    new DataProcessingSpecializationRecord(overseer!, target!, specialization));
            }

            RebuildSpecializationCaches();

            int steps = GetStepsForTarget(target);
            if (steps <= 0)
            {
                // 分配为 0 时只保存选择，不添加 Hediff。
                return true;
            }

            // 仅手动切换时替换目标 Hediff。
            ReplaceCommandFocusHediffForTarget(target!, specialization);
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

            SyncHediffForTarget(target!);
            SyncHediffsForOverseer(overseer!);
            SyncReplacedOverseerIfNeeded(replacedOverseer, overseer!);
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

            SyncHediffForTarget(target);
            SyncHediffsForOverseer(overseer);
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

            SyncHediffForTarget(target);
            SyncHediffsForOverseer(overseer);
            SyncReplacedOverseerIfNeeded(replacedOverseer, overseer);
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
            RemoveAllCommandFocusHediffs(target);
            RemoveSpecializationRecordsForTarget(target);

            if (overseer != null && !overseer.Destroyed)
            {
                SyncHediffsForOverseer(overseer);
            }
        }

        public void ClearOverseer(Pawn? overseer)
        {
            if (overseer == null)
            {
                return;
            }

            if (!recordsByOverseer.TryGetValue(overseer, out List<DataProcessingAllocationRecord>? overseerRecords))
            {
                RemoveHediff(overseer, DataProcessingAllocationUtility.DataStreamDistributionDef);
                RemoveSpecializationRecordsForOverseer(overseer);
                return;
            }

            List<DataProcessingAllocationRecord> toRemove =
                new List<DataProcessingAllocationRecord>(overseerRecords);
            for (int i = 0; i < toRemove.Count; i++)
            {
                DataProcessingAllocationRecord record = toRemove[i];
                Pawn? target = record.target;
                RemoveRecord(record);
                if (target != null && !target.Destroyed)
                {
                    RemoveAllCommandFocusHediffs(target);
                    RemoveSpecializationRecordsForTarget(target);
                }
            }

            RemoveHediff(overseer, DataProcessingAllocationUtility.DataStreamDistributionDef);
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
            for (int i = records.Count - 1; i >= 0; i--)
            {
                DataProcessingAllocationRecord? record = records[i];
                if (record == null
                    || record.steps <= 0
                    || !IsRecordValid(record))
                {
                    Pawn? target = record?.target;
                    Pawn? overseer = record?.overseer;
                    if (record != null)
                    {
                        RemoveRecord(record);
                    }

                    if (target != null && !target.Destroyed)
                    {
                        RemoveAllCommandFocusHediffs(target);
                    }

                    if (overseer != null && !overseer.Destroyed)
                    {
                        SyncHediffsForOverseer(overseer);
                    }
                }
            }

            CleanupInvalidPinRecords();
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

            for (int i = 0; i < toRemove.Count; i++)
            {
                DataProcessingAllocationRecord record = toRemove[i];
                try
                {
                    RemoveRecord(record);
                    if (record.target != null && !record.target.Destroyed)
                    {
                        RemoveAllCommandFocusHediffs(record.target);
                        RemoveSpecializationRecordsForTarget(record.target);
                    }
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

            CollectOverseersNeedingDataStreamResync(affected);
            foreach (Pawn pawn in affected)
            {
                try
                {
                    if (pawn != null && !pawn.Destroyed)
                    {
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

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                records ??= new List<DataProcessingAllocationRecord>();
                pinRecords ??= new List<DataProcessingAllocationPinRecord>();
                specializationRecords ??= new List<DataProcessingSpecializationRecord>();
                records.RemoveAll(record => record == null || record.steps <= 0);
                specializationRecords.RemoveAll(record => record == null);
                RebuildCaches();
                CleanupInvalidRecords();
                CleanupInvalidSpecializationRecords();
            }
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();

            protectionTickCounter++;
            if (protectionTickCounter < 600)
            {
                return;
            }

            protectionTickCounter = 0;
            RunConsciousnessProtection();
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            CleanupInvalidRecords();
            CleanupInvalidSpecializationRecords();
            RebuildCaches();
            RestoreCommandFocusHediffsAfterLoad();
        }

        /// <summary>
        /// 仅在载入存档流程中调用：把旧 MAP_CommandFocus 统一转换为当前特化对应 Def，
        /// 并保证每个目标至多存在一个指令聚焦类健康状态。
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
                    ReplaceCommandFocusHediffForTarget(target, specialization);
                }

                if (record.overseer != null)
                {
                    overseers.Add(record.overseer);
                }
            }

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
                RemoveRecord(existing);
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
        /// 仅用于手动切换特化与载入恢复：移除全部指令聚焦类 Hediff，再按新特化重建一个。
        /// </summary>
        private void ReplaceCommandFocusHediffForTarget(
            Pawn target,
            DataProcessingSpecialization specialization)
        {
            RemoveAllCommandFocusHediffs(target);

            int steps = GetStepsForTarget(target);
            if (steps <= 0)
            {
                return;
            }

            HediffDef? def = DataProcessingAllocationUtility.GetCommandFocusDef(specialization);
            if (def == null)
            {
                return;
            }

            Hediff hediff = target.health.GetOrAddHediff(def);
            hediff.Severity = steps * DataProcessingAllocationUtility.StepPercent;
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

            foreach (Pawn target in affectedTargets)
            {
                SyncHediffForTarget(target);
            }

            SyncHediffsForOverseer(overseer);
        }
    }
}
