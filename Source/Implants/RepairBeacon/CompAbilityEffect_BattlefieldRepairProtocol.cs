using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class CompAbilityEffect_BattlefieldRepairProtocol : CompAbilityEffect
    {
        private const int DurationTicks = 15000;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            Pawn mechanitor = parent.pawn;
            if (mechanitor?.health?.hediffSet == null)
            {
                return;
            }

            AddOrRefreshTimedHediff(
                mechanitor,
                MAPMechanitor_HediffDefOf.MAP_BeaconRepairGuidance);

            HashSet<Pawn> recipients = ImplantEffectUtility.CollectControlledMechsAndSelf(
                mechanitor,
                includeMechanoidMechanitorSelf: true);
            foreach (Pawn recipient in recipients)
            {
                AddOrRefreshTimedHediff(
                    recipient,
                    MAPMechanitor_HediffDefOf.MAP_BattlefieldRepairProtocolActive);
            }
            MechFusionRepairBeaconUtility.OnProtocolApplied(mechanitor, DurationTicks);
        }

        private static void AddOrRefreshTimedHediff(Pawn pawn, HediffDef hediffDef)
        {
            Hediff? existing = pawn.health.hediffSet.GetFirstHediffOfDef(hediffDef);
            if (existing != null)
            {
                pawn.health.RemoveHediff(existing);
            }

            Hediff added = pawn.health.AddHediff(hediffDef);
            HediffComp_Disappears? disappears = added.TryGetComp<HediffComp_Disappears>();
            if (disappears != null)
            {
                disappears.ticksToDisappear = DurationTicks;
            }
        }
    }
}
