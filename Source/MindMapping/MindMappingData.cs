using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class MindMappingTraitData : IExposable
    {
        public TraitDef? Def;
        public int Degree;

        public MindMappingTraitData()
        {
        }

        public MindMappingTraitData(Trait trait)
        {
            Def = trait.def;
            Degree = trait.Degree;
        }

        public void ExposeData()
        {
            Scribe_Defs.Look(ref Def, "def");
            Scribe_Values.Look(ref Degree, "degree", 0);
        }
    }

    public sealed class MindMappingSkillData : IExposable
    {
        public SkillDef? Def;
        public int Level;
        public Passion PassionValue;
        public bool PassionRecorded;

        public MindMappingSkillData()
        {
        }

        public MindMappingSkillData(SkillRecord skill)
        {
            Def = skill.def;
            Level = skill.Level;
            PassionValue = skill.passion;
            PassionRecorded = true;
        }

        public void ExposeData()
        {
            Scribe_Defs.Look(ref Def, "def");
            Scribe_Values.Look(ref Level, "level", 0);
            Scribe_Values.Look(ref PassionValue, "passion", RimWorld.Passion.None);
            Scribe_Values.Look(ref PassionRecorded, "passionRecorded", false);

            // 旧心智快照兼容：技能兴趣未记录（PassionRecorded == false）时，永久归一化为
            // “无兴趣”（Passion.None）并标记为已记录，玩家下次保存后写入存档。
            // 深度保存的列表元素只会在 LoadingVars 阶段进入 ExposeData（Scribe_Collections 对
            // List<Deep> 没有 PostLoadInit 分支），故在读取完成阶段（LoadingVars）完成归一化。
            // Scribe 键值名称（passion / passionRecorded）保持不变。新扫描数据已设
            // PassionRecorded = true 且保存真实兴趣，此分支不会触发，不会改写真实兴趣。
            if (Scribe.mode == LoadSaveMode.LoadingVars && !PassionRecorded)
            {
                PassionValue = RimWorld.Passion.None;
                PassionRecorded = true;
            }
        }
    }

    /// <summary>
    /// 随核心实例保存的心智快照。它不引用原 Pawn，因此扫描目标离图或死亡后仍可使用。
    /// </summary>
    public sealed class MindMappingData : IExposable
    {
        private static readonly FieldInfo ChildhoodField = AccessTools.Field(
            typeof(Pawn_StoryTracker),
            "childhood");

        private static readonly FieldInfo BackstoriesCacheField = AccessTools.Field(
            typeof(Pawn_StoryTracker),
            "backstoriesCache");

        private bool tripleName;
        private bool numericalSingleName;
        private string firstName = string.Empty;
        private string nickName = string.Empty;
        private string lastName = string.Empty;
        private string singleName = string.Empty;
        private long chronologicalAgeTicks;
        private long biologicalAgeTicks;
        private bool biologicalAgeRecorded;
        private Gender gender;
        private bool genderRecorded;
        private bool ideologyRecorded;
        private bool hadIdeology;
        private Ideo? ideology;
        private string ideologyName = string.Empty;
        private WorkTags disabledWorkTags;
        private bool disabledWorkTagsRecorded;
        private BackstoryDef? childhood;
        private BackstoryDef? adulthood;
        private List<MindMappingTraitData> traits = new List<MindMappingTraitData>();
        private List<MindMappingSkillData> skills = new List<MindMappingSkillData>();

        public IReadOnlyList<MindMappingTraitData> Traits => traits;

        public IReadOnlyList<MindMappingSkillData> Skills => skills;

        public BackstoryDef? Childhood => childhood;

        public BackstoryDef? Adulthood => adulthood;

        public string FullName => BuildName().ToStringFull;

        public int ChronologicalAgeYears
            => (int)(chronologicalAgeTicks / GenDate.TicksPerYear);

        public long ChronologicalAgeTicks => chronologicalAgeTicks;

        public bool BiologicalAgeRecorded => biologicalAgeRecorded;

        public int BiologicalAgeYears
            => (int)(biologicalAgeTicks / GenDate.TicksPerYear);

        public long BiologicalAgeTicks => biologicalAgeTicks;

        /// <summary>
        /// 只读格式化：参照原版 Pawn_AgeTracker.AgeNumberString。
        /// 生物年龄与历法年龄不同年时以"生理 (历法)"形式返回；生物年龄未记录时退化为历法年龄；
        /// 完全没有年龄数据时返回空字符串，由显示层安全回退。
        /// </summary>
        public string AgeNumberString
        {
            get
            {
                if (biologicalAgeRecorded)
                {
                    string text = ((float)biologicalAgeTicks / GenDate.TicksPerYear)
                        .ToStringApproxAge();
                    if (chronologicalAgeTicks / GenDate.TicksPerYear
                        != biologicalAgeTicks / GenDate.TicksPerYear)
                    {
                        text += " (" + chronologicalAgeTicks / GenDate.TicksPerYear + ")";
                    }

                    return text;
                }

                if (chronologicalAgeTicks > 0L)
                {
                    return ((float)chronologicalAgeTicks / GenDate.TicksPerYear)
                        .ToStringApproxAge();
                }

                return string.Empty;
            }
        }

        public bool GenderRecorded => genderRecorded;

        public Gender Gender => gender;

        public bool IdeologyRecorded => ideologyRecorded;

        public bool HadIdeology => hadIdeology;

        public Ideo? Ideology => ideology;

        public string IdeologyName
        {
            get
            {
                if (ideology != null && !ideology.name.NullOrEmpty())
                {
                    return ideology.name;
                }

                return ideologyName;
            }
        }

        public bool DisabledWorkTagsRecorded => disabledWorkTagsRecorded;

        public WorkTags DisabledWorkTags => disabledWorkTags;

        public string ShortName
        {
            get
            {
                if (tripleName)
                {
                    return nickName.NullOrEmpty() ? firstName : nickName;
                }

                return singleName.NullOrEmpty() ? "?" : singleName;
            }
        }

        public static MindMappingData Capture(Pawn pawn)
        {
            Ideo? pawnIdeology = pawn.Ideo;
            MindMappingData data = new MindMappingData
            {
                chronologicalAgeTicks = pawn.ageTracker?.AgeChronologicalTicks ?? 0L,
                biologicalAgeTicks = pawn.ageTracker?.AgeBiologicalTicks ?? 0L,
                biologicalAgeRecorded = pawn.ageTracker != null,
                gender = pawn.gender,
                genderRecorded = true,
                ideologyRecorded = true,
                hadIdeology = pawnIdeology != null,
                ideology = pawnIdeology,
                ideologyName = pawnIdeology == null
                    ? string.Empty
                    : (pawnIdeology.name.NullOrEmpty()
                        ? pawnIdeology.ToString()
                        : pawnIdeology.name),
                disabledWorkTags = pawn.CombinedDisabledWorkTags,
                disabledWorkTagsRecorded = true,
                childhood = pawn.story?.Childhood,
                adulthood = pawn.story?.Adulthood
            };

            if (pawn.Name is NameTriple triple)
            {
                data.tripleName = true;
                data.firstName = triple.First ?? string.Empty;
                data.nickName = triple.Nick ?? string.Empty;
                data.lastName = triple.Last ?? string.Empty;
            }
            else if (pawn.Name is NameSingle single)
            {
                data.singleName = single.Name ?? string.Empty;
                data.numericalSingleName = single.Numerical;
            }
            else
            {
                data.singleName = pawn.LabelShort;
            }

            List<Trait>? allTraits = pawn.story?.traits?.allTraits;
            if (allTraits != null)
            {
                for (int i = 0; i < allTraits.Count; i++)
                {
                    Trait trait = allTraits[i];
                    if (trait != null && trait.def != null && trait.sourceGene == null && !trait.Suppressed)
                    {
                        data.traits.Add(new MindMappingTraitData(trait));
                    }
                }
            }

            List<SkillRecord>? allSkills = pawn.skills?.skills;
            if (allSkills != null)
            {
                for (int i = 0; i < allSkills.Count; i++)
                {
                    SkillRecord skill = allSkills[i];
                    if (skill?.def != null)
                    {
                        data.skills.Add(new MindMappingSkillData(skill));
                    }
                }
            }

            return data;
        }

        public void ApplyTo(Pawn pawn)
        {
            MechanoidMechanitorRoleUtility.EnsureRoleState(pawn);
            EnsurePersonalityTrackers(pawn);

            pawn.Name = BuildName();
            if (pawn.ageTracker != null)
            {
                pawn.ageTracker.AgeChronologicalTicks = chronologicalAgeTicks;
            }

            SetBackstories(pawn, childhood, adulthood);
            ReplaceNonGeneTraits(pawn, traits);

            for (int i = 0; i < skills.Count; i++)
            {
                MindMappingSkillData saved = skills[i];
                if (saved.Def == null)
                {
                    continue;
                }

                SkillRecord record = GetOrCreateSkill(pawn, saved.Def);
                if (record.Level < saved.Level)
                {
                    record.Level = saved.Level;
                }
            }

            NotifyPersonalityChanged(pawn);
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref tripleName, "tripleName", false);
            Scribe_Values.Look(ref numericalSingleName, "numericalSingleName", false);
            Scribe_Values.Look(ref firstName, "firstName", string.Empty);
            Scribe_Values.Look(ref nickName, "nickName", string.Empty);
            Scribe_Values.Look(ref lastName, "lastName", string.Empty);
            Scribe_Values.Look(ref singleName, "singleName", string.Empty);
            Scribe_Values.Look(ref chronologicalAgeTicks, "chronologicalAgeTicks", 0L);
            Scribe_Values.Look(ref biologicalAgeTicks, "biologicalAgeTicks", 0L);
            Scribe_Values.Look(ref biologicalAgeRecorded, "biologicalAgeRecorded", false);
            Scribe_Values.Look(ref gender, "gender", Gender.None);
            Scribe_Values.Look(ref genderRecorded, "genderRecorded", false);
            Scribe_Values.Look(ref ideologyRecorded, "ideologyRecorded", false);
            Scribe_Values.Look(ref hadIdeology, "hadIdeology", false);
            Scribe_References.Look(ref ideology, "ideology");
            Scribe_Values.Look(ref ideologyName, "ideologyName", string.Empty);
            Scribe_Values.Look(ref disabledWorkTags, "disabledWorkTags", WorkTags.None);
            Scribe_Values.Look(
                ref disabledWorkTagsRecorded,
                "disabledWorkTagsRecorded",
                false);
            Scribe_Defs.Look(ref childhood, "childhood");
            Scribe_Defs.Look(ref adulthood, "adulthood");
            Scribe_Collections.Look(ref traits, "traits", LookMode.Deep);
            Scribe_Collections.Look(ref skills, "skills", LookMode.Deep);
            ideologyName ??= string.Empty;
            traits ??= new List<MindMappingTraitData>();
            skills ??= new List<MindMappingSkillData>();

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                // 仅清理列表中的 null 元素（显示层仍保留空值保护）。不得删除“对象存在但 Def 为空”
                // 的条目，以免改写存档含义；其显示由 MindMappingCharacterCardUtility 静默跳过。
                // 不改变有效条目的顺序、等级、兴趣或特性度数。
                traits.RemoveAll((MindMappingTraitData t) => t == null);
                skills.RemoveAll((MindMappingSkillData s) => s == null);
            }
        }

        private Name BuildName()
        {
            return tripleName
                ? (Name)new NameTriple(firstName, nickName, lastName)
                : new NameSingle(singleName, numericalSingleName);
        }

        private static void EnsurePersonalityTrackers(Pawn pawn)
        {
            pawn.story ??= new Pawn_StoryTracker(pawn);
            pawn.story.traits ??= new TraitSet(pawn);
            pawn.skills ??= new Pawn_SkillTracker(pawn);
            pawn.skills.skills ??= new List<SkillRecord>();
        }

        private static SkillRecord GetOrCreateSkill(Pawn pawn, SkillDef def)
        {
            List<SkillRecord> records = pawn.skills!.skills;
            for (int i = 0; i < records.Count; i++)
            {
                if (records[i].def == def)
                {
                    return records[i];
                }
            }

            SkillRecord created = new SkillRecord(pawn, def);
            records.Add(created);
            return created;
        }

        private static void ReplaceNonGeneTraits(Pawn pawn, List<MindMappingTraitData> savedTraits)
        {
            TraitSet traitSet = pawn.story!.traits;
            for (int i = traitSet.allTraits.Count - 1; i >= 0; i--)
            {
                Trait existing = traitSet.allTraits[i];
                if (existing.sourceGene == null)
                {
                    traitSet.RemoveTrait(existing);
                }
            }

            for (int i = 0; i < savedTraits.Count; i++)
            {
                MindMappingTraitData saved = savedTraits[i];
                if (saved.Def != null)
                {
                    traitSet.GainTrait(new Trait(saved.Def, saved.Degree), suppressConflicts: true);
                }
            }

            traitSet.RecalculateSuppression();
        }

        internal static void ResetToAcquiredBaseline(Pawn pawn)
        {
            EnsurePersonalityTrackers(pawn);
            SetBackstories(pawn, null, null);
            MechanoidBackstoryUtility.RestoreBaselineBackstories(pawn);
            ReplaceNonGeneTraits(pawn, new List<MindMappingTraitData>());

            List<SkillRecord> records = pawn.skills!.skills;
            for (int i = 0; i < records.Count; i++)
            {
                records[i].Level = 0;
                records[i].xpSinceLastLevel = 0f;
                records[i].xpSinceMidnight = 0f;
            }

            SetExactSkill(pawn, SkillDefOf.Shooting, 18);
            SetExactSkill(pawn, SkillDefOf.Melee, 16);
            SetExactSkill(pawn, SkillDefOf.Social, 12);
            SetExactSkill(pawn, SkillDefOf.Crafting, 16);
            SetExactSkill(pawn, SkillDefOf.Construction, 10);
            SetExactSkill(pawn, SkillDefOf.Mining, 6);
            SetExactSkill(pawn, SkillDefOf.Cooking, 6);
            SetExactSkill(pawn, SkillDefOf.Plants, 6);
            SetExactSkill(pawn, SkillDefOf.Animals, 6);
            SetExactSkill(pawn, SkillDefOf.Artistic, 10);
            SetExactSkill(pawn, SkillDefOf.Medicine, 6);
            SetExactSkill(pawn, SkillDefOf.Intellectual, 12);

            pawn.Name = null;
            pawn.GenerateNecessaryName();
            if (pawn.ageTracker != null)
            {
                pawn.ageTracker.AgeChronologicalTicks = pawn.ageTracker.AgeBiologicalTicks;
            }

            NotifyPersonalityChanged(pawn);
        }

        private static void SetExactSkill(Pawn pawn, SkillDef def, int level)
        {
            SkillRecord skill = GetOrCreateSkill(pawn, def);
            skill.Level = level;
            skill.xpSinceLastLevel = 0f;
            skill.xpSinceMidnight = 0f;
        }

        private static void SetBackstories(
            Pawn pawn,
            BackstoryDef? newChildhood,
            BackstoryDef? newAdulthood)
        {
            Pawn_StoryTracker story = pawn.story!;
            if (newChildhood != null)
            {
                story.Childhood = newChildhood;
            }
            else
            {
                // 原版 Childhood setter 会无条件读取 value.spawnCategories，不能传入 null。
                ChildhoodField.SetValue(story, null);
                BackstoriesCacheField.SetValue(story, null);
            }

            // Adulthood setter 支持 null，并会同步清空背景故事缓存。
            story.Adulthood = newAdulthood;
        }

        private static void NotifyPersonalityChanged(Pawn pawn)
        {
            pawn.Notify_DisabledWorkTypesChanged();
            List<SkillRecord>? records = pawn.skills?.skills;
            if (records == null)
            {
                return;
            }

            for (int i = 0; i < records.Count; i++)
            {
                records[i].Notify_SkillDisablesChanged();
            }
        }
    }
}
