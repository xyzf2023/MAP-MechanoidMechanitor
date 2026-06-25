using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 为仍保持机械体种族身份的 Pawn 补齐类殖民者档案、技能与相关 Tracker。
    /// 该组件只作用于显式挂载它的种族，不会影响普通机械体。
    /// </summary>
    public class CompColonistLikeMechProfile : ThingComp
    {
        private int initializedProfileVersion;

        private CompProperties_ColonistLikeMechProfile? ProfileProps =>
            props as CompProperties_ColonistLikeMechProfile;

        public override void PostPostMake()
        {
            base.PostPostMake();
            EnsureProfile();
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            EnsureProfile();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref initializedProfileVersion, "initializedProfileVersion", 0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EnsureProfile();
            }
        }

        private void EnsureProfile()
        {
            if (parent is not Pawn pawn || ProfileProps == null)
            {
                return;
            }

            EnsureStory(pawn, ProfileProps);

            bool shouldInitializeSkillLevels =
                pawn.skills == null || initializedProfileVersion < ProfileProps.profileVersion;
            EnsureSkills(pawn, ProfileProps, shouldInitializeSkillLevels);

            EnsureRelations(pawn, ProfileProps);
            EnsureInteractions(pawn, ProfileProps);
            EnsureGuest(pawn, ProfileProps);
            EnsureGuilt(pawn, ProfileProps);
            EnsureWorkSettings(pawn, ProfileProps);
            EnsureGenes(pawn, ProfileProps);

            if (shouldInitializeSkillLevels)
            {
                initializedProfileVersion = ProfileProps.profileVersion;
            }
        }

        private static void EnsureStory(Pawn pawn, CompProperties_ColonistLikeMechProfile props)
        {
            pawn.story ??= new Pawn_StoryTracker(pawn);
            pawn.story.traits ??= new TraitSet(pawn);

            if (props.bodyType != null)
            {
                pawn.story.bodyType = props.bodyType;
            }

            if (props.hairDef != null)
            {
                pawn.story.hairDef = props.hairDef;
            }

            if (props.setHairColor)
            {
                pawn.story.HairColor = props.hairColor;
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

        private static void EnsureSkills(
            Pawn pawn,
            CompProperties_ColonistLikeMechProfile props,
            bool initializeLevels)
        {
            pawn.skills ??= new Pawn_SkillTracker(pawn);

            if (!initializeLevels)
            {
                return;
            }

            List<SkillDef> allSkills = DefDatabase<SkillDef>.AllDefsListForReading;
            for (int i = 0; i < allSkills.Count; i++)
            {
                SkillRecord? record = pawn.skills.GetSkill(allSkills[i]);
                if (record != null)
                {
                    record.Level = 0;
                }
            }

            if (props.skillLevels == null)
            {
                return;
            }

            for (int i = 0; i < props.skillLevels.Count; i++)
            {
                ColonistLikeMechSkillLevel entry = props.skillLevels[i];
                if (entry?.skill == null)
                {
                    continue;
                }

                SkillRecord? record = pawn.skills.GetSkill(entry.skill);
                if (record == null)
                {
                    continue;
                }

                int level = entry.level;
                if (level < 0)
                {
                    level = 0;
                }
                else if (level > 20)
                {
                    level = 20;
                }

                record.Level = level;
            }
        }

        private static void EnsureRelations(Pawn pawn, CompProperties_ColonistLikeMechProfile props)
        {
            if (props.ensureRelationsTracker && pawn.relations == null)
            {
                pawn.relations = new Pawn_RelationsTracker(pawn);
            }
        }

        private static void EnsureInteractions(Pawn pawn, CompProperties_ColonistLikeMechProfile props)
        {
            if (props.ensureInteractionsTracker && pawn.interactions == null)
            {
                pawn.interactions = new Pawn_InteractionsTracker(pawn);
            }
        }

        private static void EnsureGuest(Pawn pawn, CompProperties_ColonistLikeMechProfile props)
        {
            if (props.ensureGuestTracker && pawn.guest == null)
            {
                pawn.guest = new Pawn_GuestTracker(pawn);
            }
        }

        private static void EnsureGuilt(Pawn pawn, CompProperties_ColonistLikeMechProfile props)
        {
            if (props.ensureGuiltTracker && pawn.guilt == null)
            {
                pawn.guilt = new Pawn_GuiltTracker(pawn);
            }
        }

        private static void EnsureWorkSettings(Pawn pawn, CompProperties_ColonistLikeMechProfile props)
        {
            if (props.ensureWorkSettings && pawn.workSettings == null)
            {
                pawn.workSettings = new Pawn_WorkSettings(pawn);
            }
        }

        private static void EnsureGenes(Pawn pawn, CompProperties_ColonistLikeMechProfile props)
        {
            if (props.ensureGeneTracker && ModsConfig.BiotechActive && pawn.genes == null)
            {
                pawn.genes = new Pawn_GeneTracker(pawn);
            }
        }
    }

    public class ColonistLikeMechSkillLevel
    {
        public SkillDef? skill;
        public int level;
    }

    public class CompProperties_ColonistLikeMechProfile : CompProperties
    {
        public int profileVersion = 1;
        public BodyTypeDef? bodyType;
        public HairDef? hairDef;
        public bool setHairColor;
        public Color hairColor = Color.white;
        public BackstoryDef? childhoodBackstory;
        public BackstoryDef? adulthoodBackstory;
        public List<ColonistLikeMechSkillLevel>? skillLevels;
        public bool ensureRelationsTracker = true;
        public bool ensureInteractionsTracker = true;
        public bool ensureGuestTracker = true;
        public bool ensureGuiltTracker = true;
        public bool ensureWorkSettings = true;
        public bool ensureGeneTracker = true;

        public CompProperties_ColonistLikeMechProfile()
        {
            compClass = typeof(CompColonistLikeMechProfile);
        }
    }
}
