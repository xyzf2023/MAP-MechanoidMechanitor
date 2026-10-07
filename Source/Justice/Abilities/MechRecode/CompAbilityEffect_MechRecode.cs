using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class CompAbilityEffect_MechRecode : CompAbilityEffect
    {
        private const string InvalidTargetMessageKey = "MAP_MechanoidMechanitor.Justice.Ability.MechRecode.InvalidTarget";
        private const string UnsupportedSystemMessageKey = "MAP_MechanoidMechanitor.Justice.Ability.MechRecode.UnsupportedSystem";

        public new CompProperties_AbilityMechRecode Props =>
            (CompProperties_AbilityMechRecode)props;

        public override bool CanApplyOn(LocalTargetInfo target, LocalTargetInfo dest)
        {
            return base.CanApplyOn(target, dest)
                && target.HasThing
                && target.Thing is Corpse corpse
                && CanRecode(corpse);
        }

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            if (!base.Valid(target, throwMessages))
            {
                return false;
            }

            string rejectionMessageKey = InvalidTargetMessageKey;
            if (target.HasThing
                && target.Thing is Corpse corpse
                && CanRecode(corpse, out rejectionMessageKey))
            {
                return true;
            }

            if (throwMessages)
            {
                Messages.Message(
                    rejectionMessageKey.Translate(),
                    parent.pawn,
                    MessageTypeDefOf.RejectInput,
                    historical: false);
            }

            return false;
        }

        public bool CanRecode(Corpse? corpse)
        {
            return CanRecode(corpse, out _);
        }

        public bool CanRecode(Corpse? corpse, out string rejectionMessageKey)
        {
            rejectionMessageKey = InvalidTargetMessageKey;
            Pawn? innerPawn = corpse?.InnerPawn;

            if (innerPawn == null
                || !innerPawn.Dead
                || !innerPawn.RaceProps.IsMechanoid
                || innerPawn.Faction == Faction.OfPlayer
                || MechAbilityTargetUtility.IsProtectedBoss(innerPawn))
            {
                return false;
            }

            if (!MassProductionMechGestatorRecipeRegistry.HasProductionOrResurrectionRecipe(corpse))
            {
                rejectionMessageKey = UnsupportedSystemMessageKey;
                return false;
            }

            return true;
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            // The actual effect is performed by JobDriver_MechRecode after the pawn
            // reaches the corpse and completes the recoding progress bar.
        }
    }
}
