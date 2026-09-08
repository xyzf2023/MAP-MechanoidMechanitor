using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class CompCommanderSkills : ThingComp
    {
        private bool skillsInitialized;

        private CompProperties_CommanderSkills? SkillProps => props as CompProperties_CommanderSkills;

        public override void PostExposeData()
        {
            base.PostExposeData();
            // 旧存档缺少该字段时默认 true，避免更新 MOD 后首次读档重置已有技能。
            Scribe_Values.Look(ref skillsInitialized, "skillsInitialized", true);
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);

            if (parent is not Pawn pawn || SkillProps == null)
            {
                return;
            }

            ApplyBodyType(pawn, SkillProps);
            ApplyBackstories(pawn, SkillProps);
            EnsureSkillsInitialized(pawn, SkillProps);
        }

        private void EnsureSkillsInitialized(Pawn pawn, CompProperties_CommanderSkills props)
        {
            bool trackerWasMissing = pawn.skills == null;
            if (trackerWasMissing)
            {
                pawn.skills = new Pawn_SkillTracker(pawn);
            }

            // 仅在首次初始化，或技能 Tracker 异常缺失时写入 XML 初始技能。
            // 不以 respawningAfterLoad 判断：远行队 / 运输舱等重入图也会走 PostSpawnSetup。
            if (!skillsInitialized || trackerWasMissing)
            {
                ApplySkillLevels(pawn, props);
                if (MechanoidMechanitorRoleUtility.HasNativeMechanitorMarker(pawn))
                {
                    // 只在真正首次完成技能初始化时读取“最低为好奇”设置并应用兴趣下限；
                    // 玩家之后修改设置、读档、组件重建或角色重入图都不会追溯调整既有兴趣。
                    MechanoidMechanitorSkillUtility.ApplyConfiguredInitialPassionFloor(pawn);
                }

                skillsInitialized = true;
            }
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
