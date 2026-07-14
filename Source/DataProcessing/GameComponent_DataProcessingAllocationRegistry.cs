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

        public bool TryAddStep(Pawn? overseer, Pawn? target)
        {
            if (!ResearchFeatureUnlockUtility.IsDataProcessingAllocationUnlocked()
                || !DataProcessingAllocationUtility.IsValidAllocationPair(overseer, target)
                || !DataProcessingAllocationUtility.CanAddStep(overseer))
            {
                return false;
            }

            DataProcessingAllocationRecord? record = FindRecord(overseer!, target!);
            if (record == null)
            {
                record = new DataProcessingAllocationRecord(overseer!, target!, 1);
                AddRecord(record);
            }
            else
            {
                record.steps++;
            }

            SyncHediffForTarget(target!);
            SyncHediffsForOverseer(overseer!);
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

            SyncHediffForTarget(target);
            SyncHediffsForOverseer(overseer);
        }

        public void ClearTarget(Pawn? target)
        {
            if (target == null)
            {
                return;
            }

            if (!recordByTarget.TryGetValue(target, out DataProcessingAllocationRecord? record))
            {
                RemoveHediff(target, DataProcessingAllocationUtility.CommandFocusDef);
                return;
            }

            Pawn? overseer = record.overseer;
            RemoveRecord(record);
            RemoveHediff(target, DataProcessingAllocationUtility.CommandFocusDef);

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
                    RemoveHediff(target, DataProcessingAllocationUtility.CommandFocusDef);
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
                        RemoveHediff(target, DataProcessingAllocationUtility.CommandFocusDef);
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

            HediffDef? def = DataProcessingAllocationUtility.CommandFocusDef;
            if (def == null)
            {
                return;
            }

            int steps = GetStepsForTarget(target);
            if (steps <= 0)
            {
                RemoveHediff(target, def);
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
        /// 数据处理分配科研关闭时清空全部记录、顶置与相关健康状态。
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

            records.Clear();
            recordsByOverseer.Clear();
            recordByTarget.Clear();
            pinRecords.Clear();
            nextPinOrder = 0;

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
                        RemoveHediff(
                            overseer,
                            DataProcessingAllocationUtility.CommandFocusDef);
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
                        RemoveHediff(
                            target,
                            DataProcessingAllocationUtility.CommandFocusDef);
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
        /// 自我指令聚焦科研关闭时，仅清除 overseer==target 的自身分配。
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
                        RemoveHediff(
                            record.target,
                            DataProcessingAllocationUtility.CommandFocusDef);
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
            HediffDef? focusDef = DataProcessingAllocationUtility.CommandFocusDef;
            if (streamDef == null && focusDef == null)
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
                    if (streamDef != null)
                    {
                        RemoveHediff(pawn, streamDef);
                    }

                    if (focusDef != null)
                    {
                        RemoveHediff(pawn, focusDef);
                    }
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
        /// 清理“已无任何分配记录却仍残留 CommandFocus”的状态，不误删合法下属。
        /// </summary>
        private bool ScrubResidualOrphanCommandFocusHediffs()
        {
            if (Current.Game == null || Find.World == null)
            {
                return true;
            }

            HediffDef? focusDef = DataProcessingAllocationUtility.CommandFocusDef;
            if (focusDef == null)
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
                    || !pawn.health.hediffSet.HasHediff(focusDef))
                {
                    continue;
                }

                if (HasAnyAllocationRecordForTarget(pawn))
                {
                    continue;
                }

                try
                {
                    RemoveHediff(pawn, focusDef);
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

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref records, "records", LookMode.Deep);
            Scribe_Collections.Look(ref pinRecords, "pinRecords", LookMode.Deep);
            Scribe_Values.Look(ref nextPinOrder, "nextPinOrder", 0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                records ??= new List<DataProcessingAllocationRecord>();
                pinRecords ??= new List<DataProcessingAllocationPinRecord>();
                records.RemoveAll(record => record == null || record.steps <= 0);
                RebuildCaches();
                CleanupInvalidRecords();
                SyncAllHediffs();
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
            SyncAllHediffs();
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

        private void AddRecord(DataProcessingAllocationRecord record)
        {
            if (record.overseer == null || record.target == null)
            {
                return;
            }

            if (recordByTarget.TryGetValue(record.target, out DataProcessingAllocationRecord? existing))
            {
                records.Remove(existing);
                RemoveFromOverseerCache(existing);
            }

            records.Add(record);
            AddToCaches(record);
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

            // 全表清理每轮只执行一次；即使没有机械师也要清理无效记录。
            CleanupInvalidRecords();

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
