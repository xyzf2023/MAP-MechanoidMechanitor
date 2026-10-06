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
        private readonly struct SnapshotApplyOutcome
        {
            public readonly ManagedResearchAbilityTransferSnapshot Snapshot;
            public readonly ManagedResearchAbilityTransferApplyResult Result;

            public SnapshotApplyOutcome(
                ManagedResearchAbilityTransferSnapshot snapshot,
                ManagedResearchAbilityTransferApplyResult result)
            {
                Snapshot = snapshot;
                Result = result;
            }
        }

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
                if (descriptor.TransferPolicy
                    != ManagedAbilityTransferPolicy.TransferWithConsciousness)
                {
                    continue;
                }

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

            List<SnapshotApplyOutcome> results =
                new List<SnapshotApplyOutcome>(snapshots.Count);

            for (int i = 0; i < snapshots.Count; i++)
            {
                ManagedResearchAbilityTransferSnapshot snapshot = snapshots[i];
                ManagedResearchAbilityTransferApplyResult result;
                try
                {
                    result = ApplySnapshot(source, target, snapshot);
                }
                catch (Exception ex)
                {
                    result = ManagedResearchAbilityTransferApplyResult.TargetPreparationFailed;
                    Log.Error(
                        "[MAP-机械族机械师] post-commit 能力迁移处理异常：" +
                        $"ability={snapshot.AbilityDef?.defName ?? "null"}，" +
                        $"source={source.LabelShort}（{source.ThingID}），" +
                        $"target={target.LabelShort}（{target.ThingID}）：{ex}");
                }

                results.Add(new SnapshotApplyOutcome(snapshot, result));
            }

            for (int i = 0; i < results.Count; i++)
            {
                SnapshotApplyOutcome outcome = results[i];
                try
                {
                    ManagedResearchAbilityDescriptor? descriptor =
                        ManagedResearchAbilitySyncUtility.FindDescriptor(
                            outcome.Snapshot.AbilityDef);
                    if (descriptor == null)
                    {
                        continue;
                    }

                    if (outcome.Result
                        == ManagedResearchAbilityTransferApplyResult.TargetPreparationFailed)
                    {
                        // 故障保护：跳过来源该能力的资格同步，避免因失去宿主而被删掉。
                        ManagedResearchAbilitySyncUtility.SyncPawnAbility(target, descriptor);
                        continue;
                    }

                    ManagedResearchAbilitySyncUtility.SyncPawnAbility(source, descriptor);
                    ManagedResearchAbilitySyncUtility.SyncPawnAbility(target, descriptor);
                }
                catch (Exception ex)
                {
                    Log.Error(
                        "[MAP-机械族机械师] post-commit 能力最终资格同步异常：" +
                        $"ability={outcome.Snapshot.AbilityDef?.defName ?? "null"}，" +
                        $"result={outcome.Result}，" +
                        $"source={source.LabelShort}（{source.ThingID}），" +
                        $"target={target.LabelShort}（{target.ThingID}）：{ex}");
                }
            }
        }

        private static ManagedResearchAbilityTransferApplyResult ApplySnapshot(
            Pawn source,
            Pawn target,
            ManagedResearchAbilityTransferSnapshot snapshot)
        {
            if (!snapshot.Unlocked || snapshot.AbilityDef == null)
            {
                return ManagedResearchAbilityTransferApplyResult.Skipped;
            }

            AbilityDef abilityDef = snapshot.AbilityDef;

            // A. 正义 → 正义：完全不迁移
            if (snapshot.SourceIsJustice && snapshot.TargetIsJustice)
            {
                return ManagedResearchAbilityTransferApplyResult.Skipped;
            }

            // 目标已有技能时继续原有冷却迁移；开关关闭只阻止向缺失技能的目标补发。
            ManagedResearchAbilityDescriptor? descriptor =
                ManagedResearchAbilitySyncUtility.FindDescriptor(abilityDef);
            if (target.abilities?.GetAbility(abilityDef) == null
                && descriptor != null
                && !ResearchFeatureUnlockUtility.IsAbilityGrantAllowedBySettings(descriptor))
            {
                return ManagedResearchAbilityTransferApplyResult.Skipped;
            }

            // B. 正义 → 非正义
            if (snapshot.SourceIsJustice && !snapshot.TargetIsJustice)
            {
                Ability? targetAbility = EnsureAbility(target, abilityDef);
                if (targetAbility == null)
                {
                    LogTargetPreparationFailed(source, target, abilityDef);
                    return ManagedResearchAbilityTransferApplyResult.TargetPreparationFailed;
                }

                targetAbility.ResetCooldown();
                return ManagedResearchAbilityTransferApplyResult.Success;
            }

            // C. 非正义 → 正义
            if (!snapshot.SourceIsJustice && snapshot.TargetIsJustice)
            {
                Ability? targetAbility = EnsureAbility(target, abilityDef);
                if (targetAbility == null)
                {
                    LogTargetPreparationFailed(source, target, abilityDef);
                    return ManagedResearchAbilityTransferApplyResult.TargetPreparationFailed;
                }

                int resultRemaining = Mathf.Min(
                    snapshot.SourceRemainingCooldown,
                    snapshot.TargetRemainingCooldown);
                ApplyCooldownResult(
                    targetAbility,
                    resultRemaining,
                    snapshot.TargetRemainingCooldown);
                RemoveSourceAbilityIfIneligible(source, abilityDef);
                return ManagedResearchAbilityTransferApplyResult.Success;
            }

            // D. 非正义 → 非正义
            {
                Ability? targetAbility = EnsureAbility(target, abilityDef);
                if (targetAbility == null)
                {
                    LogTargetPreparationFailed(source, target, abilityDef);
                    return ManagedResearchAbilityTransferApplyResult.TargetPreparationFailed;
                }

                if (snapshot.SourceRemainingCooldown <= 0)
                {
                    targetAbility.ResetCooldown();
                }
                else
                {
                    targetAbility.StartCooldown(snapshot.SourceRemainingCooldown);
                }

                RemoveSourceAbilityIfIneligible(source, abilityDef);
                return ManagedResearchAbilityTransferApplyResult.Success;
            }
        }

        private static void RemoveSourceAbilityIfIneligible(Pawn source, AbilityDef abilityDef)
        {
            ManagedResearchAbilityDescriptor? descriptor =
                ManagedResearchAbilitySyncUtility.FindDescriptor(abilityDef);
            // 轨道数据网络可能让失去意识宿主身份的源机械师仍有科研能力资格。
            // 保留其原能力实例与冷却，避免先删后补把独立享有的能力刷新为零冷却。
            if (descriptor != null && ManagedAbilityEligibilityUtility.ShouldPawnHaveAbility(source, descriptor))
            {
                return;
            }

            ManagedResearchAbilitySyncUtility.SafeRemoveAbility(source, abilityDef);
        }

        private static void LogTargetPreparationFailed(
            Pawn source,
            Pawn target,
            AbilityDef abilityDef)
        {
            Log.Error(
                "[MAP-机械族机械师] 目标能力处理失败，因此没有移除来源能力：" +
                $"ability={abilityDef.defName}，" +
                $"source={source.LabelShort}（{source.ThingID}），" +
                $"target={target.LabelShort}（{target.ThingID}）。" +
                "该能力保留在来源 Pawn 上作为故障保护状态。");
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
    }
}
