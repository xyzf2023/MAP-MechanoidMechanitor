using System;
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
            // 深度保存的列表元素会先在 LoadingVars 阶段进入 ExposeData，之后虽然还会收到
            // PostLoadInit，但本归一化明确只在 LoadingVars 执行，因此不会重复修改数据。
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

        internal Name GrammarName => BuildName();

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

        // 保留既有公开签名；Job 使用带结果的入口，仅在成功时提交标志并消费核心。
        public void ApplyTo(Pawn pawn)
        {
            if (!TryApplyTo(pawn))
            {
                throw new InvalidOperationException("心智人格写入失败，核心数据未消费。");
            }
        }

        public bool TryApplyTo(Pawn pawn)
        {
            MechanoidMechanitorRoleUtility.EnsureRoleState(pawn);
            EnsurePersonalityTrackers(pawn);

            // 升格身份已由公共入口建立；这里只回滚本次人格写入，不撤销监管关系或身份。
            // 核心快照的导入规则会保留较高技能、过滤受抑制特性，不能拿 Capture/ApplyTo 回滚。
            Action rollback = CapturePersonalityRollback(pawn);
            try
            {
                ApplyPersonality(pawn);
            }
            catch (Exception exception)
            {
                Log.Error("[MAP-机械族机械师] 心智人格写入失败，核心数据保持不变：" + exception);
                try
                {
                    rollback();
                }
                catch (Exception rollbackException)
                {
                    // 第三方特性通知同样可能在回滚时抛异常，不消费唯一的原始核心数据。
                    Log.Error("[MAP-机械族机械师] 心智人格回滚未能完整完成：" + rollbackException);
                }
                return false;
            }

            // 文化是可选导入项，失败不撤销已完成的人格写入，也不留下可重复消费的核心。
            try
            {
                ImportSavedIdeology(pawn);
            }
            catch (Exception exception)
            {
                Log.Error("[MAP-机械族机械师] 心智文化导入通知失败，已保留人格导入结果：" + exception);
            }

            // 公共管理器捕获科研同步异常并排队重试，不把失败传播给人格提交。
            GameComponent_MechanoidMechanitorFeatureManager.NotifyMechanitorInitialized(pawn);
            return true;
        }

        private void ApplyPersonality(Pawn pawn)
        {
            pawn.Name = BuildName();
            if (pawn.ageTracker != null)
            {
                pawn.ageTracker.AgeChronologicalTicks = chronologicalAgeTicks;
            }

            SetBackstories(pawn, childhood, adulthood);
            ReplaceNonGeneTraits(pawn, traits);

            // 兴趣解析优先级：轨道数据网络（狂热）> 心智核心保存兴趣 > “最低为好奇”设置。
            // 轨道数据网络已解锁时最终兴趣为狂热且不受设置影响；未解锁时才依据核心保存的
            // PassionValue 与设置解析。旧快照“未记录兴趣”在载入时已永久归一化为 None，
            // 这里按正常保存兴趣处理，不重复做兼容归一化。
            bool orbitalNetworkUnlocked =
                ResearchFeatureUnlockUtility.IsOrbitalDataNetworkUnlocked();

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

                record.passion =
                    MechanoidMechanitorSkillUtility.ResolveImportedPassion(
                        saved.PassionValue,
                        orbitalNetworkUnlocked);
            }

            NotifyPersonalityChanged(pawn);
        }

        private static Action CapturePersonalityRollback(Pawn pawn)
        {
            Name? name = pawn.Name;
            long age = pawn.ageTracker?.AgeChronologicalTicks ?? 0L;
            BackstoryDef? childhoodBefore = pawn.story!.Childhood;
            BackstoryDef? adulthoodBefore = pawn.story.Adulthood;
            List<Trait> traitsBefore = pawn.story.traits.allTraits
                .FindAll(trait => trait.sourceGene == null);
            List<SkillRecord> skillsBefore = new List<SkillRecord>(pawn.skills!.skills);
            List<Action> restoreSkills = new List<Action>();
            foreach (SkillRecord skill in skillsBefore)
            {
                int level = skill.levelInt;
                Passion passion = skill.passion;
                float xp = skill.xpSinceLastLevel;
                float dailyXp = skill.xpSinceMidnight;
                restoreSkills.Add(() =>
                {
                    skill.levelInt = level;
                    skill.passion = passion;
                    skill.xpSinceLastLevel = xp;
                    skill.xpSinceMidnight = dailyXp;
                });
            }

            return () =>
            {
                pawn.Name = name;
                if (pawn.ageTracker != null)
                {
                    pawn.ageTracker.AgeChronologicalTicks = age;
                }
                SetBackstories(pawn, childhoodBefore, adulthoodBefore);
                pawn.skills!.skills.Clear();
                pawn.skills.skills.AddRange(skillsBefore);
                foreach (Action restoreSkill in restoreSkills)
                {
                    restoreSkill();
                }

                // 保留原 Trait 实例（包括受抑制项），通过原版 API 恢复能力、需求与缓存通知。
                TraitSet traitSet = pawn.story!.traits;
                for (int i = traitSet.allTraits.Count - 1; i >= 0; i--)
                {
                    Trait trait = traitSet.allTraits[i];
                    if (trait.sourceGene == null)
                    {
                        traitSet.RemoveTrait(trait);
                    }
                }
                foreach (Trait trait in traitsBefore)
                {
                    traitSet.GainTrait(trait, suppressConflicts: true);
                }
                traitSet.RecalculateSuppression();
                NotifyPersonalityChanged(pawn);
            };
        }

        /// <summary>
        /// 从心智核心导入文化（仅限以下条件全部满足时）：
        /// Ideology DLC 已启用、文化适配为“部分启用/全部启用”、目标已是正式注册的
        /// 机械族机械师、本快照已记录文化且确实有文化、保存的 Ideology 引用有效（非 null）。
        /// 统一调用 MechanoidMechanitorIdeologyAdaptationUtility.TrySetIdeo 这一现有公共入口，
        /// 由它负责 100% 认可度、成员增减通知与缓存刷新；这里禁止直接写 pawn.ideo
        /// 内部字段或复制 SetIdeo 的副作用。
        /// 边界：文化适配为“不启用/基础功能”、目标非机械族机械师、核心记录“原角色没有文化”、
        /// 核心文化引用已失效或为 null、旧快照没有记录文化时，一律保持目标当前文化；
        /// 文化导入失败不会影响姓名、背景、特性、技能等其余数据的导入。
        /// </summary>
        private void ImportSavedIdeology(Pawn pawn)
        {
            if (!ideologyRecorded || !hadIdeology || ideology == null)
            {
                return;
            }

            if (pawn.Ideo == ideology)
            {
                return;
            }

            MechanoidMechanitorIdeologyAdaptationUtility.TrySetIdeo(pawn, ideology);
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
