using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class CompProperties_FreeColonistEquivalentUser : CompProperties
    {
        public CompProperties_FreeColonistEquivalentUser()
        {
            compClass = typeof(CompFreeColonistEquivalentUser);
        }
    }

    public sealed class CompFreeColonistEquivalentUser : ThingComp
    {
        public static bool IsOptedIn(Pawn? pawn) =>
            pawn?.GetComp<CompFreeColonistEquivalentUser>() != null;
    }
}
