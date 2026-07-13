using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    public static class ManagedResearchFeatureSyncUtility
    {
        public static bool SyncAllFeatures()
        {
            bool allSucceeded = true;
            bool result = SyncFeatureSafe(
                ManagedResearchFeatureCatalog.AutonomousDirectiveOptimization,
                SyncAutonomousDirectiveOptimization);
            allSucceeded = result && allSucceeded;

            result = SyncFeatureSafe(
                ManagedResearchFeatureCatalog.MechanicalConsciousnessTransfer,
                SyncMechanicalConsciousnessTransfer);
            allSucceeded = result && allSucceeded;

            result = SyncFeatureSafe(
                ManagedResearchFeatureCatalog.DataProcessingAllocation,
                SyncDataProcessingAllocation);
            allSucceeded = result && allSucceeded;

            result = SyncFeatureSafe(
                ManagedResearchFeatureCatalog.SelfDirectiveFocus,
                SyncSelfDirectiveFocus);
            allSucceeded = result && allSucceeded;

            result = SyncDynamicConsciousnessBonusesSafe();
            allSucceeded = result && allSucceeded;

            return allSucceeded;
        }

        /// <summary>
        /// 刷新所有有效注册机械族机械师与机械意识宿主上的动态意识加成。
        /// 目标仅来自注册表，不扫描全局 Pawn。
        /// </summary>
        public static bool SyncDynamicConsciousnessBonuses()
        {
            HashSet<Pawn> targets = new HashSet<Pawn>();
            IReadOnlyList<Pawn> registered =
                GameComponent_MechanoidMechanitorRegistry.CurrentRegisteredMechanitors;
            for (int i = 0; i < registered.Count; i++)
            {
                TryAddConsciousnessRefreshTarget(targets, registered[i]);
            }

            TryAddConsciousnessRefreshTarget(
                targets,
                GameComponent_MechanoidMechanitorRegistry.CurrentMechanicalConsciousnessHost);

            // 先建立快照，再执行可能触发 Notify_HediffChanged 的刷新。
            List<Pawn> snapshot = new List<Pawn>(targets);
            bool allSucceeded = true;
            for (int i = 0; i < snapshot.Count; i++)
            {
                Pawn pawn = snapshot[i];
                if (pawn == null || pawn.Destroyed || pawn.Dead)
                {
                    continue;
                }

                try
                {
                    DynamicConsciousnessBonusUtility.RefreshForPawn(pawn);
                }
                catch (Exception ex)
                {
                    allSucceeded = false;
                    Log.Error(
                        "[MAP-机械族机械师] 动态意识加成刷新失败：" +
                        $"pawn={pawn.LabelShort}（{pawn.ThingID}）：{ex}");
                }
            }

            return allSucceeded;
        }

        private static bool SyncDynamicConsciousnessBonusesSafe()
        {
            try
            {
                return SyncDynamicConsciousnessBonuses();
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 动态意识加成同步失败：" + ex);
                return false;
            }
        }

        private static void TryAddConsciousnessRefreshTarget(HashSet<Pawn> result, Pawn? pawn)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead)
            {
                return;
            }

            result.Add(pawn);
        }

        private static bool SyncFeatureSafe(
            ManagedResearchFeatureDescriptor descriptor,
            Func<bool> syncAction)
        {
            try
            {
                return syncAction();
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 受管理特性同步失败：" +
                    $"featureId={descriptor.Id}，" +
                    $"research={descriptor.ResearchProjectDefName}：{ex}");
                return false;
            }
        }

        private static bool SyncAutonomousDirectiveOptimization()
        {
            bool allSucceeded = true;
            HashSet<Pawn> targets = CollectSelfWorkModeSyncTargets();
            foreach (Pawn pawn in targets)
            {
                try
                {
                    MechanoidMechanitorSelfWorkModeUtility.SyncSelfWorkModeEffects(pawn);
                }
                catch (Exception ex)
                {
                    allSucceeded = false;
                    Log.Error(
                        "[MAP-机械族机械师] 自律指令优化状态同步失败：" +
                        $"pawn={pawn.LabelShort}（{pawn.ThingID}）：{ex}");
                }
            }

            return allSucceeded;
        }

        private static bool SyncMechanicalConsciousnessTransfer()
        {
            if (ResearchFeatureUnlockUtility.IsMechanicalConsciousnessTransferUnlocked())
            {
                return true;
            }

            JobDef? transferJobDef = MAPMechanitor_JobDefOf.MAP_TransferMechanicalConsciousness;
            if (transferJobDef == null)
            {
                Log.Error(
                    "[MAP-机械族机械师] 取消机械意识转移工作失败：缺少 JobDef " +
                    "MAP_TransferMechanicalConsciousness。");
                return false;
            }

            bool allSucceeded = true;
            HashSet<Pawn> targets = CollectPlayerMechanitorPawns();
            foreach (Pawn pawn in targets)
            {
                try
                {
                    CancelTransferJobs(pawn, transferJobDef);
                }
                catch (Exception ex)
                {
                    allSucceeded = false;
                    Log.Error(
                        "[MAP-机械族机械师] 取消机械意识转移工作失败：" +
                        $"pawn={pawn.LabelShort}（{pawn.ThingID}）：{ex}");
                }
            }

            return allSucceeded;
        }

        private static bool SyncDataProcessingAllocation()
        {
            if (ResearchFeatureUnlockUtility.IsDataProcessingAllocationUnlocked())
            {
                return true;
            }

            if (Current.Game == null)
            {
                return true;
            }

            GameComponent_DataProcessingAllocationRegistry? registry =
                GameComponent_DataProcessingAllocationRegistry.CurrentRegistry;
            if (registry == null)
            {
                Log.Error(
                    "[MAP-机械族机械师] 数据处理分配清理失败：缺少 " +
                    "GameComponent_DataProcessingAllocationRegistry。");
                return false;
            }

            return registry.ClearAllAllocationsAndEffects();
        }

        private static bool SyncSelfDirectiveFocus()
        {
            if (!ResearchFeatureUnlockUtility.IsDataProcessingAllocationUnlocked())
            {
                return true;
            }

            if (ResearchFeatureUnlockUtility.IsSelfDirectiveFocusUnlocked())
            {
                return true;
            }

            if (Current.Game == null)
            {
                return true;
            }

            GameComponent_DataProcessingAllocationRegistry? registry =
                GameComponent_DataProcessingAllocationRegistry.CurrentRegistry;
            if (registry == null)
            {
                Log.Error(
                    "[MAP-机械族机械师] 自我指令聚焦清理失败：缺少 " +
                    "GameComponent_DataProcessingAllocationRegistry。");
                return false;
            }

            return registry.ClearSelfAllocationsAndEffects();
        }

        private static void CancelTransferJobs(Pawn pawn, JobDef transferJobDef)
        {
            Pawn_JobTracker? jobs = pawn.jobs;
            if (jobs == null)
            {
                return;
            }

            if (pawn.CurJobDef == transferJobDef)
            {
                jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            }

            JobQueue? queue = jobs.jobQueue;
            if (queue != null && queue.Count > 0)
            {
                queue.RemoveAll(pawn, job => job != null && job.def == transferJobDef);
            }
        }

        private static HashSet<Pawn> CollectSelfWorkModeSyncTargets()
        {
            HashSet<Pawn> result = new HashSet<Pawn>();
            IReadOnlyList<Pawn> registered =
                GameComponent_MechanoidMechanitorRegistry.CurrentRegisteredMechanitors;
            for (int i = 0; i < registered.Count; i++)
            {
                TryAddPlayerPawn(result, registered[i]);
            }

            if (Current.Game == null || Find.World == null)
            {
                return result;
            }

            List<Pawn> playerFactionPawns =
                PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_OfPlayerFaction;
            for (int i = 0; i < playerFactionPawns.Count; i++)
            {
                Pawn pawn = playerFactionPawns[i];
                if (MechanoidMechanitorSelfWorkModeUtility.HasSelfWorkMode(pawn)
                    || MechanoidMechanitorSelfWorkModeUtility.HasSelfWorkModeHediffs(pawn))
                {
                    TryAddPlayerPawn(result, pawn);
                }
            }

            return result;
        }

        private static HashSet<Pawn> CollectPlayerMechanitorPawns()
        {
            HashSet<Pawn> result = new HashSet<Pawn>();
            IReadOnlyList<Pawn> registered =
                GameComponent_MechanoidMechanitorRegistry.CurrentRegisteredMechanitors;
            for (int i = 0; i < registered.Count; i++)
            {
                TryAddPlayerPawn(result, registered[i]);
            }

            TryAddPlayerPawn(
                result,
                GameComponent_MechanoidMechanitorRegistry.CurrentMechanicalConsciousnessHost);
            return result;
        }

        private static void TryAddPlayerPawn(HashSet<Pawn> result, Pawn? pawn)
        {
            if (pawn == null
                || pawn.Destroyed
                || pawn.Dead
                || pawn.Faction == null
                || !pawn.Faction.IsPlayerSafe())
            {
                return;
            }

            result.Add(pawn);
        }
    }
}
