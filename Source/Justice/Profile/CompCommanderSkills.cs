using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class CompCommanderSkills : ThingComp
    {
        private CompProperties_CommanderSkills? SkillProps => props as CompProperties_CommanderSkills;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            if (parent is not Pawn pawn || SkillProps == null)
            {
                return;
            }

            ApplyBodyType(pawn, SkillProps);
            ApplyBackstories(pawn, SkillProps);
            ApplySkillLevels(pawn, SkillProps);
        }

        private static void ApplyBodyType(Pawn pawn, CompProperties_CommanderSkills props)
        {
            if (props.bodyType == null)
            {
                return;
            }

            if (pawn.story == null)
            {
                pawn.story = new Pawn_StoryTracker(pawn);
            }

            pawn.story.bodyType = props.bodyType;
        }

        private static void ApplyBackstories(Pawn pawn, CompProperties_CommanderSkills props)
        {
            if (props.childhoodBackstory == null && props.adulthoodBackstory == null)
            {
                return;
            }

            if (pawn.story == null)
            {
                pawn.story = new Pawn_StoryTracker(pawn);
            }

            if (props.childhoodBackstory != null)
            {
                pawn.story.Childhood = props.childhoodBackstory;
            }

            if (props.adulthoodBackstory != null)
            {
                pawn.story.Adulthood = props.adulthoodBackstory;
            }
        }

        private static void ApplySkillLevels(Pawn pawn, CompProperties_CommanderSkills props)
        {
            if (props.skillLevels == null || props.skillLevels.Count == 0)
            {
                return;
            }

            if (pawn.skills == null)
            {
                pawn.skills = new Pawn_SkillTracker(pawn);
            }

            for (int i = 0; i < props.skillLevels.Count; i++)
            {
                CommanderSkillLevel entry = props.skillLevels[i];
                if (entry?.skill == null)
                {
                    continue;
                }

                SkillRecord? record = pawn.skills.GetSkill(entry.skill);
                if (record == null)
                {
                    continue;
                }

                record.Level = entry.level;
            }
        }
    }
}
