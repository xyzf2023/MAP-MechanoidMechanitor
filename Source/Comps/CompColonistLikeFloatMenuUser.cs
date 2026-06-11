using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class CompProperties_ColonistLikeFloatMenuUser : CompProperties
    {
        public bool allowColonistLikeFloatMenu = true;

        public CompProperties_ColonistLikeFloatMenuUser()
        {
            compClass = typeof(CompColonistLikeFloatMenuUser);
        }
    }

    public class CompColonistLikeFloatMenuUser : ThingComp
    {
        public CompProperties_ColonistLikeFloatMenuUser Props =>
            (CompProperties_ColonistLikeFloatMenuUser)props;

        public static bool PawnCanUseColonistLikeFloatMenu(Pawn? pawn)
        {
            if (pawn == null || pawn.Dead)
            {
                return false;
            }

            if (pawn.Faction == null || !pawn.Faction.IsPlayerSafe())
            {
                return false;
            }

            CompColonistLikeFloatMenuUser? comp = pawn.GetComp<CompColonistLikeFloatMenuUser>();
            if (comp == null)
            {
                return false;
            }

            return comp.Props.allowColonistLikeFloatMenu;
        }
    }
}
