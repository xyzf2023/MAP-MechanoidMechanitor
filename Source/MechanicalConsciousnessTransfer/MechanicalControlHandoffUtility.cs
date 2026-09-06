using System;
using System.Collections.Generic;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// “轨道数据网络”研究完成后，“机械意识转移/紧急意识转移”被替换为纯粹的“控制权交接”。
    /// 本类是主动控制权交接与紧急控制权交接共用的权威业务入口，只处理：
    ///   1. 芯片带宽加成转移；
    ///   2. 当前角色监管的机械族及其控制权转移。
    /// 绝不迁移技能、科研能力、能力冷却、机械意识 Hediff 或 mechanicalConsciousnessHost。
    /// “芯片带宽加成 + 监管关系”的迁移与回滚复用 MechanicalConsciousnessTransferUtility
    /// 中与旧意识转移完全一致的实现，避免新旧路径产生数值或监管语义分歧。
    /// </summary>
    public static class MechanicalControlHandoffUtility
    {
        /// <summary>
        /// 控制权交接是否处于激活状态：仅以“轨道数据网络”研究完成为准。
        /// 研究前仍然使用“意识转移”，本方法在研究前必须返回 false。
        /// </summary>
        public static bool IsActive()
        {
            return ResearchFeatureUnlockUtility.IsOrbitalDataNetworkUnlocked();
        }

        /// <summary>
        /// 是否为合法的控制权交接参与方（源与目标通用）。
        /// 必须是“机械族机械师剧本”中已注册、存活、初始化完成、属于玩家阵营的
        /// 机械族机械师；不要求是 mechanicalConsciousnessHost。
        /// </summary>
        public static bool IsValidControlHandoffParticipant(Pawn? pawn)
        {
            return IsActive()
                && MechanoidMechanitorScenarioUtility.IsScenarioActive
                && GameComponent_MechanoidMechanitorRegistry.IsPawnAliveAndInitialized(pawn)
                && pawn!.RaceProps != null
                && pawn.RaceProps.IsMechanoid
                && pawn.Faction != null
                && pawn.Faction.IsPlayerSafe()
                && GameComponent_MechanoidMechanitorRegistry.TryGetMechanitorRecord(pawn, out _);
        }

        /// <summary>
        /// 控制权交接资格：源与目标都必须是通过 IsValidControlHandoffParticipant 的
        /// 不同机械族机械师。主动交接与紧急交接共用本判断。
        /// </summary>
        public static bool CanTransferControl(Pawn? source, Pawn? target)
        {
            return IsValidControlHandoffParticipant(source)
                && IsValidControlHandoffParticipant(target)
                && !ReferenceEquals(source, target);
        }

        /// <summary>
        /// 执行控制权交接事务：芯片带宽加成 + 监管机械族控制权，具备独立事务安全。
        /// 任一环节异常都会回滚已修改的芯片奖励、监管关系、控制组与带宽，绝不留下半完成状态，
        /// 也绝不修改 mechanicalConsciousnessHost 或执行任何意识迁移。
        /// </summary>
        public static bool TryTransferControl(Pawn? source, Pawn? target)
        {
            if (!CanTransferControl(source, target))
            {
                return false;
            }

            Pawn resolvedSource = source!;
            Pawn resolvedTarget = target!;
            MechanoidMechanitorRoleUtility.EnsureRoleState(resolvedSource);
            MechanoidMechanitorRoleUtility.EnsureRoleState(resolvedTarget);

            if (resolvedSource.relations == null
                || resolvedTarget.relations == null
                || resolvedSource.mechanitor == null
                || resolvedTarget.mechanitor == null
                || !GameComponent_MechanoidMechanitorRegistry.TryGetMechanitorRecord(
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
            List<Pawn> overseenSnapshot =
                MechanicalConsciousnessTransferUtility.CaptureOverseenPawns(resolvedSource);
            List<MechanicalConsciousnessTransferUtility.OverseerRelationSnapshot>
                overseerSnapshots =
                    new List<MechanicalConsciousnessTransferUtility.OverseerRelationSnapshot>();
            bool rollbackAttempted = false;
            List<string> rollbackFailures = new List<string>();

            try
            {
                MechanicalConsciousnessTransferUtility.ApplyChipBandwidthTransfer(
                    sourceRecord,
                    targetRecord,
                    resolvedTarget,
                    sourceChipBandwidthBonus,
                    targetChipBandwidthBonus);
                MechanicalConsciousnessTransferUtility.TransferOverseerRelations(
                    resolvedSource,
                    resolvedTarget,
                    overseenSnapshot,
                    overseerSnapshots);
            }
            catch (Exception ex)
            {
                if (!rollbackAttempted)
                {
                    rollbackAttempted = true;
                    try
                    {
                        RollbackHandoffBestEffort(
                            resolvedSource,
                            resolvedTarget,
                            sourceRecord,
                            targetRecord,
                            sourceChipBandwidthBonus,
                            targetChipBandwidthBonus,
                            overseerSnapshots,
                            rollbackFailures);
                    }
                    catch (Exception rollbackEx)
                    {
                        // BestEffort 正常不应抛出；兜底记录，仍保证不重复回滚且返回 false。
                        rollbackFailures.Add("控制权交接回滚未捕获异常：" + rollbackEx);
                    }
                }

                LogControlHandoffFailure(
                    resolvedSource,
                    resolvedTarget,
                    originalException: ex,
                    rollbackFailures);
                return false;
            }

            FinalizeSuccessfulHandoffBestEffort(resolvedSource, resolvedTarget);
            return true;
        }

        /// <summary>
        /// 回滚仅涉及芯片奖励与监管关系（及各自的带宽刷新），不涉及任何意识类状态。
        /// 步骤语义与 MechanicalConsciousnessTransferUtility.RollbackTransferBestEffort
        /// 中对应步骤保持一致。
        /// </summary>
        private static void RollbackHandoffBestEffort(
            Pawn source,
            Pawn target,
            MechanoidMechanitorRecord sourceRecord,
            MechanoidMechanitorRecord targetRecord,
            int sourceChipBandwidthBonus,
            int targetChipBandwidthBonus,
            List<MechanicalConsciousnessTransferUtility.OverseerRelationSnapshot>
                overseerSnapshots,
            List<string> rollbackFailures)
        {
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
                () => MechanicalConsciousnessTransferUtility.RollbackOverseerRelations(
                    source,
                    target,
                    overseerSnapshots,
                    rollbackFailures),
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

        private static void FinalizeSuccessfulHandoffBestEffort(
            Pawn source,
            Pawn target)
        {
            try
            {
                source.mechanitor?.Notify_BandwidthChanged();
                target.mechanitor?.Notify_BandwidthChanged();
                MechanoidMechanitorRoleUtility.EnsureRoleState(target);
                MechanoidMechanitorScenarioFreeColonistUtility
                    .NotifyColonistDisplaysDirtyIfReady();
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 控制权交接成功收尾异常：" +
                    $"source={source.LabelShort}（{source.ThingID}），" +
                    $"target={target.LabelShort}（{target.ThingID}）：{ex}");
            }
        }

        private static void LogControlHandoffFailure(
            Pawn source,
            Pawn target,
            Exception? originalException,
            List<string> rollbackFailures)
        {
            bool rollbackComplete = rollbackFailures.Count == 0;
            string message =
                "[MAP-机械族机械师] 控制权交接失败（pre-commit 阶段异常）：" +
                $"source={source.LabelShort}（{source.ThingID}），" +
                $"target={target.LabelShort}（{target.ThingID}），" +
                $"rollbackAttempted=true，rollbackComplete={rollbackComplete}";

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
    }
}
