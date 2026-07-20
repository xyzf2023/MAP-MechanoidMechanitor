using System;
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

        public override Window ConfirmationDialog(
            LocalTargetInfo target,
            Action confirmAction)
        {
            Pawn? targetPawn = target.Pawn;
            Faction? targetFaction = targetPawn?.Faction;

            if (targetFaction != null
                && targetFaction != Faction.OfPlayer
                && !targetFaction.HostileTo(Faction.OfPlayer))
            {
                return Dialog_MessageBox.CreateConfirmation(
                    Props.nonHostileConfirmMessageKey.Translate(),
                    confirmAction);
            }

            return null!;
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
                && targetPawn.OverseerSubject != null
                && !JusticePawnUtility.IsBossJustice(targetPawn);
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
        }
    }
}
