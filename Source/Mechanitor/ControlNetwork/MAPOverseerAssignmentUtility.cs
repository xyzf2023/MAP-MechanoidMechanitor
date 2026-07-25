using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 统一的 MAP 监管者（实际 Overseer）关系写入入口。
    /// 剧本开局、DEV、骇入、机械重构、意识转移、升格回滚等所有
    /// “为某机械体指定新监管者”的业务逻辑都应调用本工具，
    /// 以避免再次出现“只 AddDirectRelation、不刷新带宽/控制组”
    /// 导致新存档开局监管状态不同步的问题。
    ///
    /// 流程：基础验证 → 清除旧实际监管者 → 确保 Overseer 关系存在
    /// → 确保控制组包含 subject → 刷新实际控制名单 → 方向校验。
    /// 本工具不会在 getter 中调用，也不会每 Tick 刷新。
    /// </summary>
    public static class MAPOverseerAssignmentUtility
    {
        public static bool TryAssignActualOverseer(
            Pawn? overseer,
            Pawn? subject,
            bool removeExistingActualOverseers = true)
        {
            if (!ModsConfig.BiotechActive)
            {
                return false;
            }

            if (overseer == null || subject == null || overseer == subject)
            {
                return false;
            }

            if (overseer.Dead || overseer.Destroyed || overseer.Discarded
                || subject.Dead || subject.Destroyed || subject.Discarded)
            {
                return false;
            }

            if (!subject.RaceProps.IsMechanoid || subject.OverseerSubject == null)
            {
                return false;
            }

            // 四、MAP 机械师节点控制者必须先补全 Tracker，否则 IsMechanitor 会误判失败。
            // 普通原版人类机械师不应无条件创建 MAP Tracker。
            if (MAPMechanitorNodeUtility.IsMechanitorNodeController(overseer))
            {
                MAPMechanitorNodeLifecycleUtility.EnsureBasicTrackers(overseer);
            }

            if (!MechanitorUtility.IsMechanitor(overseer))
            {
                return false;
            }

            Pawn_MechanitorTracker? tracker = overseer.mechanitor;
            if (tracker == null)
            {
                return false;
            }

            // 无需外部监管者的 MAP 节点（如正义）不应获得外部监管者。
            if (MAPMechanitorNodeUtility.HasNode(subject)
                && !MAPMechanitorNodeUtility.RequiresExternalOverseer(subject))
            {
                return false;
            }

            // 单体带宽成本检查；不代表控制组一定存在，也不代表 AssignPawnControlGroup 一定成功。
            if (!tracker.CanOverseeSubject(subject))
            {
                return false;
            }

            if (tracker.controlGroups == null)
            {
                return false;
            }

            // 原版控制组刷新逻辑：当控制组数量为 0 时，AssignPawnControlGroup 内部会触发
            // Notify_ControlGroupAmountMayChanged 重新填充。这里在写入前预填充容量，
            // 确保后续分配步骤能拿到有效控制组。
            if (tracker.controlGroups.Count == 0)
            {
                EnsureControlGroupsCapacity(tracker);
            }

            subject.relations ??= new Pawn_RelationsTracker(subject);
            overseer.relations ??= new Pawn_RelationsTracker(overseer);

            // 1. 写入前快照：记录当前所有实际监管者（用于失败时回滚）。
            List<Pawn> oldActualOverseers = new List<Pawn>();
            MAPOverseerRelationDirectionUtility.CollectActualOverseers(subject, oldActualOverseers);

            bool relationExisted =
                overseer.relations.DirectRelationExists(PawnRelationDefOf.Overseer, subject);
            bool controlGroupExisted = tracker.GetControlGroup(subject) != null;

            // 2. 预检查已在上方完成（合法性、Tracker、带宽成本、控制组列表）。
            //    预检查通过后，才开始修改状态。

            // 3. 删除旧实际监管者（仅当 removeExistingActualOverseers）。
            List<Pawn> removedOldOverseers = new List<Pawn>();
            if (removeExistingActualOverseers)
            {
                for (int i = 0; i < oldActualOverseers.Count; i++)
                {
                    Pawn? oldOver = oldActualOverseers[i];
                    if (oldOver == null || oldOver == overseer)
                    {
                        continue;
                    }

                    if (oldOver.relations != null
                        && oldOver.relations.DirectRelationExists(
                            PawnRelationDefOf.Overseer, subject))
                    {
                        oldOver.relations.TryRemoveDirectRelation(
                            PawnRelationDefOf.Overseer, subject);
                        removedOldOverseers.Add(oldOver);
                    }

                    oldOver.mechanitor?.Notify_BandwidthChanged();
                }
            }

            // 4. 确保新关系存在（不直接写入 DirectRelations 列表，使用原版入口）。
            bool relationAddedByThisCall = false;
            if (!relationExisted)
            {
                overseer.relations.AddDirectRelation(PawnRelationDefOf.Overseer, subject);
                relationAddedByThisCall = true;
            }

            // 5. 确保控制组实际分配成功。
            bool controlGroupAddedByThisCall = false;
            if (!controlGroupExisted)
            {
                if (tracker.GetControlGroup(subject) == null
                    && tracker.CanOverseeSubject(subject))
                {
                    tracker.AssignPawnControlGroup(subject);
                }

                if (tracker.GetControlGroup(subject) == null)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 为 " +
                        $"{subject.LabelShort}（{subject.ThingID}）分配监管者 " +
                        $"{overseer.LabelShort}（{overseer.ThingID}）失败：控制组分配失败。\n" +
                        BuildVerificationLog(overseer, subject, relationExisted, controlGroupExisted, tracker));
                    RollbackFailedAssignment(
                        overseer,
                        subject,
                        relationAddedByThisCall,
                        controlGroupAddedByThisCall,
                        removedOldOverseers);
                    return false;
                }

                controlGroupAddedByThisCall = true;
            }

            // 关系和控制组完成后刷新实际控制名单（带宽）。
            tracker.Notify_BandwidthChanged();

            // 6. 最终方向校验：关系存在且控制组包含 subject 才视为分配成功。
            if (!MAPOverseerRelationDirectionUtility.IsActualOverseerOf(overseer, subject))
            {
                Log.Error(BuildVerificationLog(overseer, subject, relationExisted, controlGroupExisted, tracker));
                RollbackFailedAssignment(
                    overseer,
                    subject,
                    relationAddedByThisCall,
                    controlGroupAddedByThisCall,
                    removedOldOverseers);
                return false;
            }

            return true;
        }

        /// <summary>
        /// 按原版逻辑刷新控制组容量（等价于 Pawn_MechanitorTracker.Notify_ControlGroupAmountMayChanged）。
        /// 当控制组列表为空时调用，确保后续 AssignPawnControlGroup 能拿到有效组。
        /// </summary>
        private static void EnsureControlGroupsCapacity(Pawn_MechanitorTracker? tracker)
        {
            if (tracker == null || tracker.controlGroups == null)
            {
                return;
            }

            int total = tracker.TotalAvailableControlGroups;
            while (tracker.controlGroups.Count < total)
            {
                tracker.controlGroups.Add(new MechanitorControlGroup(tracker));
            }
        }

        /// <summary>
        /// 低层写入：确保 overseer 与 subject 之间存在 Overseer 关系且 subject 被分配到
        /// overseer 的控制组，并刷新带宽。不删除 subject 的其他实际监管者。
        /// 用于回滚恢复旧监管者，避免递归调用 TryAssignActualOverseer。
        /// </summary>
        private static void EnsureOverseerRelationAndGroup(Pawn? overseer, Pawn? subject)
        {
            if (overseer == null || subject == null)
            {
                return;
            }

            overseer.relations ??= new Pawn_RelationsTracker(overseer);

            if (!overseer.relations.DirectRelationExists(PawnRelationDefOf.Overseer, subject))
            {
                overseer.relations.AddDirectRelation(PawnRelationDefOf.Overseer, subject);
            }

            Pawn_MechanitorTracker? tracker = overseer.mechanitor;
            if (tracker != null && tracker.GetControlGroup(subject) == null)
            {
                if (tracker.CanOverseeSubject(subject))
                {
                    tracker.AssignPawnControlGroup(subject);
                }

                // AssignPawnControlGroup 无返回值，原版在控制组不足时可能只记录警告并直接返回。
                // 这里立即验证是否真的分配成功；若仍为空，记录实际失败（不抛异常覆盖原始失败）。
                if (tracker.GetControlGroup(subject) == null)
                {
                    Log.Warning(
                        "[MAP-机械族机械师] 恢复旧监管者 " +
                        $"{overseer.LabelShort}（{overseer.ThingID}）对 " +
                        $"{subject.LabelShort}（{subject.ThingID}）的关系已恢复，但控制组分配失败" +
                        "（可能单体带宽成本不通过）。");
                }
            }

            tracker?.Notify_BandwidthChanged();
        }

        /// <summary>
        /// 回滚时尽力恢复旧监管者，不抛出异常覆盖原始失败原因。
        /// </summary>
        private static void RestoreOldOverseersBestEffort(Pawn? subject, List<Pawn> oldOverseers)
        {
            if (subject == null)
            {
                return;
            }

            for (int i = 0; i < oldOverseers.Count; i++)
            {
                Pawn? old = oldOverseers[i];
                if (old == null || old.Dead || old.Destroyed || old.Discarded)
                {
                    continue;
                }

                try
                {
                    EnsureOverseerRelationAndGroup(old, subject);
                }
                catch (Exception ex)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 回滚恢复旧监管者 " +
                        $"{old.LabelShort}（{old.ThingID}）对 " +
                        $"{subject.LabelShort}（{subject.ThingID}）失败：{ex}");
                }
            }
        }

        /// <summary>
        /// 分配失败时的完整回滚：撤销本次新增的关系与控制组，并恢复此前删除的旧监管者。
        /// </summary>
        private static void RollbackFailedAssignment(
            Pawn? newOverseer,
            Pawn? subject,
            bool relationAddedByThisCall,
            bool controlGroupAddedByThisCall,
            List<Pawn> removedOldOverseers)
        {
            if (newOverseer == null || subject == null)
            {
                return;
            }

            // 1. 删除本次新增的新关系。
            if (relationAddedByThisCall
                && newOverseer.relations != null
                && newOverseer.relations.DirectRelationExists(
                    PawnRelationDefOf.Overseer, subject))
            {
                newOverseer.relations.TryRemoveDirectRelation(
                    PawnRelationDefOf.Overseer, subject);
            }

            // 2. 移除本次新增的新控制组归属，避免留下“关系存在但控制组不存在”的半完成状态。
            Pawn_MechanitorTracker? newTracker = newOverseer.mechanitor;
            if (controlGroupAddedByThisCall && newTracker != null)
            {
                if (newTracker.GetControlGroup(subject) != null)
                {
                    newTracker.UnassignPawnFromAnyControlGroup(subject);
                }
            }

            // 3. 恢复此前删除的旧监管者。
            RestoreOldOverseersBestEffort(subject, removedOldOverseers);

            // 4. 刷新新旧监管者带宽。
            newTracker?.Notify_BandwidthChanged();
            for (int i = 0; i < removedOldOverseers.Count; i++)
            {
                removedOldOverseers[i].mechanitor?.Notify_BandwidthChanged();
            }
        }

        private static string BuildVerificationLog(
            Pawn overseer,
            Pawn subject,
            bool relationExisted,
            bool controlGroupExisted,
            Pawn_MechanitorTracker tracker)
        {
            List<Pawn>? controlledPawns = tracker.ControlledPawns;
            bool relationExists =
                overseer.relations?.DirectRelationExists(PawnRelationDefOf.Overseer, subject) == true;
            return
                "[MAP-机械族机械师] 监管者分配方向校验失败：" +
                $"overseer={overseer.LabelShort}({overseer.ThingID}), " +
                $"subject={subject.LabelShort}({subject.ThingID}), " +
                $"relationExists={relationExists}, " +
                $"controlGroupExists={tracker.GetControlGroup(subject) != null}, " +
                $"controlledPawnsNull={controlledPawns == null}, " +
                $"controlledPawnsContainsSubject={controlledPawns != null && controlledPawns.Contains(subject)}, " +
                $"totalBandwidth={tracker.TotalBandwidth}, " +
                $"usedBandwidth={tracker.UsedBandwidth}; " +
                $"relationExisted={relationExisted}, controlGroupExisted={controlGroupExisted}。";
        }
    }
}
