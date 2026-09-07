using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 自律指令核心保存的心智快照详情页。
    /// 这里直接绘制 MindMappingData，不创建或依赖任何角色实例。
    /// </summary>
    public sealed class ITab_MindMappingDetails : ITab
    {
        private const float TabWidth = 620f;
        private const float TabHeight = 660f;
        private const float ContentMargin = 17f;
        private const float HeaderHeight = 100f;
        private const float SectionHeaderHeight = 30f;
        private const float ValueRowHeight = 26f;
        private const float SkillHeaderHeight = 24f;
        private const float SkillRowHeight = 42f;
        private const float ColumnGap = 12f;

        private static readonly WorkTags[] WorkTagBits =
        {
            WorkTags.ManualDumb,
            WorkTags.ManualSkilled,
            WorkTags.Violent,
            WorkTags.Caring,
            WorkTags.Social,
            WorkTags.Commoner,
            WorkTags.Intellectual,
            WorkTags.Animals,
            WorkTags.Artistic,
            WorkTags.Crafting,
            WorkTags.Cooking,
            WorkTags.Firefighting,
            WorkTags.Cleaning,
            WorkTags.Hauling,
            WorkTags.PlantWork,
            WorkTags.Mining,
            WorkTags.Hunting,
            WorkTags.Constructing,
            WorkTags.Shooting,
            WorkTags.AllWork
        };

        private Vector2 scrollPosition;

        public ITab_MindMappingDetails()
        {
            labelKey = "MAP_MindMapping.Tab.Details";
            tutorTag = "MindMappingDetails";
        }

        public override bool IsVisible
        {
            get
            {
                Thing? selectedThing = SelThing;
                CompMindMappingAutonomousDirectiveCore? core =
                    selectedThing?.TryGetComp<CompMindMappingAutonomousDirectiveCore>();
                return core?.Data != null;
            }
        }

        protected override void UpdateSize()
        {
            base.UpdateSize();
            size = new Vector2(TabWidth, TabHeight);
        }

        protected override void FillTab()
        {
            Thing? selectedThing = SelThing;
            CompMindMappingAutonomousDirectiveCore? core =
                selectedThing?.TryGetComp<CompMindMappingAutonomousDirectiveCore>();
            MindMappingData? data = core?.Data;
            if (data == null)
            {
                return;
            }

            UpdateSize();
            Rect outRect = new Rect(
                ContentMargin,
                ContentMargin,
                size.x - ContentMargin * 2f,
                size.y - ContentMargin * 2f);
            Rect viewRect = new Rect(
                0f,
                0f,
                Mathf.Max(1f, outRect.width - 16f),
                GetContentHeight(data));

            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            try
            {
                DrawDetails(viewRect, data);
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        private static float GetContentHeight(MindMappingData data)
        {
            int traitRows = AtLeastOne(data.Traits.Count);
            int workTagRows = GetWorkTagRowCount(data);
            int skillRows = AtLeastOne(data.Skills.Count);

            float leftHeight =
                SectionHeaderHeight + ValueRowHeight
                + SectionHeaderHeight + ValueRowHeight
                + SectionHeaderHeight + traitRows * ValueRowHeight
                + SectionHeaderHeight + workTagRows * ValueRowHeight
                + 8f;
            float rightHeight =
                SectionHeaderHeight
                + (data.Skills.Count == 0 ? ValueRowHeight : SkillHeaderHeight + skillRows * SkillRowHeight)
                + 8f;

            return HeaderHeight
                + 14f
                + Mathf.Max(leftHeight, rightHeight)
                + 18f;
        }

        private static void DrawDetails(Rect viewRect, MindMappingData data)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWordWrap = Text.WordWrap;
            Color oldGuiColor = GUI.color;

            try
            {
                Text.WordWrap = false;
                DrawIdentityHeader(
                    new Rect(0f, 0f, viewRect.width, HeaderHeight),
                    data);

                float columnsY = HeaderHeight + 14f;
                float columnWidth = (viewRect.width - ColumnGap) / 2f;
                Rect leftRect = new Rect(
                    0f,
                    columnsY,
                    columnWidth,
                    viewRect.height - columnsY);
                Rect rightRect = new Rect(
                    columnWidth + ColumnGap,
                    columnsY,
                    columnWidth,
                    viewRect.height - columnsY);

                Widgets.DrawBoxSolid(
                    leftRect,
                    new Color(0f, 0f, 0f, 0.12f));
                Widgets.DrawBoxSolid(
                    rightRect,
                    new Color(0f, 0f, 0f, 0.12f));
                Widgets.DrawBox(leftRect);
                Widgets.DrawBox(rightRect);

                DrawLeftColumn(leftRect, data);
                DrawRightColumn(rightRect, data);
            }
            finally
            {
                GUI.color = oldGuiColor;
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                Text.WordWrap = oldWordWrap;
            }
        }

        private static void DrawIdentityHeader(Rect rect, MindMappingData data)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.08f, 0.1f, 0.13f, 0.95f));
            Widgets.DrawBox(rect);

            Color oldColor = GUI.color;
            GUI.color = ColoredText.SubtleGrayColor;
            Widgets.Label(
                new Rect(rect.x + 12f, rect.y + 8f, 66f, 30f),
                "MAP_MindMapping.Details.Name".Translate());
            GUI.color = oldColor;

            Text.Font = GameFont.Medium;
            Widgets.Label(
                new Rect(rect.x + 84f, rect.y + 8f, rect.width - 96f, 30f),
                data.FullName.NullOrEmpty() ? data.ShortName : data.FullName);

            Text.Font = GameFont.Small;
            float fieldWidth = (rect.width - 24f) / 3f;
            DrawHeaderField(
                new Rect(rect.x + 12f, rect.y + 43f, fieldWidth, 24f),
                "MAP_MindMapping.Details.ChronologicalAge",
                AgeText(data.ChronologicalAgeYears));
            DrawHeaderField(
                new Rect(rect.x + 12f + fieldWidth, rect.y + 43f, fieldWidth, 24f),
                "MAP_MindMapping.Details.BiologicalAge",
                data.BiologicalAgeRecorded
                    ? AgeText(data.BiologicalAgeYears)
                    : NotRecordedText());
            DrawHeaderField(
                new Rect(rect.x + 12f + fieldWidth * 2f, rect.y + 43f, fieldWidth, 24f),
                "MAP_MindMapping.Details.Gender",
                GenderText(data),
                ValidGender(data) ? data.Gender.GetIcon() : null);

            DrawIdeologyField(
                new Rect(rect.x + 12f, rect.y + 72f, rect.width - 24f, 24f),
                data);
        }

        private static void DrawHeaderField(
            Rect rect,
            string key,
            string value,
            Texture2D? icon = null)
        {
            const float labelWidth = 74f;
            string label = key.Translate().Resolve() + ":";
            Color oldColor = GUI.color;
            GUI.color = ColoredText.SubtleGrayColor;
            Widgets.Label(
                new Rect(rect.x, rect.y, labelWidth, rect.height),
                label);
            GUI.color = oldColor;

            Rect valueRect = new Rect(
                rect.x + labelWidth,
                rect.y,
                rect.width - labelWidth,
                rect.height);
            if (icon != null)
            {
                Rect iconRect = new Rect(
                    valueRect.x,
                    valueRect.y + 1f,
                    22f,
                    22f);
                GUI.DrawTexture(iconRect, icon);
                valueRect.xMin += 26f;
            }

            Widgets.LabelEllipses(valueRect, value);
        }

        private static void DrawIdeologyField(Rect rect, MindMappingData data)
        {
            const float labelWidth = 74f;
            Color oldColor = GUI.color;
            GUI.color = ColoredText.SubtleGrayColor;
            Widgets.Label(
                new Rect(
                    rect.x,
                    rect.y,
                    labelWidth,
                    rect.height),
                "MAP_MindMapping.Details.Ideology".Translate());
            GUI.color = oldColor;

            Rect valueRect = new Rect(
                rect.x + labelWidth,
                rect.y,
                rect.width - labelWidth,
                rect.height);
            if (data.Ideology != null)
            {
                Rect iconRect = new Rect(valueRect.x, valueRect.y + 1f, 22f, 22f);
                data.Ideology.DrawIcon(iconRect);
                valueRect.xMin += 26f;
                string ideologyName = data.IdeologyName;
                if (!ideologyName.NullOrEmpty())
                {
                    TooltipHandler.TipRegion(
                        iconRect,
                        new TipSignal(ideologyName, data.Ideology.GetHashCode()));
                }
            }

            Widgets.LabelEllipses(valueRect, IdeologyText(data));
        }

        private static void DrawLeftColumn(Rect rect, MindMappingData data)
        {
            float y = 8f;
            DrawSectionHeader(
                rect,
                ref y,
                "MAP_MindMapping.Details.Childhood");
            DrawBackstoryRow(rect, ref y, data.Childhood, data);

            DrawSectionHeader(
                rect,
                ref y,
                "MAP_MindMapping.Details.Adulthood");
            DrawBackstoryRow(rect, ref y, data.Adulthood, data);

            DrawSectionHeader(
                rect,
                ref y,
                "MAP_MindMapping.Details.Traits");
            DrawTraitRows(rect, ref y, data);

            DrawSectionHeader(
                rect,
                ref y,
                "MAP_MindMapping.Details.IncapableOf");
            DrawWorkTagRows(rect, ref y, data);
        }

        private static void DrawRightColumn(Rect rect, MindMappingData data)
        {
            float y = 8f;
            DrawSectionHeader(
                rect,
                ref y,
                "MAP_MindMapping.Details.Skills");

            if (data.Skills.Count == 0)
            {
                DrawPlainRow(rect, ref y, NoneText());
                return;
            }

            DrawSkillColumnHeaders(rect, ref y);
            for (int i = 0; i < data.Skills.Count; i++)
            {
                MindMappingSkillData? skill = data.Skills[i];
                if (skill?.Def == null)
                {
                    continue;
                }

                DrawSkillRow(rect, ref y, skill);
            }
        }

        private static void DrawSectionHeader(
            Rect rect,
            ref float y,
            string key)
        {
            Rect headerRect = new Rect(
                rect.x + 4f,
                rect.y + y,
                rect.width - 8f,
                SectionHeaderHeight);
            Widgets.DrawBoxSolid(
                headerRect,
                new Color(0.2f, 0.24f, 0.3f, 0.75f));
            Text.Font = GameFont.Small;
            Widgets.Label(
                new Rect(
                    headerRect.x + 8f,
                    headerRect.y + 3f,
                    headerRect.width - 16f,
                    headerRect.height - 6f),
                key.Translate());
            y += SectionHeaderHeight;
        }

        private static void DrawBackstoryRow(
            Rect rect,
            ref float y,
            BackstoryDef? backstory,
            MindMappingData data)
        {
            string value = NoneText();
            if (backstory != null)
            {
                string title = backstory.TitleFor(DisplayGender(data));
                value = title.NullOrEmpty()
                    ? (backstory.label.NullOrEmpty()
                        ? backstory.defName
                        : backstory.label).CapitalizeFirst()
                    : title.CapitalizeFirst();
            }

            DrawPlainRow(rect, ref y, value);
        }

        private static void DrawTraitRows(
            Rect rect,
            ref float y,
            MindMappingData data)
        {
            int drawn = 0;
            for (int i = 0; i < data.Traits.Count; i++)
            {
                MindMappingTraitData? trait = data.Traits[i];
                if (trait?.Def == null)
                {
                    continue;
                }

                TraitDegreeData? degreeData = trait.Def.degreeDatas != null
                    && trait.Def.degreeDatas.Count > 0
                    ? trait.Def.DataAtDegree(trait.Degree)
                    : null;
                string fallback = trait.Def.label.NullOrEmpty()
                    ? trait.Def.defName
                    : trait.Def.label;
                string label = degreeData == null
                    ? fallback.CapitalizeFirst()
                    : degreeData.GetLabelCapFor(DisplayGender(data));

                DrawPlainRow(rect, ref y, label);

                drawn++;
            }

            if (drawn == 0)
            {
                DrawPlainRow(rect, ref y, NoneText());
            }
        }

        private static void DrawWorkTagRows(
            Rect rect,
            ref float y,
            MindMappingData data)
        {
            if (!data.DisabledWorkTagsRecorded)
            {
                DrawPlainRow(rect, ref y, NotRecordedText());
                return;
            }

            if (data.DisabledWorkTags == WorkTags.None)
            {
                DrawPlainRow(rect, ref y, NoneText());
                return;
            }

            int drawn = 0;
            for (int i = 0; i < WorkTagBits.Length; i++)
            {
                WorkTags tag = WorkTagBits[i];
                if ((data.DisabledWorkTags & tag) != tag)
                {
                    continue;
                }

                DrawPlainRow(
                    rect,
                    ref y,
                    tag.LabelTranslated().CapitalizeFirst());
                drawn++;
            }

            if (drawn == 0)
            {
                DrawPlainRow(
                    rect,
                    ref y,
                    data.DisabledWorkTags.LabelTranslated().CapitalizeFirst());
            }
        }

        private static void DrawSkillColumnHeaders(
            Rect rect,
            ref float y)
        {
            Rect row = NewRowRect(rect, y, SkillHeaderHeight);
            float nameWidth = Mathf.Min(128f, row.width * 0.35f);
            float passionWidth = 84f;
            float levelWidth = 58f;
            Color oldColor = GUI.color;
            GUI.color = ColoredText.SubtleGrayColor;
            Widgets.LabelEllipses(
                new Rect(
                    row.x + nameWidth,
                    row.y + 2f,
                    passionWidth,
                    row.height - 4f),
                "MAP_MindMapping.Details.Passion".Translate().Resolve());
            Widgets.LabelEllipses(
                new Rect(
                    row.x + nameWidth + passionWidth,
                    row.y + 2f,
                    levelWidth,
                    row.height - 4f),
                "MAP_MindMapping.Details.SkillLevel".Translate().Resolve());
            GUI.color = oldColor;
            y += SkillHeaderHeight;
        }

        private static void DrawSkillRow(
            Rect rect,
            ref float y,
            MindMappingSkillData skill)
        {
            Rect row = NewRowRect(rect, y, SkillRowHeight);
            float nameWidth = Mathf.Min(128f, row.width * 0.35f);
            float passionWidth = 84f;
            float levelWidth = 58f;
            float barX = row.x + nameWidth + passionWidth + levelWidth;

            Widgets.LabelEllipses(
                new Rect(row.x + 6f, row.y + 8f, nameWidth - 8f, 24f),
                skill.Def!.skillLabel.CapitalizeFirst());

            DrawPassion(
                new Rect(row.x + nameWidth, row.y + 7f, passionWidth, 26f),
                skill);

            int level = Mathf.Clamp(skill.Level, 0, 20);
            string levelText =
                "MAP_MindMapping.Details.SkillLevel".Translate().Resolve()
                + ": "
                + level;
            Widgets.LabelEllipses(
                new Rect(
                    row.x + nameWidth + passionWidth,
                    row.y + 7f,
                    levelWidth,
                    26f),
                levelText);

            Rect barRect = new Rect(
                barX + 4f,
                row.y + 14f,
                Mathf.Max(1f, row.xMax - barX - 10f),
                12f);
            Widgets.FillableBar(barRect, Mathf.Clamp01(level / 20f));

            string description = skill.Def.description.NullOrEmpty()
                ? skill.Def.skillLabel.CapitalizeFirst()
                : skill.Def.description;
            TooltipHandler.TipRegion(
                row,
                new TipSignal(description, skill.Def.GetHashCode()));
            y += SkillRowHeight;
        }

        private static void DrawPassion(Rect rect, MindMappingSkillData skill)
        {
            if (!skill.PassionRecorded)
            {
                Widgets.LabelEllipses(rect, NotRecordedText());
                return;
            }

            string label;
            Texture2D? icon = null;
            switch (skill.PassionValue)
            {
                case Passion.None:
                    label = "PassionNone".Translate().Resolve().CapitalizeFirst();
                    break;
                case Passion.Minor:
                    label = "PassionMinor".Translate().Resolve().CapitalizeFirst();
                    icon = SkillUI.PassionMinorIcon;
                    break;
                case Passion.Major:
                    label = "PassionMajor".Translate().Resolve().CapitalizeFirst();
                    icon = SkillUI.PassionMajorIcon;
                    break;
                default:
                    Widgets.LabelEllipses(rect, NotRecordedText());
                    return;
            }

            if (icon == null)
            {
                Widgets.LabelEllipses(rect, label);
                return;
            }

            GUI.DrawTexture(
                new Rect(rect.x, rect.y + 1f, 22f, 22f),
                icon);
            Widgets.LabelEllipses(
                new Rect(rect.x + 25f, rect.y, rect.width - 25f, rect.height),
                label);
        }

        private static void DrawPlainRow(
            Rect rect,
            ref float y,
            string value)
        {
            Rect row = NewRowRect(rect, y, ValueRowHeight);
            Widgets.LabelEllipses(
                new Rect(row.x + 8f, row.y + 2f, row.width - 16f, row.height - 4f),
                value);
            y += ValueRowHeight;
        }

        private static Rect NewRowRect(Rect rect, float y, float height)
        {
            return new Rect(
                rect.x + 4f,
                rect.y + y,
                rect.width - 8f,
                height);
        }

        private static string AgeText(int years)
            => "MAP_MindMapping.Details.AgeYears".Translate(years).Resolve();

        private static string GenderText(MindMappingData data)
        {
            if (!ValidGender(data))
            {
                return NotRecordedText();
            }

            return data.Gender.GetLabel().CapitalizeFirst();
        }

        private static string IdeologyText(MindMappingData data)
        {
            if (!data.IdeologyRecorded)
            {
                return NotRecordedText();
            }

            if (!data.HadIdeology)
            {
                return NoneText();
            }

            return data.IdeologyName.NullOrEmpty()
                ? NotRecordedText()
                : data.IdeologyName;
        }

        private static Gender DisplayGender(MindMappingData data)
        {
            return ValidGender(data) ? data.Gender : Gender.None;
        }

        private static bool ValidGender(MindMappingData data)
        {
            return data.GenderRecorded
                && (data.Gender == Gender.None
                    || data.Gender == Gender.Male
                    || data.Gender == Gender.Female);
        }

        private static int GetWorkTagRowCount(MindMappingData data)
        {
            if (!data.DisabledWorkTagsRecorded
                || data.DisabledWorkTags == WorkTags.None)
            {
                return 1;
            }

            int count = 0;
            for (int i = 0; i < WorkTagBits.Length; i++)
            {
                if ((data.DisabledWorkTags & WorkTagBits[i]) == WorkTagBits[i])
                {
                    count++;
                }
            }

            return count > 0 ? count : 1;
        }

        private static int AtLeastOne(int count)
            => count > 0 ? count : 1;

        private static string NoneText()
            => "MAP_MindMapping.Details.None".Translate().Resolve();

        private static string NotRecordedText()
            => "MAP_MindMapping.Details.NotRecorded".Translate().Resolve();
    }
}
