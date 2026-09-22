using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>自律框架的监管变更协调入口；效果规则和数据仍归各业务系统。</summary>
    internal static class AutonomousMechEffectUtility
    {
        private static void RefreshWorkModeEffects(Pawn pawn)
        {
            MechWorkModeDef mode = pawn.GetMechControlGroup()?.WorkMode ?? MechWorkModeDefOf.Work;
            // 正式机械师原有的第一控制组本体加成仍按原规则保留。
            if (MechanoidMechanitorCapabilityUtility.HasCapability(pawn, MechanoidMechanitorCapability.EnhancedControlModes)
                && pawn.mechanitor?.controlGroups != null)
            {
                foreach (MechanitorControlGroup group in pawn.mechanitor.controlGroups)
                {
                    if (group.Index != 1) continue;
                    mode = group.WorkMode ?? MechWorkModeDefOf.Work;
                    break;
                }
            }
            MechanoidMechanitorWorkModeUtility.ApplyWorkModeHediff(pawn, mode);
            MechanoidMechanitorSelfWorkModeUtility.SyncSelfWorkModeEffects(pawn);
        }

        internal static bool RefreshAfterOverseerChange(Pawn pawn, HashSet<Pawn> formerOverseers)
        {
            if (Scribe.mode != LoadSaveMode.Inactive
                || MechanoidMechanitorPostLoadSafetyCoordinator.ShouldDeferPositiveRestore)
                return false;
            if (pawn.Discarded || pawn.Destroyed || pawn.Dead)
                return true;

            RefreshWorkModeEffects(pawn);
            GameComponent_DataProcessingAllocationRegistry.CurrentRegistry
                ?.RefreshAfterExternalOverseerChange(pawn, formerOverseers);

            // 先撤旧提供者，再按当前来源恢复；本体植入体和合法的新上级效果不可误删。
            foreach (Pawn provider in new List<Pawn>(formerOverseers))
                ImplantEffectUtility.RefreshDistributedEffects(provider);
            Pawn? currentOverseer = MAPOverseerRelationDirectionUtility.FindActualOverseer(pawn);
            if (currentOverseer != null && currentOverseer != pawn)
                ImplantEffectUtility.RefreshDistributedEffects(currentOverseer);
            ImplantEffectUtility.RefreshDistributedEffects(pawn);
            return true;
        }
    }
}
