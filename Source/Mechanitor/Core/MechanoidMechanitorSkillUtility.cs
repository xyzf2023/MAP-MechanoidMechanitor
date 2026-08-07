using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    internal static class MechanoidMechanitorSkillUtility
    {
        internal static bool TryGetPreferredSkillLevel(
            Pawn? pawn,
            SkillDef? skill,
            out int level)
        {
            level = 0;
            if (pawn == null
                || skill == null
                || !MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
            {
                return false;
            }

            Pawn_SkillTracker? tracker = pawn.skills;
            if (tracker == null)
            {
                return false;
            }

            SkillRecord? record = tracker.GetSkill(skill);
            if (record == null)
            {
                return false;
            }

            // 使用 Level 而不是 levelInt，保持原版技能禁用与 aptitude 等修正。
            level = record.Level;
            return true;
        }

        internal static int ResolveMechSkillLevel(
            int vanillaMechSkillLevel,
            Pawn? pawn,
            SkillDef? skill)
        {
            return TryGetPreferredSkillLevel(pawn, skill, out int preferredLevel)
                ? preferredLevel
                : vanillaMechSkillLevel;
        }

        internal static bool TryGetPreferredWorkTypeSkillLevel(
            Pawn? pawn,
            WorkTypeDef? workType,
            out int level)
        {
            level = 0;
            if (pawn == null
                || workType?.relevantSkills == null
                || workType.relevantSkills.Count == 0
                || !MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
            {
                return false;
            }

            int minimumLevel = int.MaxValue;
            for (int i = 0; i < workType.relevantSkills.Count; i++)
            {
                SkillDef? skill = workType.relevantSkills[i];
                if (!TryGetPreferredSkillLevel(pawn, skill, out int skillLevel))
                {
                    return false;
                }

                if (skillLevel < minimumLevel)
                {
                    minimumLevel = skillLevel;
                }
            }

            if (minimumLevel == int.MaxValue)
            {
                return false;
            }

            level = minimumLevel;
            return true;
        }

        internal static void PromoteNonePassionsToMinor(Pawn? pawn)
        {
            Pawn_SkillTracker? tracker = pawn?.skills;
            if (tracker?.skills == null)
            {
                return;
            }

            for (int i = 0; i < tracker.skills.Count; i++)
            {
                SkillRecord? record = tracker.skills[i];
                if (record != null && record.passion == Passion.None)
                {
                    record.passion = Passion.Minor;
                }
            }
        }
    }
}
