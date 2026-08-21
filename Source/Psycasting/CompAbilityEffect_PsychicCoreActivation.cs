using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class CompAbilityEffect_PsychicCoreActivation : CompAbilityEffect
    {
        private const int DurationTicks = 15000;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);

            Pawn pawn = parent.pawn;
            HediffDef? activationDef = PsychicCoreUtility.PsychicActivationHediffDef;
            if (pawn?.health?.hediffSet == null
                || activationDef == null
                || !PsychicCoreUtility.HasPsychicCore(pawn))
            {
                return;
            }

            if (pawn.RaceProps.IsMechanoid)
            {
                MechanoidMechanitorPsycastUtility.EnsurePsycastInfrastructure(pawn);
            }

            if (pawn.psychicEntropy == null)
            {
                return;
            }

            pawn.psychicEntropy.RechargePsyfocus();

            Hediff? existing =
                pawn.health.hediffSet.GetFirstHediffOfDef(activationDef);
            if (existing != null)
            {
                pawn.health.RemoveHediff(existing);
            }

            Hediff active = pawn.health.AddHediff(activationDef);
            HediffComp_Disappears? disappears =
                active.TryGetComp<HediffComp_Disappears>();
            if (disappears != null)
            {
                disappears.ticksToDisappear = DurationTicks;
            }
        }
    }
}
