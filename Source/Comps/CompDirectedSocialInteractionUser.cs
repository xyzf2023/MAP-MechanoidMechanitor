using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class CompProperties_DirectedSocialInteractionUser : CompProperties
    {
        public bool ensureInteractionsTracker = true;

        public CompProperties_DirectedSocialInteractionUser()
        {
            compClass = typeof(CompDirectedSocialInteractionUser);
        }
    }

    public class CompDirectedSocialInteractionUser : ThingComp
    {
        private CompProperties_DirectedSocialInteractionUser? Props =>
            props as CompProperties_DirectedSocialInteractionUser;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);

            if (parent is not Pawn pawn)
            {
                return;
            }

            if (Props == null || !Props.ensureInteractionsTracker)
            {
                return;
            }

            EnsureInteractionsTracker(pawn);
        }

        private static void EnsureInteractionsTracker(Pawn pawn)
        {
            if (pawn.interactions == null)
            {
                pawn.interactions = new Pawn_InteractionsTracker(pawn);
            }
        }
    }
}
