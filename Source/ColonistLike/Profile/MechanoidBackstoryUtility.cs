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
            if (!CanInitializeBackstories(pawn))
            {
                return;
            }

            if (ShouldSkipDedicatedBackstoryPawn(pawn!))
            {
                return;
            }

            EnsureStoryTracker(pawn!);
            ApplyMissingBackstories(
                pawn!,
                GetGenericChildhood(),
                GetGenericAdulthood());
        }

        /// <summary>
        /// 心智数据导出后恢复机体原本应有的背景故事。
        /// 正义、恋人及其他声明了专属背景的机体读取自身组件配置；
        /// 普通机械族则使用通用“机械体”背景。
        /// </summary>
        public static void RestoreBaselineBackstories(Pawn? pawn)
        {
            if (!CanInitializeBackstories(pawn))
            {
                return;
            }

            EnsureStoryTracker(pawn!);
            if (TryGetDedicatedBackstories(
                    pawn!.def,
                    out BackstoryDef? childhood,
                    out BackstoryDef? adulthood))
            {
                ApplyMissingBackstories(pawn, childhood, adulthood);
                return;
            }

            ApplyMissingBackstories(
                pawn,
                GetGenericChildhood(),
                GetGenericAdulthood());
        }

        private static bool CanInitializeBackstories(Pawn? pawn)
        {
            return pawn != null
                && !pawn.Destroyed
                && pawn.RaceProps.IsMechanoid
                && pawn.kindDef != null;
        }

        private static void EnsureStoryTracker(Pawn pawn)
        {
            pawn.story ??= new Pawn_StoryTracker(pawn);
            pawn.story.traits ??= new TraitSet(pawn);
        }

        private static void ApplyMissingBackstories(
            Pawn pawn,
            BackstoryDef? childhood,
            BackstoryDef? adulthood)
        {
            if (pawn.story!.Childhood == null && childhood != null)
            {
                pawn.story.Childhood = childhood;
            }

            if (pawn.story.Adulthood == null && adulthood != null)
            {
                pawn.story.Adulthood = adulthood;
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
            return TryGetDedicatedBackstories(
                def,
                out _,
                out _);
        }

        private static bool TryGetDedicatedBackstories(
            ThingDef? def,
            out BackstoryDef? childhood,
            out BackstoryDef? adulthood)
        {
            childhood = null;
            adulthood = null;
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
                    childhood = colonistProps.childhoodBackstory;
                    adulthood = colonistProps.adulthoodBackstory;
                    return true;
                }

                if (compProps is CompProperties_CommanderSkills commanderProps
                    && (commanderProps.childhoodBackstory != null
                        || commanderProps.adulthoodBackstory != null))
                {
                    childhood = commanderProps.childhoodBackstory;
                    adulthood = commanderProps.adulthoodBackstory;
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
