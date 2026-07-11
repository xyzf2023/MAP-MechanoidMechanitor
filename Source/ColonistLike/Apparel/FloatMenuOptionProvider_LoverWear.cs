using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class FloatMenuOptionProvider_LoverWear : FloatMenuOptionProvider_Wear
    {
        protected override bool MechanoidCanDo => true;

        protected override bool AppliesInt(FloatMenuContext context)
        {
            Pawn pawn = context.FirstSelectedPawn;

            return pawn != null
                && HumanApparelUtility.CanUseWearFloatMenu(pawn)
                && pawn.apparel != null;
        }
    }
}
