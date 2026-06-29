using System;
using System.Collections.Generic;
using MAP_MechanoidMechanitor;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public static class MechanicalConsciousnessTransferUtility
    {
        private enum OverseerRelationChangeKind
        {
            Transferred,
            RemovedSourceOnly
        }

        private readonly struct OverseerRelationChange
        {
            public readonly Pawn Mech;
            public readonly OverseerRelationChangeKind Kind;

            public OverseerRelationChange(Pawn mech, OverseerRelationChangeKind kind)
            {
                Mech = mech;
                Kind = kind;
            }
        }

        public static bool CanTransferMechanicalConsciousness(Pawn? source, Pawn? target)
        {
            if (!PassesTransferEligibility(source, target, out _, out _))
            {
                return false;
            }

            return HasMechanitorTransferComponents(source!)
                && HasMechanitorTransferComponents(target!);
        }

        public static bool TryTransferMechanicalConsciousness(Pawn? source, Pawn? target)
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
            List<OverseerRelationChange> overseerChanges = new List<OverseerRelationChange>();
            bool hostReplaced = false;

            try
            {
                MergeTargetSkillsFromSource(resolvedSource, resolvedTarget);
                ApplyChipBandwidthMigration(
                    sourceRecord,
                    targetRecord,
                    resolvedTarget,
                    sourceChipBandwidthBonus,
                    targetChipBandwidthBonus);
                TransferOverseerRelations(
                    resolvedSource,
                    resolvedTarget,
                    overseenSnapshot,
                    overseerChanges);

                hostReplaced = GameComponent_MechanoidMechanitorRegistry
                    .TryReplaceMechanicalConsciousnessHost(resolvedSource, resolvedTarget);
                if (!hostReplaced)
                {
                    RollbackTransfer(
                        resolvedSource,
                        resolvedTarget,
                        sourceRecord,
                        targetRecord,
                        targetSkillSnapshot,
                        sourceChipBandwidthBonus,
                        targetChipBandwidthBonus,
                        overseerChanges);
                    return false;
                }

                FinalizeSuccessfulTransfer(resolvedSource, resolvedTarget);
                return true;
            }
            catch (Exception ex)
            {
                if (!hostReplaced)
                {
                    RollbackTransfer(
                        resolvedSource,
                        resolvedTarget,
                        sourceRecord,
                        targetRecord,
                        targetSkillSnapshot,
                        sourceChipBandwidthBonus,
                        targetChipBandwidthBonus,
                        overseerChanges);
                }

                Log.Error(
                    "[MAP-MechanoidMechanitor] TryTransferMechanicalConsciousness failed for " +
                    $"source={resolvedSource.LabelShort} ({resolvedSource.ThingID}), " +
                    $"target={resolvedTarget.LabelShort} ({resolvedTarget.ThingID}), " +
                    $"hostBefore={hostBeforeTransfer?.LabelShort ?? "null"}: {ex}");
                return false;
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

            if (!JusticeScenarioUtility.IsJusticeScenarioActive)
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

        private static bool HasMechanitorTransferComponents(Pawn pawn)
        {
            return pawn.relations != null && pawn.mechanitor != null;
        }

        private static bool TryEnsureMechanitorTransferComponents(
            Pawn source,
            Pawn target)
        {
            MechanoidMechanitorRoleUtility.EnsureRoleState(source);
            MechanoidMechanitorRoleUtility.EnsureRoleState(target);
            return HasMechanitorTransferComponents(source)
                && HasMechanitorTransferComponents(target);
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

        private static void ApplyChipBandwidthMigration(
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
            List<OverseerRelationChange> overseerChanges)
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

                if (ReferenceEquals(mech, target))
                {
                    RemoveOverseerRelationIfPresent(sourceRelations, source, mech);
                    overseerChanges.Add(
                        new OverseerRelationChange(mech, OverseerRelationChangeKind.RemovedSourceOnly));
                    continue;
                }

                if (ReferenceEquals(mech, source))
                {
                    RemoveOverseerRelationIfPresent(sourceRelations, source, mech);
                    overseerChanges.Add(
                        new OverseerRelationChange(mech, OverseerRelationChangeKind.RemovedSourceOnly));
                    continue;
                }

                RemoveOverseerRelationIfPresent(sourceRelations, source, mech);
                AddOverseerRelationIfAbsent(targetRelations, target, mech);
                overseerChanges.Add(
                    new OverseerRelationChange(mech, OverseerRelationChangeKind.Transferred));
            }
        }

        private static void RemoveOverseerRelationIfPresent(
            Pawn_RelationsTracker overseerRelations,
            Pawn overseer,
            Pawn subject)
        {
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
            List<OverseerRelationChange> overseerChanges)
        {
            RestoreSkillLevels(target, targetSkillSnapshot);
            sourceRecord.ChipBandwidthBonus = sourceChipBandwidthBonus;
            targetRecord.ChipBandwidthBonus = targetChipBandwidthBonus;
            RollbackOverseerRelations(source, target, overseerChanges);
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
            List<OverseerRelationChange> overseerChanges)
        {
            Pawn_RelationsTracker? sourceRelations = source.relations;
            Pawn_RelationsTracker? targetRelations = target.relations;
            if (sourceRelations == null || targetRelations == null)
            {
                return;
            }

            for (int i = overseerChanges.Count - 1; i >= 0; i--)
            {
                OverseerRelationChange change = overseerChanges[i];
                Pawn mech = change.Mech;
                if (mech == null || mech.Destroyed)
                {
                    continue;
                }

                switch (change.Kind)
                {
                    case OverseerRelationChangeKind.Transferred:
                        RemoveOverseerRelationIfPresent(targetRelations, target, mech);
                        AddOverseerRelationIfAbsent(sourceRelations, source, mech);
                        break;
                    case OverseerRelationChangeKind.RemovedSourceOnly:
                        AddOverseerRelationIfAbsent(sourceRelations, source, mech);
                        break;
                }
            }
        }

        private static void FinalizeSuccessfulTransfer(Pawn source, Pawn target)
        {
            source.mechanitor?.Notify_BandwidthChanged();
            target.mechanitor?.Notify_BandwidthChanged();
            MechanoidMechanitorRoleUtility.EnsureRoleState(source);
            MechanoidMechanitorRoleUtility.EnsureRoleState(target);
        }
    }
}
