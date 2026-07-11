using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class CommanderSkillLevel
    {
        public SkillDef? skill;
        public int level;
    }

    public class CompProperties_CommanderSkills : CompProperties
    {
        public BodyTypeDef? bodyType;
        public BackstoryDef? childhoodBackstory;
        public BackstoryDef? adulthoodBackstory;
        public List<CommanderSkillLevel>? skillLevels;

        public CompProperties_CommanderSkills()
        {
            compClass = typeof(CompCommanderSkills);
        }
    }
}
