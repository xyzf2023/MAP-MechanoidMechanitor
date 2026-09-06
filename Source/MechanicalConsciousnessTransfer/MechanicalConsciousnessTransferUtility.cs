using System;
using System.Collections.Generic;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    internal enum MechanicalConsciousnessTransferContext
    {
        Voluntary,
        Emergency
    }

    public static class MechanicalConsciousnessTransferUtility
    {
        // “芯片带宽加成 + 监管关系”迁移/回滚共用同一实现：既被研究前的完整意识转移使用，
        // 也被研究后的“控制权交接”（MechanicalControlHandoffUtility）复用，避免数值与语义分叉。
        internal readonly struct OverseerRelationSnapshot
        {
            public readonly Pawn Mech;
            public readonly bool HadSourceRelation;
            public readonly bool HadTargetRelation;

            public OverseerRelationSnapshot(
                Pawn mech,
                bool hadSourceRelation,
                bool hadTargetRelation)
            {
                Mech = mech;
                HadSourceRelation = hadSourceRelation;
                HadTargetRelation = hadTargetRelation;
            }
        }

        public static bool CanTransferMechanicalConsciousness(Pawn? source, Pawn? target)
        {
            return PassesTransferEligibility(source, target, out _, out _);
        }

        public static bool CanVoluntarilyTransferMechanicalConsciousness(Pawn? source, Pawn? target)
        {
            // 轨道数据网络研究完成后不再执行任何“意识转移”，主动路径只能走控制权交接。
            // 若研究状态在意识转移 Job 执行期间变化，此处会使旧 Job 立即失效并安全终止。
            return ResearchFeatureUnlockUtility.IsMechanicalConsciousnessTransferUnlocked()
                && !MechanicalControlHandoffUtility.IsActive()
                && !EmergencyMechanicalConsciousnessTransferUtility
                    .HasEmergencyConsciousnessTransferHediff(source)
                && CanTransferMechanicalConsciousness(source, target);
        }

        public static bool TryVoluntarilyTransferMechanicalConsciousness(Pawn? source, Pawn? target)
        {
            if (!CanVoluntarilyTransferMechanicalConsciousness(source, target))
            {
                return false;
            }

            return TryTransferMechanicalConsciousness(source, target);
        }

        public static bool TryTransferMechanicalConsciousness(Pawn? source, Pawn? target)
        {
            return TryTransferMechanicalConsciousness(
                source,
                target,
                MechanicalConsciousnessTransferContext.Voluntary);
        }

        internal static bool TryTransferMechanicalConsciousness(
            Pawn? source,
            Pawn? target,
            MechanicalConsciousnessTransferContext context)
        {
            if (!PassesTransferEligibility(source, target, out Pawn resolvedSource, out Pawn resolvedTarget)
                || !TryEnsureMechanitorTransferComponents(resolvedSource, resolvedTarget))
            {
                return false;
            }

            if (!GameComponent_MechanoidMechanitorRegistry.TryGetMechanitorRecord(
                    resolvedSource,
                    out MechanoidMechanitorRecord? sourceRecord)
                || sourceRecord == null
                || !GameComponent_MechanoidMechanitorRegistry.TryGetMechanitorRecord(
                    resolvedTarget,
                    out MechanoidMechanitorRecord? targetRecord)
                || targetRecord == null)
            {
                return false;
            }

            int sourceChipBandwidthBonus = sourceRecord.ChipBandwidthBonus;
            int targetChipBandwidthBonus = targetRecord.ChipBandwidthBonus;
            Dictionary<SkillDef, int> targetSkillSnapshot =
                CaptureSkillLevels(resolvedTarget);
            List<Pawn> overseenSnapshot = CaptureOverseenPawns(resolvedSource);
            Pawn? hostBeforeTransfer =
                GameComponent_MechanoidMechanitorRegistry.CurrentMechanicalConsciousnessHost;
            List<OverseerRelationSnapshot> overseerSnapshots =
                new List<OverseerRelationSnapshot>();
            List<ManagedResearchAbilityTransferSnapshot> abilitySnapshots =
                ManagedResearchAbilityTransferUtility.CaptureSnapshots(
                    resolvedSource,
                    resolvedTarget);
            bool transactionCommitted = false;
            bool rollbackAttempted = false;
            List<string> rollbackFailures = new List<string>();

            try
            {
                MergeTargetSkillsFromSource(resolvedSource, resolvedTarget);
                ApplyChipBandwidthTransfer(
                    sourceRecord,
                    targetRecord,
                    resolvedTarget,
                    sourceChipBandwidthBonus,
                    targetChipBandwidthBonus);
                TransferOverseerRelations(
                    resolvedSource,
                    resolvedTarget,
                    overseenSnapshot,
                    overseerSnapshots);

                if (!GameComponent_MechanoidMechanitorRegistry.TryReplaceMechanicalConsciousnessHost(
                        resolvedSource,
                        resolvedTarget))
                {
                    rollbackAttempted = true;
                    RollbackTransferBestEffort(
                        resolvedSource,
                        resolvedTarget,
                        sourceRecord,
                        targetRecord,
                        targetSkillSnapshot,
                        sourceChipBandwidthBonus,
                        targetChipBandwidthBonus,
                        overseerSnapshots,
                        rollbackFailures);
                    if (rollbackFailures.Count > 0)
                    {
                        LogTransferFailureWithRollback(
                            resolvedSource,
                            resolvedTarget,
                            hostBeforeTransfer,
                            originalException: null,
                            hostReplaceRejected: true,
                            rollbackAttempted: true,
                            rollbackFailures);
                    }

                    return false;
                }

                transactionCommitted = true;
                ApplyManagedAbilityTransferBestEffort(
                    resolvedSource,
                    resolvedTarget,
                    abilitySnapshots);
                FinalizeSuccessfulTransferBestEffort(
                    resolvedSource,
                    resolvedTarget,
                    hostBeforeTransfer,
                    context);
                return true;
            }
            catch (Exception ex)
            {
                if (!transactionCommitted)
                {
                    if (!rollbackAttempted)
                    {
                        rollbackAttempted = true;
                        try
                        {
                            RollbackTransferBestEffort(
                                resolvedSource,
                                resolvedTarget,
                                sourceRecord,
                                targetRecord,
                                targetSkillSnapshot,
                                sourceChipBandwidthBonus,
                                targetChipBandwidthBonus,
                                overseerSnapshots,
                                rollbackFailures);
                        }
                        catch (Exception rollbackEx)
                        {
                            // BestEffort 正常不应抛出；兜底记录，仍保证不重复回滚且返回 false。
                            rollbackFailures.Add(
                                "RollbackTransferBestEffort 未捕获异常：" + rollbackEx);
                        }
                    }

                    LogTransferFailureWithRollback(
                        resolvedSource,
                        resolvedTarget,
                        hostBeforeTransfer,
                        originalException: ex,
                        hostReplaceRejected: false,
                        rollbackAttempted: rollbackAttempted,
                        rollbackFailures);
                    return false;
                }

                LogTransferException(
                    resolvedSource,
                    resolvedTarget,
                    hostBeforeTransfer,
                    ex,
                    committed: true);
                return true;
            }
        }

        private static bool PassesTransferEligibility(
            Pawn? source,
            Pawn? target,
            out Pawn resolvedSource,
            out Pawn resolvedTarget)
        {
            resolvedSource = source!;
            resolvedTarget = target!;

            if (!MechanoidMechanitorScenarioUtility.IsScenarioActive)
            {
                return false;
            }

            if (source == null || source.Dead || source.Destroyed)
            {
                return false;
            }

            if (!GameComponent_MechanoidMechanitorRegistry.IsMechanicalConsciousnessHost(source))
            {
                return false;
            }

            if (!MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(source))
            {
                return false;
            }

            if (target == null || target.Dead || target.Destroyed)
            {
                return false;
            }

            if (ReferenceEquals(source, target))
            {
                return false;
            }

            if (!target.RaceProps.IsMechanoid
                || target.Faction == null
                || !target.Faction.IsPlayerSafe())
            {
                return false;
            }

            if (!MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(target))
            {
                return false;
            }

            if (!GameComponent_MechanoidMechanitorRegistry.CanHostMechanicalConsciousness(target))
            {
                return false;
            }

            if (!GameComponent_MechanoidMechanitorRegistry.TryGetMechanitorRecord(
                    source,
                    out MechanoidMechanitorRecord? sourceRecord)
                || sourceRecord == null
                || !GameComponent_MechanoidMechanitorRegistry.TryGetMechanitorRecord(
                    target,
                    out MechanoidMechanitorRecord? targetRecord)
                || targetRecord == null)
            {
                return false;
            }

            return true;
        }

        private static bool TryEnsureMechanitorTransferComponents(
            Pawn source,
            Pawn target)
        {
            MechanoidMechanitorRoleUtility.EnsureRoleState(source);
            MechanoidMechanitorRoleUtility.EnsureRoleState(target);
            return source.relations != null
                && target.relations != null
                && source.mechanitor != null
                && target.mechanitor != null;
        }

        private static Dictionary<SkillDef, int> CaptureSkillLevels(Pawn pawn)
        {
            Dictionary<SkillDef, int> snapshot = new Dictionary<SkillDef, int>();
            List<SkillRecord>? skills = pawn.skills?.skills;
            if (skills == null)
            {
                return snapshot;
            }

            for (int i = 0; i < skills.Count; i++)
            {
                SkillRecord? record = skills[i];
                if (record?.def == null)
                {
                    continue;
                }

                snapshot[record.def] = record.Level;
            }

            return snapshot;
        }

        internal static List<Pawn> CaptureOverseenPawns(Pawn source)
        {
            if (source.mechanitor == null)
            {
                return new List<Pawn>();
            }

            return new List<Pawn>(source.mechanitor.OverseenPawns);
        }

        private static void MergeTargetSkillsFromSource(Pawn source, Pawn target)
        {
            List<SkillRecord>? sourceSkills = source.skills?.skills;
            List<SkillRecord>? targetSkills = target.skills?.skills;
            if (sourceSkills == null || targetSkills == null)
            {
                return;
            }

            for (int i = 0; i < sourceSkills.Count; i++)
            {
                SkillRecord? sourceSkill = sourceSkills[i];
                if (sourceSkill?.def == null)
                {
                    continue;
                }

                SkillRecord? targetSkill = FindSkillRecord(targetSkills, sourceSkill.def);
                if (targetSkill == null)
                {
                    continue;
                }

                int mergedLevel = Mathf.Max(sourceSkill.Level, targetSkill.Level);
                if (mergedLevel > targetSkill.Level)
                {
                    targetSkill.Level = mergedLevel;
                }
            }
        }

        private static SkillRecord? FindSkillRecord(List<SkillRecord> skills, SkillDef skillDef)
        {
            for (int i = 0; i < skills.Count; i++)
            {
                SkillRecord? record = skills[i];
                if (record != null && record.def == skillDef)
                {
                    return record;
                }
            }

            return null;
        }

        internal static void ApplyChipBandwidthTransfer(
            MechanoidMechanitorRecord sourceRecord,
            MechanoidMechanitorRecord targetRecord,
            Pawn target,
            int sourceChipBandwidthBonus,
            int targetChipBandwidthBonus)
        {
            int maxBonus = MechanoidMechanitorRecord.GetMaxChipBandwidthBonus(
                target,
                targetRecord.Origin);
            targetRecord.ChipBandwidthBonus = Mathf.Min(
                targetChipBandwidthBonus + sourceChipBandwidthBonus,
                maxBonus);
            sourceRecord.ChipBandwidthBonus = 0;
        }

        internal static void TransferOverseerRelations(
            Pawn source,
            Pawn target,
            List<Pawn> overseenSnapshot,
            List<OverseerRelationSnapshot> overseerSnapshots)
        {
            Pawn_RelationsTracker sourceRelations = source.relations!;
            Pawn_RelationsTracker targetRelations = target.relations!;

            for (int i = 0; i < overseenSnapshot.Count; i++)
            {
                Pawn mech = overseenSnapshot[i];
                if (mech == null || mech.Destroyed)
                {
                    continue;
                }

                bool hadSourceRelation = HasOverseerRelation(sourceRelations, source, mech);
                bool hadTargetRelation = HasOverseerRelation(targetRelations, target, mech);
                overseerSnapshots.Add(
                    new OverseerRelationSnapshot(mech, hadSourceRelation, hadTargetRelation));

                if (ReferenceEquals(mech, target))
                {
                    RemoveOverseerRelationIfPresent(sourceRelations, source, mech);
                    continue;
                }

                if (ReferenceEquals(mech, source))
                {
                    RemoveOverseerRelationIfPresent(sourceRelations, source, mech);
                    continue;
                }

                RemoveOverseerRelationIfPresent(sourceRelations, source, mech);
                AddOverseerRelationIfAbsent(target, mech);
            }
        }

        private static bool HasOverseerRelation(
            Pawn_RelationsTracker relations,
            Pawn overseer,
            Pawn subject)
        {
            if (ReferenceEquals(overseer, subject))
            {
                return false;
            }

            return relations.DirectRelationExists(PawnRelationDefOf.Overseer, subject);
        }

        private static void RemoveOverseerRelationIfPresent(
            Pawn_RelationsTracker overseerRelations,
            Pawn overseer,
            Pawn subject)
        {
            if (ReferenceEquals(overseer, subject))
            {
                return;
            }

            if (overseerRelations.DirectRelationExists(PawnRelationDefOf.Overseer, subject))
            {
                overseerRelations.TryRemoveDirectRelation(PawnRelationDefOf.Overseer, subject);
            }
        }

        private static void AddOverseerRelationIfAbsent(Pawn overseer, Pawn subject)
        {
            if (ReferenceEquals(overseer, subject))
            {
                return;
            }

            // 统一工具负责关系写入、控制组分配与带宽刷新。必须传播其失败：
            // 返回 false 表示 target 未能成功接管监管，抛出异常交由主事务 catch 触发回滚，
            // 不得忽略返回值或只记录日志后继续。
            if (!MAPOverseerAssignmentUtility.TryAssignActualOverseer(overseer, subject))
            {
                throw new InvalidOperationException(
                    "[MAP-机械族机械师] 监管关系迁移失败：" +
                    $"targetOverseer={overseer.LabelShort}（{overseer.ThingID}），" +
                    $"subject={subject.LabelShort}（{subject.ThingID}），" +
                    "TryAssignActualOverseer 返回 false。");
            }
        }

        private static void RollbackTransferBestEffort(
            Pawn source,
            Pawn target,
            MechanoidMechanitorRecord sourceRecord,
            MechanoidMechanitorRecord targetRecord,
            Dictionary<SkillDef, int> targetSkillSnapshot,
            int sourceChipBandwidthBonus,
            int targetChipBandwidthBonus,
            List<OverseerRelationSnapshot> overseerSnapshots,
            List<string> rollbackFailures)
        {
            RunRollbackStep(
                "恢复目标技能快照",
                () => RestoreSkillLevels(target, targetSkillSnapshot),
                rollbackFailures);

            RunRollbackStep(
                "恢复 sourceRecord.ChipBandwidthBonus",
                () => sourceRecord.ChipBandwidthBonus = sourceChipBandwidthBonus,
                rollbackFailures);

            RunRollbackStep(
                "恢复 targetRecord.ChipBandwidthBonus",
                () => targetRecord.ChipBandwidthBonus = targetChipBandwidthBonus,
                rollbackFailures);

            RunRollbackStep(
                "恢复监管关系",
                () => RollbackOverseerRelations(source, target, overseerSnapshots, rollbackFailures),
                rollbackFailures);

            RunRollbackStep(
                "通知 source.mechanitor 带宽变化",
                () => source.mechanitor?.Notify_BandwidthChanged(),
                rollbackFailures);

            RunRollbackStep(
                "通知 target.mechanitor 带宽变化",
                () => target.mechanitor?.Notify_BandwidthChanged(),
                rollbackFailures);
        }

        private static void RunRollbackStep(
            string stepName,
            Action action,
            List<string> rollbackFailures)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                rollbackFailures.Add($"{stepName}：{ex}");
            }
        }

        private static void RestoreSkillLevels(
            Pawn target,
            Dictionary<SkillDef, int> targetSkillSnapshot)
        {
            List<SkillRecord>? targetSkills = target.skills?.skills;
            if (targetSkills == null)
            {
                return;
            }

            foreach (KeyValuePair<SkillDef, int> entry in targetSkillSnapshot)
            {
                SkillRecord? record = FindSkillRecord(targetSkills, entry.Key);
                if (record != null)
                {
                    record.Level = entry.Value;
                }
            }
        }

        internal static void RollbackOverseerRelations(
            Pawn source,
            Pawn target,
            List<OverseerRelationSnapshot> overseerSnapshots,
            List<string> rollbackFailures)
        {
            Pawn_RelationsTracker? sourceRelations = source.relations;
            Pawn_RelationsTracker? targetRelations = target.relations;
            if (sourceRelations == null || targetRelations == null)
            {
                rollbackFailures.Add(
                    "恢复监管关系：source 或 target 缺少 relations " +
                    $"(source={source.LabelShort}（{source.ThingID}），" +
                    $"target={target.LabelShort}（{target.ThingID}））。");
                return;
            }

            for (int i = overseerSnapshots.Count - 1; i >= 0; i--)
            {
                OverseerRelationSnapshot snapshot = overseerSnapshots[i];
                Pawn? mech = snapshot.Mech;
                if (mech == null || mech.Destroyed)
                {
                    continue;
                }

                RunRollbackStep(
                    $"恢复源监管关系：mech={mech.LabelShort}（{mech.ThingID}）",
                    () => RestoreOverseerRelation(
                        source,
                        sourceRelations,
                        mech,
                        snapshot.HadSourceRelation,
                        allowSelfRelation: ReferenceEquals(mech, source)),
                    rollbackFailures);

                bool allowTargetRelation = !ReferenceEquals(mech, target)
                    && !ReferenceEquals(mech, source);
                RunRollbackStep(
                    $"恢复目标监管关系：mech={mech.LabelShort}（{mech.ThingID}）",
                    () => RestoreOverseerRelation(
                        target,
                        targetRelations,
                        mech,
                        allowTargetRelation && snapshot.HadTargetRelation,
                        allowSelfRelation: false),
                    rollbackFailures);
            }
        }

        private static void RestoreOverseerRelation(
            Pawn overseer,
            Pawn_RelationsTracker relations,
            Pawn subject,
            bool shouldHaveRelation,
            bool allowSelfRelation)
        {
            if (overseer.Destroyed
                || subject.Destroyed
                || (!allowSelfRelation && ReferenceEquals(overseer, subject)))
            {
                return;
            }

            bool hasRelation = relations.DirectRelationExists(
                PawnRelationDefOf.Overseer,
                subject);

            if (shouldHaveRelation)
            {
                bool relationAddedByThisCall = false;
                bool controlGroupExisted = overseer.mechanitor?.GetControlGroup(subject) != null;

                if (!hasRelation)
                {
                    relations.AddDirectRelation(PawnRelationDefOf.Overseer, subject);
                    relationAddedByThisCall = true;
                }

                Pawn_MechanitorTracker? tracker = overseer.mechanitor;
                if (tracker == null)
                {
                    if (relationAddedByThisCall)
                    {
                        relations.TryRemoveDirectRelation(PawnRelationDefOf.Overseer, subject);
                    }

                    throw new InvalidOperationException(
                        $"[MAP-机械族机械师] 监管关系回滚失败：shouldHaveRelation=true，" +
                        $"overseer={overseer.LabelShort}（{overseer.ThingID}）缺少 mechanitor Tracker，" +
                        $"subject={subject.LabelShort}（{subject.ThingID}），" +
                        "relationExists=false，controlGroupExists=false，directionValid=false。");
                }

                // 原版 RelationWorker 可能在 OnRelationCreated 中已分配控制组；
                // 仅当仍为空时显式分配。AssignPawnControlGroup 无返回值，必须随后验证。
                if (tracker.GetControlGroup(subject) == null && tracker.CanOverseeSubject(subject))
                {
                    tracker.AssignPawnControlGroup(subject);
                }

                if (tracker.GetControlGroup(subject) == null)
                {
                    if (relationAddedByThisCall)
                    {
                        relations.TryRemoveDirectRelation(PawnRelationDefOf.Overseer, subject);
                    }

                    tracker.UnassignPawnFromAnyControlGroup(subject);
                    tracker.Notify_BandwidthChanged();
                    throw new InvalidOperationException(
                        $"[MAP-机械族机械师] 监管关系回滚失败：shouldHaveRelation=true，控制组分配失败，" +
                        $"overseer={overseer.LabelShort}（{overseer.ThingID}），" +
                        $"subject={subject.LabelShort}（{subject.ThingID}），" +
                        $"relationExists={relations.DirectRelationExists(PawnRelationDefOf.Overseer, subject)}，" +
                        "controlGroupExists=false，directionValid=false。");
                }

                tracker.Notify_BandwidthChanged();

                bool relationExists = relations.DirectRelationExists(PawnRelationDefOf.Overseer, subject);
                bool controlGroupExists = tracker.GetControlGroup(subject) != null;
                bool directionValid = MAPOverseerRelationDirectionUtility.IsActualOverseerOf(overseer, subject);

                if (!relationExists || !controlGroupExists || !directionValid)
                {
                    if (relationAddedByThisCall)
                    {
                        relations.TryRemoveDirectRelation(PawnRelationDefOf.Overseer, subject);
                    }

                    if (!controlGroupExisted)
                    {
                        tracker.UnassignPawnFromAnyControlGroup(subject);
                    }

                    tracker.Notify_BandwidthChanged();
                    throw new InvalidOperationException(
                        $"[MAP-机械族机械师] 监管关系回滚失败：shouldHaveRelation=true，最终验证失败，" +
                        $"overseer={overseer.LabelShort}（{overseer.ThingID}），" +
                        $"subject={subject.LabelShort}（{subject.ThingID}），" +
                        $"relationExists={relationExists}，controlGroupExists={controlGroupExists}，" +
                        $"directionValid={directionValid}。");
                }
            }
            else
            {
                Pawn_MechanitorTracker? tracker = overseer.mechanitor;

                if (hasRelation)
                {
                    relations.TryRemoveDirectRelation(PawnRelationDefOf.Overseer, subject);
                }

                // 即使关系已不存在，仍必须显式清除可能残留的控制组（防御性），
                // 不能依赖 OnRelationRemoved 一定清除。
                tracker?.UnassignPawnFromAnyControlGroup(subject);
                tracker?.Notify_BandwidthChanged();

                bool relationExists = relations.DirectRelationExists(PawnRelationDefOf.Overseer, subject);
                bool controlGroupExists = tracker?.GetControlGroup(subject) != null;
                bool directionValid = MAPOverseerRelationDirectionUtility.IsActualOverseerOf(overseer, subject);

                if (relationExists || controlGroupExists || directionValid)
                {
                    throw new InvalidOperationException(
                        "[MAP-机械族机械师] 监管关系回滚失败：" +
                        "shouldHaveRelation=false，但监管状态未完全清除，" +
                        $"overseer={overseer.LabelShort}（{overseer.ThingID}），" +
                        $"subject={subject.LabelShort}（{subject.ThingID}），" +
                        $"hasRelationBefore={hasRelation}，" +
                        $"relationExists={relationExists}，controlGroupExists={controlGroupExists}，" +
                        $"directionValid={directionValid}。");
                }
            }
        }

        private static void ApplyManagedAbilityTransferBestEffort(
            Pawn source,
            Pawn target,
            List<ManagedResearchAbilityTransferSnapshot> abilitySnapshots)
        {
            try
            {
                ManagedResearchAbilityTransferUtility.ApplyAfterCommittedTransfer(
                    source,
                    target,
                    abilitySnapshots);
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] post-commit 能力迁移处理异常：" +
                    $"source={source.LabelShort}（{source.ThingID}），" +
                    $"target={target.LabelShort}（{target.ThingID}）：{ex}");
            }
        }

        private static void FinalizeSuccessfulTransferBestEffort(
            Pawn source,
            Pawn target,
            Pawn? hostBeforeTransfer,
            MechanicalConsciousnessTransferContext context)
        {
            try
            {
                source.mechanitor?.Notify_BandwidthChanged();
                target.mechanitor?.Notify_BandwidthChanged();
                if (context != MechanicalConsciousnessTransferContext.Emergency)
                {
                    MechanoidMechanitorRoleUtility.EnsureRoleState(source);
                }

                MechanoidMechanitorRoleUtility.EnsureRoleState(target);
                MechanoidMechanitorScenarioFreeColonistUtility.NotifyColonistDisplaysDirtyIfReady();
            }
            catch (Exception ex)
            {
                LogTransferException(source, target, hostBeforeTransfer, ex, committed: true);
            }
        }

        private static void LogTransferFailureWithRollback(
            Pawn source,
            Pawn target,
            Pawn? hostBeforeTransfer,
            Exception? originalException,
            bool hostReplaceRejected,
            bool rollbackAttempted,
            List<string> rollbackFailures)
        {
            Pawn? currentHost =
                GameComponent_MechanoidMechanitorRegistry.CurrentMechanicalConsciousnessHost;
            bool rollbackComplete = rollbackFailures.Count == 0;
            string cause = hostReplaceRejected
                ? "宿主替换未提交（TryReplaceMechanicalConsciousnessHost 返回 false）"
                : "pre-commit 阶段异常";

            string message =
                "[MAP-机械族机械师] 机械意识转移失败（" + cause + "）：" +
                $"source={source.LabelShort}（{source.ThingID}），" +
                $"target={target.LabelShort}（{target.ThingID}），" +
                $"hostBefore={hostBeforeTransfer?.LabelShort ?? "null"}，" +
                $"currentHost={currentHost?.LabelShort ?? "null"}，" +
                $"rollbackAttempted={rollbackAttempted}，" +
                $"rollbackComplete={rollbackComplete}";

            if (originalException != null)
            {
                message += "：" + originalException;
            }

            if (!rollbackComplete)
            {
                message += "；回滚失败步骤：[" + string.Join(" | ", rollbackFailures) + "]";
            }

            Log.Error(message);
        }

        private static void LogTransferException(
            Pawn source,
            Pawn target,
            Pawn? hostBeforeTransfer,
            Exception ex,
            bool committed)
        {
            string phase = committed ? "post-commit" : "pre-commit";
            Pawn? currentHost =
                GameComponent_MechanoidMechanitorRegistry.CurrentMechanicalConsciousnessHost;
            Log.Error(
                "[MAP-机械族机械师] 机械意识转移在 " +
                $"{phase} 阶段失败：source={source.LabelShort}（{source.ThingID}），" +
                $"target={target.LabelShort}（{target.ThingID}），" +
                $"hostBefore={hostBeforeTransfer?.LabelShort ?? "null"}，" +
                $"currentHost={currentHost?.LabelShort ?? "null"}：{ex}");
        }
    }
}
