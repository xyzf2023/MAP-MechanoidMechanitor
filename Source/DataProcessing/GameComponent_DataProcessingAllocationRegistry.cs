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
            if (target == null)
            {
                return 0;
            }

            return recordByTarget.TryGetValue(target, out DataProcessingAllocationRecord? record)
                ? Mathf.Max(0, record.steps)
                : 0;
        }

        public int GetStepsForOverseerTarget(Pawn? overseer, Pawn? target)
        {
            if (overseer == null || target == null)
            {
                return 0;
            }

            DataProcessingAllocationRecord? record = FindRecord(overseer, target);
            return record != null ? Mathf.Max(0, record.steps) : 0;
        }

        public int GetTotalStepsForOverseer(Pawn? overseer)
        {
            if (overseer == null
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

        public bool TryAddStep(Pawn? overseer, Pawn? target)
        {
            if (!DataProcessingAllocationUtility.IsValidAllocationPair(overseer, target)
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
            return FindPinRecord(overseer, target) != null;
        }

        public int GetPinOrder(Pawn? overseer, Pawn? target)
        {
            DataProcessingAllocationPinRecord? pin = FindPinRecord(overseer, target);
            return pin != null ? pin.pinOrder : int.MaxValue;
        }

        public bool TryPinTarget(Pawn? overseer, Pawn? target)
        {
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

            IReadOnlyList<Pawn> mechanitors =
                GameComponent_MechanoidMechanitorRegistry.CurrentRegisteredMechanitors;
            for (int i = 0; i < mechanitors.Count; i++)
            {
                Pawn overseer = mechanitors[i];
                if (overseer == null || overseer.Destroyed)
                {
                    continue;
                }

                CleanupInvalidRecords();
                if (GetTotalStepsForOverseer(overseer) <= 0)
                {
                    continue;
                }

                while (DataProcessingAllocationUtility.GetCurrentConsciousness(overseer)
                    < DataProcessingAllocationUtility.MinReservedConsciousness)
                {
                    DataProcessingAllocationRecord? largest = FindLargestStepRecordForOverseer(overseer);
                    if (largest == null || largest.target == null)
                    {
                        break;
                    }

                    TryRemoveStep(overseer, largest.target);
                    if (GetTotalStepsForOverseer(overseer) <= 0)
                    {
                        break;
                    }
                }
            }
        }

        private DataProcessingAllocationRecord? FindLargestStepRecordForOverseer(Pawn overseer)
        {
            if (!recordsByOverseer.TryGetValue(overseer, out List<DataProcessingAllocationRecord>? overseerRecords)
                || overseerRecords.Count == 0)
            {
                return null;
            }

            DataProcessingAllocationRecord? largest = null;
            for (int i = 0; i < overseerRecords.Count; i++)
            {
                DataProcessingAllocationRecord record = overseerRecords[i];
                if (record.steps <= 0)
                {
                    continue;
                }

                if (largest == null || record.steps > largest.steps)
                {
                    largest = record;
                }
            }

            return largest;
        }
    }
}
