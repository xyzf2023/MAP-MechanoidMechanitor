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
            if (AutonomousMechUtility.IsAutonomousMech(subject))
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

            // 循环监管预检查：在任何状态写入前拒绝直接双向控制（A 控制 B 时拒绝 B 控制 A）。
            Pawn_MechanitorTracker? subjectTracker = subject.mechanitor;
            bool subjectControlsOverseer =
                subjectTracker?.GetControlGroup(overseer) != null
                || MAPOverseerRelationDirectionUtility.IsActualOverseerOf(
                    subject,
                    overseer);
            if (subjectControlsOverseer)
            {
                Log.Error(
                    "[MAP-机械族机械师] 拒绝建立循环监管关系：" +
                    $"overseer={overseer.LabelShort}（{overseer.ThingID}），" +
                    $"subject={subject.LabelShort}（{subject.ThingID}），" +
                    "subjectControlsOverseer=true。");
                return false;
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
        /// 低层写入：确保 overseer 与 subject 之间存在 Overseer 关系且 subject 被分配到
        /// overseer 的控制组，并刷新带宽。不删除 subject 的其他实际监管者。
        /// 用于回滚恢复旧监管者，避免递归调用 TryAssignActualOverseer。
        /// 返回 true 表示关系与控制组均已成功建立且方向校验通过；false 时 failureReason 给出具体原因。
        /// </summary>
        private static bool TryEnsureOverseerRelationAndGroup(
            Pawn? overseer,
            Pawn? subject,
            out string failureReason)
        {
            failureReason = string.Empty;

            if (overseer == null || subject == null || ReferenceEquals(overseer, subject))
            {
                failureReason = "overseer 或 subject 为空或与自身相等。";
                return false;
            }

            if (overseer.Dead || overseer.Destroyed || overseer.Discarded
                || subject.Dead || subject.Destroyed || subject.Discarded)
            {
                failureReason =
                    $"overseer 或 subject 已死亡/销毁：" +
                    $"overseer={overseer.LabelShort}（{overseer.ThingID}），" +
                    $"subject={subject.LabelShort}（{subject.ThingID}）。";
                return false;
            }

            if (!subject.RaceProps.IsMechanoid || subject.OverseerSubject == null)
            {
                failureReason =
                    $"subject 不是有效机械体（需机械族且有 OverseerSubject）：" +
                    $"subject={subject.LabelShort}（{subject.ThingID}）。";
                return false;
            }

            // 无需外部监管者的 MAP 节点（如正义）不应获得外部监管者。
            if (AutonomousMechUtility.IsAutonomousMech(subject))
            {
                failureReason =
                    $"subject 不需要外部监管者：" +
                    $"subject={subject.LabelShort}（{subject.ThingID}）。";
                return false;
            }

            // 合法 MAP 机械师节点控制者先补全 Tracker；普通人类机械师不无条件初始化。
            if (MAPMechanitorNodeUtility.IsMechanitorNodeController(overseer))
            {
                MAPMechanitorNodeLifecycleUtility.EnsureBasicTrackers(overseer);
            }

            if (!MechanitorUtility.IsMechanitor(overseer))
            {
                failureReason =
                    $"overseer 不是有效机械师：" +
                    $"overseer={overseer.LabelShort}（{overseer.ThingID}）。";
                return false;
            }

            Pawn_MechanitorTracker? tracker = overseer.mechanitor;
            if (tracker == null)
            {
                failureReason =
                    $"overseer 缺少 mechanitor Tracker：" +
                    $"overseer={overseer.LabelShort}（{overseer.ThingID}）。";
                return false;
            }

            if (tracker.controlGroups == null)
            {
                failureReason =
                    $"overseer 控制组列表为 null：" +
                    $"overseer={overseer.LabelShort}（{overseer.ThingID}）。";
                return false;
            }

            // 循环监管预检查（直接双向）：subject 不能反过来控制 overseer。
            if (subject.mechanitor?.GetControlGroup(overseer) != null
                || MAPOverseerRelationDirectionUtility.IsActualOverseerOf(subject, overseer))
            {
                failureReason =
                    $"拒绝建立循环监管关系：" +
                    $"overseer={overseer.LabelShort}（{overseer.ThingID}），" +
                    $"subject={subject.LabelShort}（{subject.ThingID}），" +
                    "subjectControlsOverseer=true。";
                return false;
            }

            if (!tracker.CanOverseeSubject(subject))
            {
                failureReason =
                    $"overseer 无法承担 subject 单体带宽成本：" +
                    $"overseer={overseer.LabelShort}（{overseer.ThingID}），" +
                    $"subject={subject.LabelShort}（{subject.ThingID}）。";
                return false;
            }

            // 执行前快照。
            bool relationExisted =
                overseer.relations?.DirectRelationExists(
                    PawnRelationDefOf.Overseer, subject) == true;
            bool controlGroupExisted = tracker.GetControlGroup(subject) != null;

            overseer.relations ??= new Pawn_RelationsTracker(overseer);
            subject.relations ??= new Pawn_RelationsTracker(subject);

            bool relationAddedByThisCall = false;
            if (!relationExisted)
            {
                overseer.relations.AddDirectRelation(PawnRelationDefOf.Overseer, subject);
                relationAddedByThisCall = true;
            }

            // 原版 RelationWorker 可能在 OnRelationCreated 中已分配控制组；
            // 仅当仍为空时显式分配。AssignPawnControlGroup 无返回值，必须随后验证。
            if (tracker.GetControlGroup(subject) == null && tracker.CanOverseeSubject(subject))
            {
                tracker.AssignPawnControlGroup(subject);
            }

            MechanitorControlGroup? group = tracker.GetControlGroup(subject);
            if (group == null)
            {
                // 控制组分配失败：清理本次新增关系（及控制组），返回失败。
                if (relationAddedByThisCall)
                {
                    overseer.relations.TryRemoveDirectRelation(
                        PawnRelationDefOf.Overseer, subject);
                }

                tracker.UnassignPawnFromAnyControlGroup(subject);
                tracker.Notify_BandwidthChanged();
                failureReason =
                    $"控制组分配失败（原版无控制组可分配）：" +
                    $"overseer={overseer.LabelShort}（{overseer.ThingID}），" +
                    $"subject={subject.LabelShort}（{subject.ThingID}）。";
                return false;
            }

            // 最终验证：关系存在、控制组存在、方向有效。不要求 ControlledPawns 包含 subject。
            tracker.Notify_BandwidthChanged();

            bool relationExists =
                overseer.relations.DirectRelationExists(
                    PawnRelationDefOf.Overseer, subject);
            bool controlGroupExists = tracker.GetControlGroup(subject) != null;
            bool directionValid =
                MAPOverseerRelationDirectionUtility.IsActualOverseerOf(overseer, subject);

            if (!relationExists || !controlGroupExists || !directionValid)
            {
                if (relationAddedByThisCall)
                {
                    overseer.relations.TryRemoveDirectRelation(
                        PawnRelationDefOf.Overseer, subject);
                }

                if (!controlGroupExisted)
                {
                    tracker.UnassignPawnFromAnyControlGroup(subject);
                }

                tracker.Notify_BandwidthChanged();

                List<Pawn>? controlledPawns = tracker.ControlledPawns;
                failureReason =
                    $"最终验证失败：overseer={overseer.LabelShort}（{overseer.ThingID}），" +
                    $"subject={subject.LabelShort}（{subject.ThingID}），" +
                    $"relationExists={relationExists}，" +
                    $"controlGroupExists={controlGroupExists}，" +
                    $"directionValid={directionValid}，" +
                    $"controlledPawnsContainsSubject={controlledPawns != null && controlledPawns.Contains(subject)}，" +
                    $"totalBandwidth={tracker.TotalBandwidth}，" +
                    $"usedBandwidth={tracker.UsedBandwidth}。";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 回滚时尽力恢复旧监管者。依据 TryEnsureOverseerRelationAndGroup 的 bool 返回值判断
        /// 恢复是否成功；返回 false 视为恢复失败并记录 failureReason。异常仅作为最后兜底。
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
                    if (!TryEnsureOverseerRelationAndGroup(old, subject, out string failureReason))
                    {
                        Log.Error(
                            "[MAP-机械族机械师] 回滚恢复旧监管者失败：" + failureReason);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 回滚恢复旧监管者异常：" +
                        $"old={old.LabelShort}（{old.ThingID}），" +
                        $"subject={subject.LabelShort}（{subject.ThingID}）：{ex}");
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

            Pawn_MechanitorTracker? newTracker = newOverseer.mechanitor;

            // 1+2. 删除本次新增的新关系；无论关系删除是否自动移除控制组，
            //       只要本次新增过控制组，都显式移除，避免留下“关系存在但控制组不存在”的半完成状态。
            if (relationAddedByThisCall)
            {
                try
                {
                    if (newOverseer.relations != null
                        && newOverseer.relations.DirectRelationExists(
                            PawnRelationDefOf.Overseer, subject))
                    {
                        newOverseer.relations.TryRemoveDirectRelation(
                            PawnRelationDefOf.Overseer, subject);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 回滚删除新关系异常：" +
                        $"overseer={newOverseer.LabelShort}（{newOverseer.ThingID}），" +
                        $"subject={subject.LabelShort}（{subject.ThingID}）：{ex}");
                }
            }

            if (controlGroupAddedByThisCall && newTracker != null)
            {
                try
                {
                    newTracker.UnassignPawnFromAnyControlGroup(subject);
                }
                catch (Exception ex)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 回滚移除新控制组异常：" +
                        $"overseer={newOverseer.LabelShort}（{newOverseer.ThingID}），" +
                        $"subject={subject.LabelShort}（{subject.ThingID}）：{ex}");
                }
            }

            // 3. 恢复此前删除的旧监管者（内部已按 bool 结果判断并记录失败）。
            try
            {
                RestoreOldOverseersBestEffort(subject, removedOldOverseers);
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 回滚恢复旧监管者异常：" +
                    $"subject={subject.LabelShort}（{subject.ThingID}）：{ex}");
            }

            // 4. 刷新新旧监管者带宽。
            try
            {
                newTracker?.Notify_BandwidthChanged();
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 回滚刷新新监管者带宽异常：" +
                    $"overseer={newOverseer.LabelShort}（{newOverseer.ThingID}）：{ex}");
            }

            for (int i = 0; i < removedOldOverseers.Count; i++)
            {
                Pawn? old = removedOldOverseers[i];
                try
                {
                    old?.mechanitor?.Notify_BandwidthChanged();
                }
                catch (Exception ex)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 回滚刷新旧监管者带宽异常：" +
                        $"old={old?.LabelShort}（{old?.ThingID}）：{ex}");
                }
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
