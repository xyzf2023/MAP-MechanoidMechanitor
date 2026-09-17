using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 信标资格只捕获一次；协议目标则使用施放时的真实监管关系。
    /// 不复制植入体，也不向合体者授予源机械族的技能。
    /// </summary>
    internal static class MechFusionRepairBeaconUtility
    {
        internal const string StructuralRepairRuleId = "MAP.FusionStructuralRepair";

        internal static void Capture(MechFusionSession session, Pawn? source)
        {
            if (session.RepairBeaconCaptured || source == null) return;

            Pawn? overseer = GetCurrentOverseer(source);
            session.CaptureRepairBeacon(
                ImplantEffectUtility.HasHediff(source, MAPMechanitor_HediffDefOf.MAP_RepairBeacon)
                || ImplantEffectUtility.HasHediff(overseer, MAPMechanitor_HediffDefOf.MAP_RepairBeacon));

            Hediff? protocol = source.health?.hediffSet?.GetFirstHediffOfDef(
                MAPMechanitor_HediffDefOf.MAP_BattlefieldRepairProtocolActive);
            int remaining = protocol?.TryGetComp<HediffComp_Disappears>()
                ?.EffectiveTicksToDisappear ?? 0;
            if (remaining > 0)
            {
                MechFusionHealthEffectManager.AddOrRefreshTimedEffect(
                    session, StructuralRepairRuleId,
                    MAPMechanitor_HediffDefOf.MAP_FusionStructuralRepair,
                    remaining, applyImmediately: false);
            }
        }

        internal static void OnProtocolApplied(Pawn caster, int durationTicks)
        {
            foreach (MechFusionSession session in
                     GameComponent_MechFusionSessionRegistry.GetSessionsForReading())
            {
                Pawn? wearer = session.WearerPawn;
                Pawn? source = session.SourcePawn;
                if (session.State != MechFusionSessionState.Active
                    || wearer == null || wearer.Dead || wearer.Destroyed
                    || source == null || source.Dead || source.Destroyed)
                    continue;

                // 一次遍历每个会话只发放一次；不以 OriginalOverseer 代替当前关系。
                if (wearer == caster || GetCurrentOverseer(source) == caster)
                {
                    MechFusionHealthEffectManager.AddOrRefreshTimedEffect(
                        session, StructuralRepairRuleId,
                        MAPMechanitor_HediffDefOf.MAP_FusionStructuralRepair,
                        durationTicks);
                }
            }
        }

        private static Pawn? GetCurrentOverseer(Pawn source)
        {
            if (MAPMechanitorNodeUtility.HasNode(source)
                && MAPMechanitorNodeUtility.UsesVanillaControlPath(source))
                return MAPOverseerRelationDirectionUtility.FindActualOverseer(source);
            return source.GetOverseer();
        }
    }
}
