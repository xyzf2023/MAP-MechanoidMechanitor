using System.Collections.Generic;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed partial class GameComponent_DataProcessingAllocationRegistry
    {
        /// <summary>监管解除事件的单目标校准；保留特化、顶置、动态配置及合法自身分配。</summary>
        internal void RefreshAfterExternalOverseerChange(Pawn target, HashSet<Pawn> formerOverseers)
        {
            if (recordByTarget.TryGetValue(target, out DataProcessingAllocationRecord? record)
                && record.overseer != target && record.overseer != null
                && formerOverseers.Contains(record.overseer) && !IsRecordValid(record))
            {
                // 目标也可能正在向下属分配算力；先按即将失去的正面意识回收超额分配。
                PrepareForExternalConsciousnessLoss(target,
                    record.steps * DataProcessingAllocationUtility.StepPercent);
                if (recordByTarget.TryGetValue(target, out DataProcessingAllocationRecord? current)
                    && ReferenceEquals(current, record) && !IsRecordValid(record))
                    RemoveRecord(record);
            }

            // 即使上次同步中途失败、记录已删除，重试也必须先降低原提供者的负面消耗。
            foreach (Pawn provider in new List<Pawn>(formerOverseers))
                if (provider != null && !provider.Destroyed) SyncHediffsForOverseer(provider);
            SyncHediffForTarget(target);

            cachedDynamicEvaluationByTarget.Remove(target);
            nextDynamicCheckTickByTarget.Remove(target);
        }
    }
}
