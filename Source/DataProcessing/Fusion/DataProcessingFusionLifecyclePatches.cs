using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(MechFusionMechanitorSynchronizationService), "CaptureBeforeSourceDespawn")]
    internal static class DataProcessingFusionCapturePatch
    {
        private static void Postfix(MechFusionSession session)
        {
            // 早于 DeSpawn、征召刷新与合体属性快照。
            DataProcessingOverseerResolver.CaptureFrozenSelf(session);
        }
    }

    [HarmonyPatch]
    internal static class DataProcessingFusionTransferScopePatch
    {
        private sealed class Scope
        {
            internal MechFusionSession? previousSession;
            internal HashSet<Pawn>? previousTargets;
        }

        private static IEnumerable<MethodBase> TargetMethods()
        {
            Type type = typeof(MechFusionMechanitorSynchronizationService);
            yield return DataProcessingFusionReadPatches.Require(type, "ApplyOrRepair");
            yield return DataProcessingFusionReadPatches.Require(type, "PrepareForHealthEffectRemoval");
        }

        private static void Prefix(MechFusionSession session, out Scope __state)
        {
            __state = new Scope
            {
                previousSession = DataProcessingOverseerResolver.TransferSession,
                previousTargets = DataProcessingOverseerResolver.TransferTargets
            };
            HashSet<Pawn> targets = new HashSet<Pawn>();
            MechFusionMechanitorSnapshot? snapshot = session.MechanitorSnapshot;
            if (snapshot != null)
            {
                foreach (MechFusionControlGroupSnapshot group in snapshot.sourceGroups)
                {
                    if (group == null) continue;
                    foreach (MechFusionControlledMechSnapshot mech in group.mechs)
                        if (mech?.pawn != null) targets.Add(mech.pawn);
                }
                if (!snapshot.wearerWasMechanitor && session.WearerPawn?.mechanitor != null)
                    foreach (Pawn pawn in session.WearerPawn.mechanitor.OverseenPawns)
                        targets.Add(pawn);
            }
            DataProcessingOverseerResolver.TransferSession = session;
            DataProcessingOverseerResolver.TransferTargets = targets;
        }

        private static void Finalizer(Scope? __state)
        {
            if (__state == null) return;
            DataProcessingOverseerResolver.TransferSession = __state.previousSession;
            DataProcessingOverseerResolver.TransferTargets = __state.previousTargets;
        }
    }

    [HarmonyPatch(typeof(GameComponent_MechFusionSessionRegistry), "RemoveSession")]
    internal static class DataProcessingFusionResumePatch
    {
        private static void Postfix(MechFusionSession? session)
        {
            if (session?.TeardownCompleted != true) return;
            DataProcessingPawnLifecycleCoordinator.EnqueueReactivation(session.SourcePawn);
            DataProcessingPawnLifecycleCoordinator.EnqueueReactivation(session.WearerPawn);
        }
    }

    [HarmonyPatch(typeof(GameComponent_MechFusionSessionRegistry), nameof(GameComponent_MechFusionSessionRegistry.GameComponentTick))]
    internal static class DataProcessingFusionIntegrityPatch
    {
        private static void Prefix()
        {
            if (!GameComponent_MechFusionSessionRegistry.HasAnySession
                || Find.TickManager.TicksGame % 60 != 0) return;
            GameComponent_DataProcessingAllocationRegistry? registry =
                GameComponent_DataProcessingAllocationRegistry.CurrentRegistry;
            if (registry == null) return;

            // 解除会改变原始列表，必须枚举副本；只扫描活动合体，不扫描地图 Pawn。
            var sessions = new List<MechFusionSession>(
                GameComponent_MechFusionSessionRegistry.GetSessionsForReading());
            foreach (MechFusionSession session in sessions)
            {
                if (!session.IsActive || session.MechanitorSnapshot?.captured != true
                    || session.SourcePawn == null || session.SourcePawn.Dead) continue;
                if (!session.MechanitorSnapshot.fusionSelfAllocationCaptured)
                {
                    DataProcessingOverseerResolver.ExitForSafety(session,
                        "旧版会话缺少自身分配冻结快照，请在解除完成后重新合体");
                    continue;
                }
                if (!DataProcessingFusionFreezeUtility.Matches(registry, session))
                    DataProcessingOverseerResolver.ExitForSafety(session,
                        "自身分配或科研状态已在合体期间发生变化");
            }
        }
    }
}
