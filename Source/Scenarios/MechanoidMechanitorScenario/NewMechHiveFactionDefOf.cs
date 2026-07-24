using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    [DefOf]
    public static class NewMechHiveFactionDefOf
    {
        public static FactionDef MAP_NewMechHive = null!;

        static NewMechHiveFactionDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(NewMechHiveFactionDefOf));
        }
    }
}
