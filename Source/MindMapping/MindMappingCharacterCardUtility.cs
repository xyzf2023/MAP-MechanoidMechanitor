using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Grammar;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 自律指令核心“详情”ITab 的角色卡只读绘制器。
    ///
    /// 视觉与布局完整复刻 RimWorld 1.6 原版 Pawn 角色 ITab
    /// （参照 CharacterCardUtility / SkillUI / IdeoUIUtility.DrawIdeoPlate），
    /// 但完全不使用 / 不构造 / 不伪造 Pawn、SkillRecord 等对象：
    /// 所有输入均来自 MindMappingData 快照，所有文本与提示都只依赖快照与 Def 静态数据。
    ///
    /// 职责边界：
    /// - 本类只负责“画”；
    /// - ITab_MindMappingDetails 负责取数据、IsVisible、UpdateSize、FillTab
    ///   并持有左栏滚动位置（以 ref 传入）。
    /// </summary>
    public static class MindMappingCharacterCardUtility
    {
        // —— 与原版角色卡一致的常量 ——
        public const float CardMargin = 17f;
        public const float LeftRectWidth = 250f;
        public const float RightRectWidth = 258f;
        private const float RowHeight = 22f;
        private const float NameHeight = 30f;
        private const float SkillRowSpacing = 3f;
        private const float SkillHeight = 24f;
        private const float SkillWidth = 230f;
        private const float SkillLabelX = 6f;
        private const float MaxSkillLevel = 20f;

        public static readonly Vector2 BaseCardSize = new Vector2(480f, 455f);

        public static Vector2 TotalCardSize
            => new Vector2(BaseCardSize.x + CardMargin * 2f, BaseCardSize.y + CardMargin * 2f);

        private static readonly Color StackElementBackground = new Color(1f, 1f, 1f, 0.1f);
        private static readonly Texture2D SkillBarFillTex =
            SolidColorMaterials.NewSolidColorTexture(new Color(1f, 1f, 1f, 0.1f));

        private static List<SkillDef>? skillDefsInListOrderCached;
        private static readonly List<GenUI.AnonymousStackElement> tmpTopElements =
            new List<GenUI.AnonymousStackElement>();

        /// <summary>技能展示顺序：与原版 SkillUI 相同（按 listOrder 降序）。</summary>
        private static List<SkillDef> SkillDefsInListOrder
        {
            get
            {
                if (skillDefsInListOrderCached == null)
                {
                    skillDefsInListOrderCached = DefDatabase<SkillDef>.AllDefsListForReading
                        .OrderByDescending(sd => sd.listOrder)
                        .ToList();
                }
                return skillDefsInListOrderCached;
            }
        }

        private struct LeftRectSection
        {
            public Rect rect;
            public Action<Rect> drawer;
            public float calculatedSize;
        }

        // ================================================================
        //  入口
        // ================================================================

        /// <summary>
        /// 在 rect（内容区，通常位于 Tab 内 (17,17)，480x455）内绘制角色卡。
        /// leftRectScrollPos 由调用方（ITab）持有，用于左栏内部滚动。
        /// </summary>
        public static void DrawCharacterCard(Rect rect, MindMappingData data, ref Vector2 leftRectScrollPos)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWrap = Text.WordWrap;
            Color oldColor = GUI.color;
            try
            {
                Text.WordWrap = false;
                Widgets.BeginGroup(rect);
                try
                {
                    Rect cardRect = new Rect(0f, 0f, rect.width, rect.height);

                    // 姓名行
                    Rect nameRect = new Rect(0f, 0f, 999f, NameHeight);
                    Text.Font = GameFont.Medium;
                    Widgets.Label(nameRect, DisplayName(data));
                    Text.Font = GameFont.Small;

                    // 顶部信息栈（简介/文化板）之后开始左右栏
                    float curY = nameRect.height + 10f;
                    float topStartY = curY;
                    curY = DoTopStack(cardRect, data, curY);
                    if (curY - topStartY < 78f)
                    {
                        curY += 15f;
                    }

                    Rect leftRect = new Rect(0f, curY, LeftRectWidth, cardRect.height - curY);
                    DoLeftSection(cardRect, leftRect, data, ref leftRectScrollPos);

                    Rect rightRect = new Rect(leftRect.xMax, curY, RightRectWidth, cardRect.height - curY);
                    Widgets.BeginGroup(rightRect);
                    try
                    {
                        DrawSkills(data);
                    }
                    finally
                    {
                        Widgets.EndGroup();
                    }
                }
                finally
                {
                    Widgets.EndGroup();
                }
            }
            finally
            {
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                Text.WordWrap = oldWrap;
                GUI.color = oldColor;
            }
        }

        // ================================================================
        //  顶部：姓名下方信息行
        // ================================================================

        private static float DoTopStack(Rect cardRect, MindMappingData data, float startY)
        {
            float width = cardRect.width - 10f;
            Text.Font = GameFont.Small;
            tmpTopElements.Clear();

            string mainDesc = BuildMainDesc(data);
            if (!mainDesc.NullOrEmpty())
            {
                tmpTopElements.Add(new GenUI.AnonymousStackElement
                {
                    drawer = delegate(Rect r)
                    {
                        Widgets.Label(r, mainDesc);
                    },
                    width = Text.CalcSize(mainDesc).x + 5f
                });
            }

            AddIdeoElementIfAny(data);

            float height = 0f;
            if (tmpTopElements.Count > 0)
            {
                Rect stackRect = new Rect(0f, startY, width, 50f);
                height = GenUI.DrawElementStack(stackRect, RowHeight, tmpTopElements,
                    (Rect r, GenUI.AnonymousStackElement element) => element.drawer(r),
                    (GenUI.AnonymousStackElement element) => element.width,
                    4f, 5f, allowOrderOptimization: false).height;
            }

            startY += height;
            if (tmpTopElements.Count > 0)
            {
                startY += 10f;
            }
            return startY;
        }

        private static void AddIdeoElementIfAny(MindMappingData data)
        {
            bool canDrawPlate = ModsConfig.IdeologyActive
                && Find.IdeoManager != null
                && !Find.IdeoManager.classicMode
                && data.IdeologyRecorded
                && data.HadIdeology
                && data.Ideology != null;

            Ideo? ideo = canDrawPlate ? data.Ideology : null;
            if (ideo != null)
            {
                string ideoName = ideo.name;
                float ideoWidth = Text.CalcSize(ideoName).x + 22f + 15f;
                tmpTopElements.Add(new GenUI.AnonymousStackElement
                {
                    drawer = delegate(Rect r)
                    {
                        Color color = GUI.color;
                        GUI.color = StackElementBackground;
                        GUI.DrawTexture(r, BaseContent.WhiteTex);
                        GUI.color = color;
                        IdeoUIUtility.DrawIdeoPlate(r, ideo);
                    },
                    width = ideoWidth
                });
            }
            else if (data.IdeologyRecorded && data.HadIdeology && !data.IdeologyName.NullOrEmpty())
            {
                // 快照记录过文化、但文化对象不可用时（如依赖未加载），退化为静态名称标签。
                string name = data.IdeologyName;
                tmpTopElements.Add(new GenUI.AnonymousStackElement
                {
                    drawer = delegate(Rect r)
                    {
                        Color color = GUI.color;
                        GUI.color = StackElementBackground;
                        GUI.DrawTexture(r, BaseContent.WhiteTex);
                        GUI.color = color;
                        Widgets.Label(new Rect(r.x + 22f + 5f, r.y, r.width - 10f, r.height), name);
                    },
                    width = Text.CalcSize(name).x + 22f + 15f
                });
            }
        }

        private static string BuildMainDesc(MindMappingData data)
        {
            StringBuilder sb = new StringBuilder();
            Gender gender = data.GenderRecorded ? data.Gender : Gender.None;
            if (gender == Gender.Male || gender == Gender.Female)
            {
                sb.Append(gender.GetLabel());
            }

            string ageNumber = data.AgeNumberString;
            if (!ageNumber.NullOrEmpty())
            {
                if (sb.Length > 0)
                {
                    sb.Append(", ");
                }
                sb.Append("AgeIndicator".Translate(ageNumber).Resolve());
            }

            string result = sb.ToString().CapitalizeFirst();
            return result;
        }

        private static string DisplayName(MindMappingData data)
        {
            string name = data.FullName;
            if (name.NullOrEmpty())
            {
                name = data.ShortName;
            }
            return name.CapitalizeFirst();
        }

        // ================================================================
        //  左栏：背景 / 特性 / 无法从事
        // ================================================================

        private static void DoLeftSection(Rect rect, Rect leftRect, MindMappingData data, ref Vector2 leftRectScrollPos)
        {
            Widgets.BeginGroup(leftRect);
            try
            {
                int numSections = 4; // 原版没有能力槽时也用 4 参与分栏计算
                float leftWidth = leftRect.width;
                List<LeftRectSection> sections = new List<LeftRectSection>(3);

                // —— 背景（童年/成年）——
                float backstorySectionHeight = 2f * RowHeight;
                sections.Add(new LeftRectSection
                {
                    rect = new Rect(0f, 0f, leftWidth, backstorySectionHeight),
                    drawer = delegate(Rect sectionRect)
                    {
                        DrawBackstorySection(sectionRect, leftWidth, data);
                    }
                });

                // —— 特性 ——
                List<MindMappingTraitData> traits = TraitElements(data);
                float traitSectionHeight;
                if (traits.Count == 0)
                {
                    traitSectionHeight = 30f + RowHeight;
                }
                else
                {
                    Rect measured = GenUI.DrawElementStack(
                        new Rect(0f, 0f, leftWidth - 5f, leftRect.height), RowHeight, traits,
                        delegate { },
                        (MindMappingTraitData trait) => Text.CalcSize(TraitLabelCap(trait, data)).x + 10f,
                        4f, 5f, allowOrderOptimization: false);
                    traitSectionHeight = 30f + measured.height;
                }
                MindMappingData traitsCapture = data;
                float traitStackHeight = traitSectionHeight > 30f ? traitSectionHeight - 30f : RowHeight;
                float traitStackHeightForDraw = traitStackHeight;
                sections.Add(new LeftRectSection
                {
                    rect = new Rect(0f, 0f, leftWidth, traitSectionHeight),
                    drawer = delegate(Rect sectionRect)
                    {
                        DrawTraitSection(sectionRect, leftWidth, traitsCapture, traitStackHeightForDraw);
                    }
                });

                // —— 无法从事 ——
                bool tagsRecorded = data.DisabledWorkTagsRecorded;
                WorkTags disabledTags = tagsRecorded ? data.DisabledWorkTags : WorkTags.None;
                List<WorkTags> disabledTagsList = WorkTagsFrom(disabledTags);
                float incapableSectionHeight;
                bool allowWorkTagVerticalLayout = false;
                if (disabledTags == WorkTags.None)
                {
                    incapableSectionHeight = 30f + RowHeight;
                }
                else
                {
                    Rect measured = GenUI.DrawElementStack(
                        new Rect(0f, 0f, leftWidth - 5f, leftRect.height), RowHeight, disabledTagsList,
                        delegate { },
                        WorkTagWidth,
                        4f, 5f, allowOrderOptimization: false);
                    incapableSectionHeight = 30f + measured.height + 12f;
                    float incapStackHeight = measured.height;
                    allowWorkTagVerticalLayout = GenUI.DrawElementStackVertical(
                        new Rect(0f, 0f, rect.width, incapStackHeight), RowHeight, disabledTagsList,
                        delegate { },
                        WorkTagWidth).width <= leftWidth;
                }
                bool incapableRecorded = tagsRecorded;
                MindMappingData incapCapture = data;
                bool allowVerticalForDraw = allowWorkTagVerticalLayout;
                float leftRectHeightForVertical = leftRect.height;
                int numSectionsCapture = numSections;
                WorkTags disabledTagsCapture = disabledTags;
                sections.Add(new LeftRectSection
                {
                    rect = new Rect(0f, 0f, leftWidth, incapableSectionHeight),
                    drawer = delegate(Rect sectionRect)
                    {
                        DrawIncapableSection(sectionRect, leftWidth, incapCapture,
                            allowVerticalForDraw, leftRectHeightForVertical, numSectionsCapture,
                            disabledTagsCapture, incapableRecorded);
                    }
                });

                // —— 与原版一致的空间分配：给每个分栏分配纵向空间 ——
                float num3 = leftRect.height / (float)sections.Count;
                float num4 = 0f;
                for (int i = 0; i < sections.Count; i++)
                {
                    LeftRectSection section = sections[i];
                    if (section.rect.height > num3)
                    {
                        num4 += section.rect.height - num3;
                        section.calculatedSize = section.rect.height;
                    }
                    else
                    {
                        section.calculatedSize = num3;
                    }
                    sections[i] = section;
                }

                bool needScroll = false;
                float contentHeight = 0f;
                if (num4 > 0f)
                {
                    LeftRectSection first = sections[0];
                    float firstSize = first.rect.height + 12f;
                    num4 -= first.calculatedSize - firstSize;
                    first.calculatedSize = firstSize;
                    sections[0] = first;
                }
                while (num4 > 0f)
                {
                    bool shrunkAny = true;
                    for (int i = 0; i < sections.Count; i++)
                    {
                        LeftRectSection section = sections[i];
                        if (section.calculatedSize - section.rect.height > 0f)
                        {
                            section.calculatedSize -= 1f;
                            num4 -= 1f;
                            shrunkAny = false;
                        }
                        sections[i] = section;
                    }
                    if (!shrunkAny)
                    {
                        continue;
                    }
                    for (int i = 0; i < sections.Count; i++)
                    {
                        LeftRectSection section = sections[i];
                        if (i > 0)
                        {
                            section.calculatedSize = Mathf.Max(section.rect.height, num3);
                        }
                        else
                        {
                            section.calculatedSize = section.rect.height + 22f;
                        }
                        contentHeight += section.calculatedSize;
                        sections[i] = section;
                    }
                    needScroll = true;
                    break;
                }

                bool scrolled = needScroll;
                if (scrolled)
                {
                    // 与原版 BeginScrollView 参数一致：滚动范围、内容高度、滚动位置保存方式均不变。
                    Widgets.BeginScrollView(
                        new Rect(0f, 0f, leftWidth, leftRect.height),
                        ref leftRectScrollPos,
                        new Rect(0f, 0f, leftWidth - 16f, contentHeight));
                }

                // 一旦开启滚动，无论绘制 delegate 是否抛出异常，都必须通过 finally 收尾 EndScrollView，
                // 以保证 GUI 裁剪栈平衡；禁止用 catch 吞掉绘制异常，禁止提前结束外层 Group。
                try
                {
                    float num = 0f;
                    for (int i = 0; i < sections.Count; i++)
                    {
                        LeftRectSection section = sections[i];
                        section.drawer(new Rect(0f, num, leftWidth - 5f, section.rect.height));
                        num += section.calculatedSize;
                    }
                }
                finally
                {
                    if (scrolled)
                    {
                        Widgets.EndScrollView();
                    }
                }
            }
            finally
            {
                Widgets.EndGroup();
            }
        }

        private static List<MindMappingTraitData> TraitElements(MindMappingData data)
        {
            List<MindMappingTraitData> list = new List<MindMappingTraitData>();
            for (int i = 0; i < data.Traits.Count; i++)
            {
                // 元素本身或 Def 为空时静默跳过，避免后续访问 .Def 触发 NRE。
                MindMappingTraitData? trait = data.Traits[i];
                if (trait != null && trait.Def != null)
                {
                    list.Add(trait);
                }
            }
            return list;
        }

        private static List<WorkTags> WorkTagsFrom(WorkTags tags)
        {
            List<WorkTags> list = new List<WorkTags>();
            foreach (WorkTags tag in tags.GetAllSelectedItems<WorkTags>())
            {
                if (tag != WorkTags.None)
                {
                    list.Add(tag);
                }
            }
            return list;
        }

        private static float WorkTagWidth(WorkTags tag)
        {
            return Text.CalcSize(tag.LabelTranslated().CapitalizeFirst()).x + 10f;
        }

        // —— 左栏各分区绘制 ——

        private static void DrawBackstorySection(Rect sectionRect, float leftWidth, MindMappingData data)
        {
            Text.Font = GameFont.Small;
            float currentY = sectionRect.y;
            DrawBackstorySlotRow(sectionRect.x, leftWidth, ref currentY, BackstorySlot.Childhood,
                data.Childhood, data);
            DrawBackstorySlotRow(sectionRect.x, leftWidth, ref currentY, BackstorySlot.Adulthood,
                data.Adulthood, data);
        }

        private static void DrawBackstorySlotRow(float x, float leftWidth, ref float currentY,
            BackstorySlot slot, BackstoryDef? backstory, MindMappingData data)
        {
            Rect slotLabelRect = new Rect(x, currentY, leftWidth, RowHeight);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(slotLabelRect,
                slot == BackstorySlot.Adulthood ? "Adulthood".Translate() : "Childhood".Translate());
            Text.Anchor = TextAnchor.UpperLeft;

            string text;
            if (backstory != null)
            {
                text = backstory.TitleCapFor(DisplayGender(data));
            }
            else
            {
                text = "None".Translate().Resolve();
            }

            Rect bgRect = new Rect(slotLabelRect);
            bgRect.x += 90f;
            bgRect.width = Text.CalcSize(text).x + 10f;
            if (bgRect.xMax > x + leftWidth)
            {
                bgRect.width = x + leftWidth - bgRect.x;
            }

            Color oldColor = GUI.color;
            GUI.color = StackElementBackground;
            GUI.DrawTexture(bgRect, BaseContent.WhiteTex);
            GUI.color = backstory == null ? Color.gray : Color.white;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(bgRect, text.Truncate(bgRect.width));
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = oldColor;

            if (backstory != null && Mouse.IsOver(bgRect))
            {
                Widgets.DrawHighlight(bgRect);
                BackstoryDef? tipBackstory = backstory;
                TooltipHandler.TipRegion(bgRect,
                    new TipSignal(BuildBackstoryTooltip(tipBackstory, data), backstory.shortHash * 397945));
            }
            else if (backstory == null && Mouse.IsOver(bgRect))
            {
                TooltipHandler.TipRegion(bgRect,
                    new TipSignal("None".Translate().Resolve(), slot.GetHashCode() * 31));
            }

            currentY += RowHeight + 4f;
        }

        private static void DrawTraitSection(Rect sectionRect, float leftWidth, MindMappingData data,
            float stackHeight)
        {
            Text.Font = GameFont.Small;
            float currentY = sectionRect.y;
            Widgets.Label(new Rect(sectionRect.x, currentY, 200f, 30f),
                "Traits".Translate().AsTipTitle());
            currentY += 24f;

            List<MindMappingTraitData> traits = TraitElements(data);
            if (traits.Count == 0)
            {
                Color oldColor = GUI.color;
                GUI.color = Color.gray;
                Rect noneRect = new Rect(sectionRect.x, currentY, leftWidth, 24f);
                if (Mouse.IsOver(noneRect))
                {
                    Widgets.DrawHighlight(noneRect);
                }
                Widgets.Label(noneRect, "None".Translate());
                currentY += noneRect.height + 2f;
                TooltipHandler.TipRegionByKey(noneRect, "None");
                GUI.color = oldColor;
            }
            else
            {
                MindMappingData dataCapture = data;
                GenUI.DrawElementStack(
                    new Rect(sectionRect.x, currentY, leftWidth - 5f, stackHeight), RowHeight, traits,
                    delegate(Rect r, MindMappingTraitData trait)
                    {
                        Color oldColor = GUI.color;
                        GUI.color = StackElementBackground;
                        GUI.DrawTexture(r, BaseContent.WhiteTex);
                        GUI.color = oldColor;
                        if (Mouse.IsOver(r))
                        {
                            Widgets.DrawHighlight(r);
                        }
                        Widgets.Label(new Rect(r.x + 5f, r.y, r.width - 10f, r.height),
                            TraitLabelCap(trait, dataCapture));
                        GUI.color = Color.white;
                        if (Mouse.IsOver(r) && trait.Def != null)
                        {
                            MindMappingTraitData traitLocal = trait;
                            TooltipHandler.TipRegion(r, new TipSignal(
                                BuildTraitTooltip(traitLocal, dataCapture), trait.Def.shortHash * 397945));
                        }
                    },
                    (MindMappingTraitData trait) => Text.CalcSize(TraitLabelCap(trait, dataCapture)).x + 10f,
                    4f, 5f, allowOrderOptimization: false);
            }
        }

        private static void DrawIncapableSection(Rect sectionRect, float leftWidth, MindMappingData data,
            bool allowVertical, float leftRectHeight, int numSections,
            WorkTags disabledTags, bool recorded)
        {
            Text.Font = GameFont.Small;
            float currentY = sectionRect.y;
            Widgets.Label(new Rect(sectionRect.x, currentY, 200f, 24f),
                "IncapableOf".Translate().AsTipTitle());
            currentY += 24f;

            if (disabledTags == WorkTags.None)
            {
                Color oldColor = GUI.color;
                GUI.color = Color.gray;
                Rect noneRect = new Rect(sectionRect.x, currentY, leftWidth, 24f);
                if (Mouse.IsOver(noneRect))
                {
                    Widgets.DrawHighlight(noneRect);
                }
                string noneText = recorded
                    ? "None".Translate().Resolve()
                    : "MAP_MindMapping.Details.NotRecorded".Translate().Resolve();
                Widgets.Label(noneRect, noneText);
                TooltipHandler.TipRegion(noneRect, new TipSignal(noneText, 813771));
                GUI.color = oldColor;
                GUI.color = Color.white;
                return;
            }

            List<WorkTags> tagsList = WorkTagsFrom(disabledTags);
            MindMappingData dataCapture = data;
            GenUI.StackElementDrawer<WorkTags> drawer = delegate(Rect r, WorkTags tag)
            {
                Color oldColor = GUI.color;
                GUI.color = StackElementBackground;
                GUI.DrawTexture(r, BaseContent.WhiteTex);
                GUI.color = oldColor;
                GUI.color = Color.white;
                if (Mouse.IsOver(r))
                {
                    Widgets.DrawHighlight(r);
                }
                Widgets.Label(new Rect(r.x + 5f, r.y, r.width - 10f, r.height),
                    tag.LabelTranslated().CapitalizeFirst());
                if (Mouse.IsOver(r))
                {
                    WorkTags tagLocal = tag;
                    TooltipHandler.TipRegion(r, new TipSignal(
                        BuildWorkTagTooltip(tagLocal, dataCapture), (int)tag * 7919));
                }
                GUI.color = Color.white;
            };

            Rect stackRect = new Rect(sectionRect.x, currentY, leftWidth - 5f,
                leftRectHeight / (float)numSections);
            if (allowVertical)
            {
                GenUI.DrawElementStackVertical(stackRect, RowHeight, tagsList, drawer, WorkTagWidth);
            }
            else
            {
                // 与原版一致：该分支走默认的顺序优化
                GenUI.DrawElementStack(stackRect, RowHeight, tagsList, drawer, WorkTagWidth, 5f);
            }
            GUI.color = Color.white;
        }

        // ================================================================
        //  右栏：技能
        // ================================================================

        private static void DrawSkills(MindMappingData data)
        {
            Text.Font = GameFont.Small;

            float labelWidth = 0f;
            foreach (SkillDef skillDef in DefDatabase<SkillDef>.AllDefsListForReading)
            {
                float w = Text.CalcSize(skillDef.skillLabel.CapitalizeFirst()).x;
                if (w > labelWidth)
                {
                    labelWidth = w;
                }
            }

            List<SkillDef> defs = SkillDefsInListOrder;
            int row = 0;
            for (int j = 0; j < defs.Count; j++)
            {
                SkillDef skillDef = defs[j];
                if (skillDef == null)
                {
                    continue;
                }
                // 八.10：只显示快照中实际保存且 Def 有效的技能；
                // 展示顺序仍与原版 SkillUI.DrawSkillsOf 一致（按 listOrder 降序）。
                if (FindSkillData(data, skillDef) == null)
                {
                    continue;
                }
                float y = row * (SkillHeight + SkillRowSpacing);
                row++;
                DrawSkillRow(skillDef, data, labelWidth, y);
            }
        }

        private static void DrawSkillRow(SkillDef skillDef, MindMappingData data, float labelWidth, float y)
        {
            Rect holdingRect = new Rect(0f, y, SkillWidth, SkillHeight);
            if (Mouse.IsOver(holdingRect))
            {
                GUI.DrawTexture(holdingRect, TexUI.HighlightTex);
            }

            Widgets.BeginGroup(holdingRect);
            try
            {
                Text.Anchor = TextAnchor.MiddleLeft;
                Rect labelRect = new Rect(SkillLabelX, 0f, labelWidth + SkillLabelX, holdingRect.height);
                Widgets.Label(labelRect, skillDef.skillLabel.CapitalizeFirst());

                MindMappingSkillData? skill = FindSkillData(data, skillDef);
                // 等级数字显示保存的实际值；只有进度条比例按 0-20 夹取（八.12）。
                int displayLevel = 0;
                Passion passion = Passion.None;
                if (skill != null)
                {
                    displayLevel = skill.Level;
                    if (skill.PassionRecorded)
                    {
                        passion = skill.PassionValue;
                    }
                }

                Rect passionRect = new Rect(labelRect.xMax, 0f, 24f, 24f);
                if (passion == Passion.Minor)
                {
                    GUI.DrawTexture(passionRect, SkillUI.PassionMinorIcon);
                }
                else if (passion == Passion.Major)
                {
                    GUI.DrawTexture(passionRect, SkillUI.PassionMajorIcon);
                }

                Rect barRect = new Rect(passionRect.xMax, 0f, holdingRect.width - passionRect.xMax,
                    holdingRect.height);
                float fillPercent = Mathf.Max(0.01f,
                    Mathf.Clamp(displayLevel, 0, 20) / MaxSkillLevel);
                Widgets.FillableBar(barRect, fillPercent, SkillBarFillTex, null, doBorder: false);

                Rect levelRect = new Rect(passionRect.xMax + 4f, 0f, 999f, holdingRect.height);
                levelRect.yMin += 3f;
                GenUI.SetLabelAlign(TextAnchor.MiddleLeft);
                Widgets.Label(levelRect, displayLevel.ToStringCached());
                GenUI.ResetLabelAlign();
                GUI.color = Color.white;
            }
            finally
            {
                Widgets.EndGroup();
            }

            if (Mouse.IsOver(holdingRect))
            {
                MindMappingSkillData? skillForTip = FindSkillData(data, skillDef);
                SkillDef defForTip = skillDef;
                TooltipHandler.TipRegion(holdingRect, new TipSignal(
                    BuildSkillTooltip(defForTip, skillForTip), skillDef.shortHash * 397945));
            }
        }

        private static MindMappingSkillData? FindSkillData(MindMappingData data, SkillDef skillDef)
        {
            for (int i = 0; i < data.Skills.Count; i++)
            {
                // 元素本身或 Def 为空时跳过，避免直接读取 .Def 触发 NRE。
                MindMappingSkillData? skill = data.Skills[i];
                if (skill != null && skill.Def == skillDef)
                {
                    return skill;
                }
            }
            return null;
        }

        // ================================================================
        //  只读文本 / 标签 / 提示（绝不依赖 Pawn / SkillRecord）
        // ================================================================

        private static Gender DisplayGender(MindMappingData data)
        {
            return data.GenderRecorded ? data.Gender : Gender.None;
        }

        private static string TraitLabelCap(MindMappingTraitData trait, MindMappingData data)
        {
            TraitDef? def = trait.Def;
            if (def == null)
            {
                return string.Empty;
            }
            TraitDegreeData? degreeData = GetDegreeData(def, trait.Degree);
            if (degreeData != null)
            {
                string label = degreeData.GetLabelCapFor(DisplayGender(data));
                if (!label.NullOrEmpty())
                {
                    return label;
                }
            }
            if (!def.label.NullOrEmpty())
            {
                return def.label.CapitalizeFirst();
            }
            return def.defName;
        }

        private static TraitDegreeData? GetDegreeData(TraitDef def, int degree)
        {
            if (def.degreeDatas == null)
            {
                return null;
            }
            for (int i = 0; i < def.degreeDatas.Count; i++)
            {
                TraitDegreeData? data = def.degreeDatas[i];
                if (data != null && data.degree == degree)
                {
                    return data;
                }
            }
            return null;
        }

        private static string BuildBackstoryTooltip(BackstoryDef? backstory, MindMappingData data)
        {
            if (backstory == null)
            {
                return string.Empty;
            }
            StringBuilder sb = new StringBuilder();
            // 与原版 BackstoryDef.FullDescriptionFor(Pawn) 对齐：标题在最上方，之后为主体描述。
            sb.AppendLineTagged(backstory.TitleCapFor(DisplayGender(data)));

            // 主体描述：必须用 Def 的 description 字段，不得硬编码；无 Pawn 条件下安全解析。
            // 即便该背景没有技能加成或工作限制，只要描述存在也必须保留（不再退化成标题）。
            string description = ResolveSnapshotDescription(backstory.description, data);
            if (!description.NullOrEmpty())
            {
                sb.AppendLineTagged(description);
                sb.AppendLine();
            }

            if (backstory.skillGains != null)
            {
                foreach (SkillGain gain in backstory.skillGains)
                {
                    if (gain == null || gain.skill == null)
                    {
                        continue;
                    }
                    sb.AppendLine("  " + gain.skill.skillLabel.CapitalizeFirst() + ": "
                        + gain.amount.ToString("+0;-0;0"));
                }
            }
            if (backstory.DisabledWorkTypes != null)
            {
                foreach (WorkTypeDef disabledWorkType in backstory.DisabledWorkTypes)
                {
                    if (disabledWorkType == null)
                    {
                        continue;
                    }
                    sb.AppendLine("  " + disabledWorkType.gerundLabel.CapitalizeFirst() + " "
                        + "DisabledLower".Translate());
                }
            }
            return sb.ToString().TrimEndNewlines();
        }

        private static string BuildTraitTooltip(MindMappingTraitData trait, MindMappingData data)
        {
            TraitDef? def = trait.Def;
            if (def == null)
            {
                return string.Empty;
            }
            StringBuilder sb = new StringBuilder();
            // 与原版 Trait.TipString(Pawn) 对齐：先特性主体描述，空行，再技能/属性等静态效果。
            sb.AppendLineTagged(TraitLabelCap(trait, data));

            TraitDegreeData? degreeData = GetDegreeData(def, trait.Degree);
            if (degreeData != null)
            {
                // 当前 Degree 的主体描述：必须用 TraitDegreeData.description，不得只读取 TraitDef 通用标签。
                // 即便该特性没有技能或属性数值效果，只要描述存在也必须保留（不再退化成特性名）。
                string description = ResolveSnapshotDescription(degreeData.description, data);
                if (!description.NullOrEmpty())
                {
                    sb.AppendLineTagged(description);
                    sb.AppendLine();
                }
            }

            if (degreeData != null)
            {
                if (degreeData.skillGains != null)
                {
                    foreach (SkillGain gain in degreeData.skillGains)
                    {
                        if (gain == null || gain.skill == null)
                        {
                            continue;
                        }
                        sb.AppendLine("  " + gain.skill.skillLabel.CapitalizeFirst() + ": "
                            + gain.amount.ToString("+0;-0;0"));
                    }
                }
                if (degreeData.statOffsets != null)
                {
                    foreach (StatModifier statOffset in degreeData.statOffsets)
                    {
                        if (statOffset == null || statOffset.stat == null)
                        {
                            continue;
                        }
                        sb.AppendLine("  " + statOffset.stat.LabelCap + ": "
                            + statOffset.ValueToStringAsOffset);
                    }
                }
                if (degreeData.statFactors != null)
                {
                    foreach (StatModifier statFactor in degreeData.statFactors)
                    {
                        if (statFactor == null || statFactor.stat == null)
                        {
                            continue;
                        }
                        sb.AppendLine("  " + statFactor.stat.LabelCap + ": "
                            + statFactor.ToStringAsFactor);
                    }
                }
            }
            return sb.ToString().TrimEndNewlines();
        }

        private static string BuildSkillTooltip(SkillDef skillDef, MindMappingSkillData? skill)
        {
            StringBuilder sb = new StringBuilder();
            TaggedString title = skillDef.LabelCap.AsTipTitle();
            if (skill == null)
            {
                title += " (" + "MAP_MindMapping.Details.NotRecorded".Translate().Resolve() + ")";
            }
            sb.AppendLineTagged(title);

            string description = skillDef.description;
            if (!description.NullOrEmpty())
            {
                sb.AppendLineTagged(description.Colorize(ColoredText.SubtleGrayColor)).AppendLine();
            }

            int level = (skill != null) ? skill.Level : 0;
            Passion passion = Passion.None;
            if (skill != null && skill.PassionRecorded)
            {
                passion = skill.PassionValue;
            }
            sb.AppendLineTagged(("SkillLevel".Translate().CapitalizeFirst() + ": ").AsTipTitle()
                + level.ToString());
            // 兴趣归一化后，PassionRecorded == false 仅作为极端异常数据的防御状态；
            // 此处不再向玩家显示“未记录”。明确的 Passion.None 不显示兴趣图标/文本，保持原版表现。
            if (passion != Passion.None)
            {
                sb.AppendLine();
                sb.AppendLine("  - " + passion.GetLabel() + ": x"
                    + passion.GetLearningFactor().ToStringPercent("F0"));
            }
            return sb.ToString().TrimEndNewlines();
        }

        private static string BuildWorkTagTooltip(WorkTags tag, MindMappingData data)
        {
            StringBuilder sb = new StringBuilder();
            List<WorkTypeDef> disabledWorkTypes = new List<WorkTypeDef>();
            foreach (WorkTypeDef workTypeDef in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                if (workTypeDef != null && (workTypeDef.workTags & tag) != WorkTags.None)
                {
                    disabledWorkTypes.Add(workTypeDef);
                }
            }
            sb.AppendLineTagged(tag.LabelTranslated().CapitalizeFirst().Colorize(ColoredText.TipSectionTitleColor));
            sb.AppendLine();
            if (disabledWorkTypes.Count == 0)
            {
                sb.AppendLine("- " + "MAP_MindMapping.Details.NotRecorded".Translate().Resolve());
            }
            else
            {
                foreach (WorkTypeDef workTypeDef in disabledWorkTypes)
                {
                    sb.AppendLine("- " + workTypeDef.pawnLabel);
                }
            }
            return sb.ToString().TrimEndNewlines();
        }

        // ================================================================
        //  无 Pawn 的 Def 描述安全解析
        // ================================================================
        //
        // 原版背景与特性描述包含两种 PAWN 语法：
        // - {PAWN_xxx}：Formatted 阶段使用的格式化标记；
        // - [PAWN_xxx]：AdjustedFor 阶段使用的语法规则。
        //
        // 这里不构造 Pawn。简单花括号标记先转换为对应的方括号规则，再通过
        // GrammarUtility.RulesForPawn 的“Name + Gender”重载生成原版规则。
        // 性别分支同样调用 GrammarResolverSimple 的原版实现，因此会服从当前语言。

        /// <summary>
        /// 匹配 {PAWN_gender ? male : female} 或包含第三个中性形式的性别分支。
        /// 参数内容交给原版 ResolveGenderSymbol 解释，保留原版空格与 Gender.None 行为。
        /// </summary>
        private static readonly Regex PawnGenderFormatterToken = new Regex(
            @"\{\s*PAWN_gender\s*\?\s*([^{}]+)\}",
            RegexOptions.IgnoreCase);

        /// <summary>
        /// 匹配 {PAWN_nameDef}、{PAWN_pronoun} 等无参数格式化标记。
        /// 转换后由原版 DynamicWrapper 与 RulesForPawn 解析，不在 MOD 中维护代词表。
        /// </summary>
        private static readonly Regex PawnSimpleFormatterToken = new Regex(
            @"\{\s*(PAWN_[A-Za-z0-9]+)\s*\}",
            RegexOptions.IgnoreCase);

        /// <summary>
        /// 使用心智快照中的姓名、性别与年龄解析 Def 描述。
        /// 不创建 Pawn，不保存解析后的文本；每次显示都按当前语言重新生成。
        /// </summary>
        private static string ResolveSnapshotDescription(string rawDescription, MindMappingData data)
        {
            if (rawDescription.NullOrEmpty())
            {
                return string.Empty;
            }

            Gender gender = DisplayGender(data);

            // Formatted(Pawn.Named("PAWN")) 所处理的性别函数，改由原版公共解析器
            // 直接使用快照性别完成。两参数时 Gender.None 与原版一样采用第一个参数。
            string formatted = PawnGenderFormatterToken.Replace(rawDescription, (Match match) =>
                GrammarResolverSimple.ResolveGenderSymbol(
                    gender,
                    animal: false,
                    match.Groups[1].Value,
                    rawDescription));

            // TraitDef 常用花括号，而部分 TraitDef 与 BackstoryDef 使用方括号。
            // 统一成原版 DynamicWrapper 能处理的方括号规则。
            formatted = PawnSimpleFormatterToken.Replace(
                formatted,
                (Match match) => "[" + match.Groups[1].Value + "]");

            GrammarRequest request = default(GrammarRequest);
            request.Includes.Add(RulePackDefOf.DynamicWrapper);
            request.Rules.Add(new Rule_String("RULE", formatted));
            request.Rules.AddRange(GrammarUtility.RulesForPawn(
                pawnSymbol: "PAWN",
                name: data.GrammarName,
                title: null,
                kind: PawnKindDefOf.Colonist,
                gender: gender,
                faction: null,
                age: data.BiologicalAgeYears,
                chronologicalAge: data.ChronologicalAgeYears,
                relationInfo: string.Empty,
                everBeenColonistOrTameAnimal: false,
                everBeenQuestLodger: false,
                isFactionLeader: false,
                royalTitles: null,
                cubeInterest: false,
                labelNoParenthesis: data.ShortName,
                constants: request.Constants,
                addTags: true));

            string resolved = GrammarResolver.Resolve("r_root", request);
            return ((TaggedString)resolved).Resolve();
        }
    }
}
