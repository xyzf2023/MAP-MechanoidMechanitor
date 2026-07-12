using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 机械意识迁移时受管理科研能力的冷却处理。主动与紧急迁移共用此路径。
    /// </summary>
    public static class ManagedResearchAbilityTransferUtility
    {
        public static List<ManagedResearchAbilityTransferSnapshot> CaptureSnapshots(
            Pawn source,
            Pawn target)
        {
            List<ManagedResearchAbilityTransferSnapshot> snapshots =
                new List<ManagedResearchAbilityTransferSnapshot>();

            bool sourceIsJustice = JusticePawnUtility.IsJustice(source);
            bool targetIsJustice = JusticePawnUtility.IsJustice(target);

            IReadOnlyList<ManagedResearchAbilityDescriptor> all =
                ManagedResearchAbilityCatalog.All;
            for (int i = 0; i < all.Count; i++)
            {
                ManagedResearchAbilityDescriptor descriptor = all[i];
                AbilityDef? abilityDef = descriptor.AbilityDef;
                if (abilityDef == null)
                {
                    continue;
                }

                bool unlocked = ResearchFeatureUnlockUtility.IsAbilityUnlocked(descriptor);
                if (!unlocked)
                {
                    continue;
                }

                Ability? sourceAbility = source.abilities?.GetAbility(abilityDef);
                Ability? targetAbility = target.abilities?.GetAbility(abilityDef);
                snapshots.Add(
                    new ManagedResearchAbilityTransferSnapshot(
                        abilityDef,
                        unlocked: true,
                        sourceIsJustice,
                        targetIsJustice,
                        sourceHadAbility: sourceAbility != null,
                        sourceRemainingCooldown: sourceAbility?.CooldownTicksRemaining ?? 0,
                        targetHadAbility: targetAbility != null,
                        targetRemainingCooldown: targetAbility?.CooldownTicksRemaining ?? 0));
            }

            return snapshots;
        }

        public static void ApplyAfterCommittedTransfer(
            Pawn source,
            Pawn target,
            List<ManagedResearchAbilityTransferSnapshot>? snapshots)
        {
            if (snapshots == null || snapshots.Count == 0)
            {
                ManagedResearchAbilitySyncUtility.SyncPawn(source);
                ManagedResearchAbilitySyncUtility.SyncPawn(target);
                return;
            }

            for (int i = 0; i < snapshots.Count; i++)
            {
                ManagedResearchAbilityTransferSnapshot snapshot = snapshots[i];
                try
                {
                    ApplySnapshot(source, target, snapshot);
                }
                catch (Exception ex)
                {
                    Log.Error(
                        "[MAP-机械族机械师] post-commit 能力迁移处理异常：" +
                        $"ability={snapshot.AbilityDef?.defName ?? "null"}，" +
                        $"source={source.LabelShort}（{source.ThingID}），" +
                        $"target={target.LabelShort}（{target.ThingID}）：{ex}");
                }
            }

            ManagedResearchAbilitySyncUtility.SyncPawn(source);
            ManagedResearchAbilitySyncUtility.SyncPawn(target);
        }

        private static void ApplySnapshot(
            Pawn source,
            Pawn target,
            ManagedResearchAbilityTransferSnapshot snapshot)
        {
            if (!snapshot.Unlocked || snapshot.AbilityDef == null)
            {
                return;
            }

            AbilityDef abilityDef = snapshot.AbilityDef;

            // A. 正义 → 正义：完全不迁移
            if (snapshot.SourceIsJustice && snapshot.TargetIsJustice)
            {
                return;
            }

            // B. 正义 → 非正义
            if (snapshot.SourceIsJustice && !snapshot.TargetIsJustice)
            {
                Ability? targetAbility = EnsureAbility(target, abilityDef);
                if (targetAbility == null)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 目标能力处理失败，因此没有移除来源能力：" +
                        $"ability={abilityDef.defName}，" +
                        $"source={source.LabelShort}（{source.ThingID}），" +
                        $"target={target.LabelShort}（{target.ThingID}）。");
                    return;
                }

                targetAbility.ResetCooldown();
                return;
            }

            // C. 非正义 → 正义
            if (!snapshot.SourceIsJustice && snapshot.TargetIsJustice)
            {
                Ability? targetAbility = EnsureAbility(target, abilityDef);
                if (targetAbility == null)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 目标能力处理失败，因此没有移除来源能力：" +
                        $"ability={abilityDef.defName}，" +
                        $"source={source.LabelShort}（{source.ThingID}），" +
                        $"target={target.LabelShort}（{target.ThingID}）。");
                    return;
                }

                int resultRemaining = Mathf.Min(
                    snapshot.SourceRemainingCooldown,
                    snapshot.TargetRemainingCooldown);
                ApplyCooldownResult(
                    targetAbility,
                    resultRemaining,
                    snapshot.TargetRemainingCooldown);
                RemoveAbility(source, abilityDef);
                return;
            }

            // D. 非正义 → 非正义
            {
                Ability? targetAbility = EnsureAbility(target, abilityDef);
                if (targetAbility == null)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 目标能力处理失败，因此没有移除来源能力：" +
                        $"ability={abilityDef.defName}，" +
                        $"source={source.LabelShort}（{source.ThingID}），" +
                        $"target={target.LabelShort}（{target.ThingID}）。");
                    return;
                }

                if (snapshot.SourceRemainingCooldown <= 0)
                {
                    targetAbility.ResetCooldown();
                }
                else
                {
                    targetAbility.StartCooldown(snapshot.SourceRemainingCooldown);
                }

                RemoveAbility(source, abilityDef);
            }
        }

        private static void ApplyCooldownResult(
            Ability ability,
            int resultRemaining,
            int previousTargetRemaining)
        {
            if (resultRemaining <= 0)
            {
                ability.ResetCooldown();
                return;
            }

            if (resultRemaining == previousTargetRemaining)
            {
                return;
            }

            ability.StartCooldown(resultRemaining);
        }

        private static Ability? EnsureAbility(Pawn pawn, AbilityDef abilityDef)
        {
            Pawn_AbilityTracker? tracker = pawn.abilities;
            if (tracker == null)
            {
                if (!pawn.RaceProps.IsMechanoid)
                {
                    return null;
                }

                pawn.abilities = new Pawn_AbilityTracker(pawn);
                tracker = pawn.abilities;
            }

            if (tracker == null)
            {
                Log.Error(
                    "[MAP-机械族机械师] 符合资格但无法创建 Pawn_AbilityTracker：" +
                    $"pawn={pawn.LabelShort}（{pawn.ThingID}）。");
                return null;
            }

            Ability? existing = tracker.GetAbility(abilityDef);
            if (existing != null)
            {
                return existing;
            }

            tracker.GainAbility(abilityDef);
            return tracker.GetAbility(abilityDef);
        }

        private static void RemoveAbility(Pawn pawn, AbilityDef abilityDef)
        {
            Pawn_AbilityTracker? tracker = pawn.abilities;
            if (tracker == null)
            {
                return;
            }

            Ability? ability = tracker.GetAbility(abilityDef);
            if (ability == null)
            {
                return;
            }

            if (pawn.CurJob != null && ReferenceEquals(pawn.CurJob.ability, ability))
            {
                pawn.jobs?.EndCurrentJob(
                    Verse.AI.JobCondition.InterruptForced,
                    startNewJob: false);
            }

            tracker.RemoveAbility(abilityDef);
        }
    }
}
