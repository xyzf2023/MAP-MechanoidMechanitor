using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [DefOf]
    public static class MAP_JusticeBossDefOf
    {
        public static PawnKindDef MAP_Mech_JusticeBOSS = null!;

        public static BossDef MAP_JusticeBoss = null!;

        public static BossgroupDef MAP_JusticeBossGroup = null!;

        public static QuestScriptDef MAP_JusticeBossGroupQuest = null!;

        static MAP_JusticeBossDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(MAP_JusticeBossDefOf));
        }
    }
}