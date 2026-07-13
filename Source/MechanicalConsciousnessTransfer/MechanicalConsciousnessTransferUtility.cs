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
        private readonly struct OverseerRelationSnapshot
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
            return ResearchFeatureUnlockUtility.IsMechanicalConsciousnessTransferUnlocked()
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
                    RollbackTransfer(
                        resolvedSource,
                        resolvedTarget,
                        sourceRecord,
                        targetRecord,
                        targetSkillSnapshot,
                        sourceChipBandwidthBonus,
                        targetChipBandwidthBonus,
                        overseerSnapshots);
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
                    RollbackTransfer(
                        resolvedSource,
                        resolvedTarget,
                        sourceRecord,
                        targetRecord,
                        targetSkillSnapshot,
                        sourceChipBandwidthBonus,
                        targetChipBandwidthBonus,
                        overseerSnapshots);
                    LogTransferException(
                        resolvedSource,
                        resolvedTarget,
                        hostBeforeTransfer,
                        ex,
                        committed: false);
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

        private static List<Pawn> CaptureOverseenPawns(Pawn source)
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

        private static void ApplyChipBandwidthTransfer(
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

        private static void TransferOverseerRelations(
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
                AddOverseerRelationIfAbsent(targetRelations, target, mech);
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

        private static void AddOverseerRelationIfAbsent(
            Pawn_RelationsTracker overseerRelations,
            Pawn overseer,
            Pawn subject)
        {
            if (ReferenceEquals(overseer, subject)
                || overseerRelations.DirectRelationExists(PawnRelationDefOf.Overseer, subject))
            {
                return;
            }

            overseerRelations.AddDirectRelation(PawnRelationDefOf.Overseer, subject);
        }

        private static void RollbackTransfer(
            Pawn source,
            Pawn target,
            MechanoidMechanitorRecord sourceRecord,
            MechanoidMechanitorRecord targetRecord,
            Dictionary<SkillDef, int> targetSkillSnapshot,
            int sourceChipBandwidthBonus,
            int targetChipBandwidthBonus,
            List<OverseerRelationSnapshot> overseerSnapshots)
        {
            RestoreSkillLevels(target, targetSkillSnapshot);
            sourceRecord.ChipBandwidthBonus = sourceChipBandwidthBonus;
            targetRecord.ChipBandwidthBonus = targetChipBandwidthBonus;
            RollbackOverseerRelations(source, target, overseerSnapshots);
            source.mechanitor?.Notify_BandwidthChanged();
            target.mechanitor?.Notify_BandwidthChanged();
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

        private static void RollbackOverseerRelations(
            Pawn source,
            Pawn target,
            List<OverseerRelationSnapshot> overseerSnapshots)
        {
            Pawn_RelationsTracker? sourceRelations = source.relations;
            Pawn_RelationsTracker? targetRelations = target.relations;
            if (sourceRelations == null || targetRelations == null)
            {
                return;
            }

            for (int i = overseerSnapshots.Count - 1; i >= 0; i--)
            {
                OverseerRelationSnapshot snapshot = overseerSnapshots[i];
                Pawn mech = snapshot.Mech;
                if (mech == null || mech.Destroyed)
                {
                    continue;
                }

                RestoreOverseerRelation(
                    source,
                    sourceRelations,
                    mech,
                    snapshot.HadSourceRelation,
                    allowSelfRelation: ReferenceEquals(mech, source));

                bool allowTargetRelation = !ReferenceEquals(mech, target)
                    && !ReferenceEquals(mech, source);
                RestoreOverseerRelation(
                    target,
                    targetRelations,
                    mech,
                    allowTargetRelation && snapshot.HadTargetRelation,
                    allowSelfRelation: false);
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
                if (!hasRelation)
                {
                    relations.AddDirectRelation(PawnRelationDefOf.Overseer, subject);
                }
            }
            else if (hasRelation)
            {
                relations.TryRemoveDirectRelation(PawnRelationDefOf.Overseer, subject);
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
