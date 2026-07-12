using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class MechanoidBackstoryUtility
    {
        private const string LoverDefName = "MAP_Mech_Lover";
        private const string GenericChildhoodDefName = "MAP_GenericMechanoid_Childhood";
        private const string GenericAdulthoodDefName = "MAP_GenericMechanoid_Adulthood";

        private static BackstoryDef? cachedGenericChildhood;
        private static BackstoryDef? cachedGenericAdulthood;

        public static void EnsureGenericBackstories(Pawn? pawn)
        {
            if (pawn == null || pawn.Destroyed || !pawn.RaceProps.IsMechanoid)
            {
                return;
            }

            if (pawn.kindDef == null)
            {
                return;
            }

            if (ShouldSkipDedicatedBackstoryPawn(pawn))
            {
                return;
            }

            pawn.story ??= new Pawn_StoryTracker(pawn);
            pawn.story.traits ??= new TraitSet(pawn);

            BackstoryDef? genericChildhood = GetGenericChildhood();
            BackstoryDef? genericAdulthood = GetGenericAdulthood();

            if (pawn.story.Childhood == null && genericChildhood != null)
            {
                pawn.story.Childhood = genericChildhood;
            }

            if (pawn.story.Adulthood == null && genericAdulthood != null)
            {
                pawn.story.Adulthood = genericAdulthood;
            }
        }

        private static bool ShouldSkipDedicatedBackstoryPawn(Pawn pawn)
        {
            if (JusticePawnUtility.IsJustice(pawn) || pawn.def?.defName == LoverDefName)
            {
                return true;
            }

            return HasDedicatedBackstoryComp(pawn.def);
        }

        private static bool HasDedicatedBackstoryComp(ThingDef? def)
        {
            if (def?.comps == null)
            {
                return false;
            }

            for (int i = 0; i < def.comps.Count; i++)
            {
                CompProperties compProps = def.comps[i];
                if (compProps is CompProperties_ColonistLikeMechProfile colonistProps
                    && (colonistProps.childhoodBackstory != null
                        || colonistProps.adulthoodBackstory != null))
                {
                    return true;
                }

                if (compProps is CompProperties_CommanderSkills commanderProps
                    && (commanderProps.childhoodBackstory != null
                        || commanderProps.adulthoodBackstory != null))
                {
                    return true;
                }
            }

            return false;
        }

        private static BackstoryDef? GetGenericChildhood()
        {
            return cachedGenericChildhood ??=
                DefDatabase<BackstoryDef>.GetNamedSilentFail(GenericChildhoodDefName);
        }

        private static BackstoryDef? GetGenericAdulthood()
        {
            return cachedGenericAdulthood ??=
                DefDatabase<BackstoryDef>.GetNamedSilentFail(GenericAdulthoodDefName);
        }
    }
}
