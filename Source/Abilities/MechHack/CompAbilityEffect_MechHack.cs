using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class CompAbilityEffect_MechHack : CompAbilityEffect
    {
        public new CompProperties_AbilityMechHack Props =>
            (CompProperties_AbilityMechHack)props;

        public override bool CanApplyOn(LocalTargetInfo target, LocalTargetInfo dest)
        {
            return base.CanApplyOn(target, dest)
                && target.HasThing
                && target.Thing is Pawn targetPawn
                && CanHack(targetPawn, parent.pawn);
        }

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            if (!base.Valid(target, throwMessages))
            {
                return false;
            }

            if (target.HasThing
                && target.Thing is Pawn targetPawn
                && CanHack(targetPawn, parent.pawn))
            {
                return true;
            }

            if (throwMessages)
            {
                Messages.Message(
                    Props.invalidTargetMessageKey.Translate(),
                    parent.pawn,
                    MessageTypeDefOf.RejectInput,
                    historical: false);
            }

            return false;
        }

        public bool CanHack(Pawn? targetPawn, Pawn? caster)
        {
            if (targetPawn == null || caster == null || caster.Map == null)
            {
                return false;
            }

            return targetPawn.Spawned
                && !targetPawn.Dead
                && targetPawn.Map == caster.Map
                && targetPawn.RaceProps.IsMechanoid
                && targetPawn.Faction != Faction.OfPlayer
                && targetPawn.OverseerSubject != null;
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
        }
    }
}
