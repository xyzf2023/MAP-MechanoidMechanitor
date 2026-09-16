using System;
using System.Collections.Generic;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed partial class GameComponent_DataProcessingAllocationRegistry
    {
        // PostLoadInit 的跨组件引用尚不能用来判定归属失效；Ending 会话也可能仍在回转。
        private static bool ShouldDeferFusionOwnershipCleanup()
        {
            return Scribe.mode == LoadSaveMode.PostLoadInit
                && GameComponent_MechFusionSessionRegistry.HasAnySession;
        }

        private bool FrozenSelfMatches(MechFusionSession session)
        {
            Pawn source = session.SourcePawn!;
            MechFusionMechanitorSnapshot snapshot = session.MechanitorSnapshot!;
            return GetStepsForOverseerTarget(source, source) == snapshot.fusionSelfAllocationSteps
                && (snapshot.fusionSelfAllocationSteps == 0
                    || GetSpecializationForOverseerTarget(source, source)
                        == snapshot.fusionSelfAllocationSpecialization);
        }

        private bool TryPrepareFrozenSelfPlan(Pawn owner, List<PlanEntry> entries, out float cost)
        {
            cost = 0f;
            if (!DataProcessingOverseerResolver.TryGetFrozenSelf(owner, out MechFusionSession? session))
            {
                if (session?.IsActive == true && session.MechanitorSnapshot?.captured == true)
                {
                    DataProcessingOverseerResolver.ExitForSafety(session, "旧版会话缺少自身分配冻结快照");
                    return false;
                }
                return true;
            }

            if (!FrozenSelfMatches(session!))
            {
                DataProcessingOverseerResolver.ExitForSafety(session!, "自身档位或特化已被外部改变");
                return false;
            }

            MechFusionMechanitorSnapshot snapshot = session!.MechanitorSnapshot!;
            // 冻结自身不在计划内，必须先恢复半额返还，再允许计划施加监管者负面。
            if (snapshot.fusionSelfAllocationSteps > 0
                && !TryApplyCommandFocusHediffForTarget(owner,
                    snapshot.fusionSelfAllocationSpecialization, snapshot.fusionSelfAllocationSteps,
                    cleanupOtherCommandFocusHediffs: true))
                return false;

            cost = GetBudgetCostUnits(owner, owner, snapshot.fusionSelfAllocationSteps) * 0.025f;
            if (!TryCalculateAllocationFreeConsciousness(owner, out float available)) return false;
            if (snapshot.fusionSelfAllocationSteps > 0
                && available - cost + 0.0001f < DataProcessingAllocationUtility.MinReservedConsciousness)
            {
                DataProcessingOverseerResolver.EndBeforeSelfReclaim(owner, "冻结自身分配已超过安全容量");
                return false;
            }

            entries.RemoveAll(entry => ReferenceEquals(entry.target, owner));
            return true;
        }

        internal void ValidateFusionAllocations()
        {
            if (!GameComponent_MechFusionSessionRegistry.HasAnySession
                || Find.TickManager.TicksGame % 60 != 0) return;
            // 安全解除可能删除会话，不能在原列表上枚举。
            var sessions = new List<MechFusionSession>(GameComponent_MechFusionSessionRegistry.GetSessionsForReading());
            foreach (MechFusionSession session in sessions)
            {
                if (!session.IsActive || session.MechanitorSnapshot?.captured != true
                    || session.SourcePawn == null || session.SourcePawn.Dead) continue;
                if (!session.MechanitorSnapshot.fusionSelfAllocationCaptured)
                    DataProcessingOverseerResolver.ExitForSafety(session, "旧版会话缺少自身分配冻结快照，请在解除完成后重新合体");
                else if (!FrozenSelfMatches(session))
                    DataProcessingOverseerResolver.ExitForSafety(session, "自身分配或科研状态已在合体期间发生变化");
            }
        }

        private static void EndFusionsBeforeClearingAllocations()
        {
            if (!GameComponent_MechFusionSessionRegistry.HasAnySession) return;
            var sessions = new List<MechFusionSession>(GameComponent_MechFusionSessionRegistry.GetSessionsForReading());
            foreach (MechFusionSession session in sessions)
            {
                if (session.IsActive && session.MechanitorSnapshot?.captured == true)
                    DataProcessingOverseerResolver.ExitForSafety(session, "清空全部数据处理分配及配置");
            }
        }
    }
}
