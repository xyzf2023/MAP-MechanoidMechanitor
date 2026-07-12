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
            allSucceeded &= SyncFeatureSafe(
                ManagedResearchFeatureCatalog.AutonomousDirectiveOptimization,
                SyncAutonomousDirectiveOptimization);
            allSucceeded &= SyncFeatureSafe(
                ManagedResearchFeatureCatalog.MechanicalConsciousnessTransfer,
                SyncMechanicalConsciousnessTransfer);
            allSucceeded &= SyncFeatureSafe(
                ManagedResearchFeatureCatalog.DataProcessingAllocation,
                SyncDataProcessingAllocation);
            allSucceeded &= SyncFeatureSafe(
                ManagedResearchFeatureCatalog.SelfDirectiveFocus,
                SyncSelfDirectiveFocus);
            return allSucceeded;
        }

        private static bool SyncFeatureSafe(
            ManagedResearchFeatureDescriptor descriptor,
            Action syncAction)
        {
            try
            {
                syncAction();
                return true;
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

        private static void SyncAutonomousDirectiveOptimization()
        {
            HashSet<Pawn> targets = CollectSelfWorkModeSyncTargets();
            foreach (Pawn pawn in targets)
            {
                try
                {
                    MechanoidMechanitorSelfWorkModeUtility.SyncSelfWorkModeEffects(pawn);
                }
                catch (Exception ex)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 自律指令优化状态同步失败：" +
                        $"pawn={pawn.LabelShort}（{pawn.ThingID}）：{ex}");
                }
            }
        }

        private static void SyncMechanicalConsciousnessTransfer()
        {
            if (ResearchFeatureUnlockUtility.IsMechanicalConsciousnessTransferUnlocked())
            {
                return;
            }

            JobDef? transferJobDef = MAPMechanitor_JobDefOf.MAP_TransferMechanicalConsciousness;
            if (transferJobDef == null)
            {
                return;
            }

            HashSet<Pawn> targets = CollectPlayerMechanitorPawns();
            foreach (Pawn pawn in targets)
            {
                try
                {
                    CancelTransferJobs(pawn, transferJobDef);
                }
                catch (Exception ex)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 取消机械意识转移工作失败：" +
                        $"pawn={pawn.LabelShort}（{pawn.ThingID}）：{ex}");
                }
            }
        }

        private static void SyncDataProcessingAllocation()
        {
            if (ResearchFeatureUnlockUtility.IsDataProcessingAllocationUnlocked())
            {
                return;
            }

            GameComponent_DataProcessingAllocationRegistry? registry =
                GameComponent_DataProcessingAllocationRegistry.CurrentRegistry;
            registry?.ClearAllAllocationsAndEffects();
        }

        private static void SyncSelfDirectiveFocus()
        {
            if (!ResearchFeatureUnlockUtility.IsDataProcessingAllocationUnlocked())
            {
                return;
            }

            if (ResearchFeatureUnlockUtility.IsSelfDirectiveFocusUnlocked())
            {
                return;
            }

            GameComponent_DataProcessingAllocationRegistry? registry =
                GameComponent_DataProcessingAllocationRegistry.CurrentRegistry;
            registry?.ClearSelfAllocationsAndEffects();
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
