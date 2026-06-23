using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class CompAbilityEffect_MechReconstruction : CompAbilityEffect
    {
        public new CompProperties_AbilityMechReconstruction Props =>
            (CompProperties_AbilityMechReconstruction)props;

        public override bool CanApplyOn(LocalTargetInfo target, LocalTargetInfo dest)
        {
            return base.CanApplyOn(target, dest)
                && target.HasThing
                && target.Thing is Corpse corpse
                && CanReconstruct(corpse);
        }

        public override bool CanApplyOn(GlobalTargetInfo target)
        {
            return target.HasThing
                && target.Thing is Corpse corpse
                && CanReconstruct(corpse);
        }

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            if (!base.Valid(target, throwMessages))
            {
                return false;
            }

            if (target.HasThing && target.Thing is Corpse corpse && CanReconstruct(corpse))
            {
                return true;
            }

            if (throwMessages)
            {
                Messages.Message(
                    Props.invalidTargetMessageKey.Translate(),
                    parent.pawn,
                    MessageTypeDefOf.RejectInput,
                    false);
            }

            return false;
        }

        public bool CanReconstruct(Corpse? corpse)
        {
            Pawn? innerPawn = corpse?.InnerPawn;
            Pawn caster = parent.pawn;

            return corpse != null
                && !corpse.Destroyed
                && innerPawn != null
                && innerPawn.Dead
                && innerPawn.RaceProps.IsMechanoid
                && caster != null
                && caster.Faction != null
                && innerPawn.Faction == caster.Faction;
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
        }
    }
}
