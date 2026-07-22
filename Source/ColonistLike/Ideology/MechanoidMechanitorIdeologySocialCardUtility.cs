using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// Partial/Full 下机械族机械师的意识形态切换 UI 工具。
    /// 由社交面板（SocialCardUtility.DrawPawnRoleSelection 的 Postfix）复用，
    /// 在原版“分配职位……”按钮正下方绘制“切换意识形态”文本按钮。
    /// 不再向 Pawn.GetGizmos 追加 Gizmo。
    /// </summary>
    public static class MechanoidMechanitorIdeologySocialCardUtility
    {
        private const string ChangeIdeoLabelKey =
            "MAP_MechanoidMechanitor.Ideology.ChangeIdeo";
        private const string ChangeIdeoDescKey =
            "MAP_MechanoidMechanitor.Ideology.ChangeIdeoDesc";
        private const string CurrentIdeoSuffixKey =
            "MAP_MechanoidMechanitor.Ideology.CurrentIdeoSuffix";

        // 与原版 SocialCardUtility.DrawPawnRoleSelection 中“分配职位……”按钮完全一致的几何：
        //   RoleChangeButtonSize = new Vector2(115f, 28f);
        //   rect2 = new Rect(rect.width - 150f, y, 115f, 28f); rect2.xMax = rect.width - 26f - 4f;
        // 因此按钮 x = rect.width - 150，右边界 = rect.width - 30，宽度 = 120，高度 = 28。
        private const float RoleButtonLeftFromRight = 150f;
        private const float RoleButtonRightInset = 30f;
        private const float RoleButtonHeight = 28f;

        // 原版职位分隔线绘制在 rect.yMax 处；新按钮置于分隔线下方 1px，避免覆盖分隔线，
        // 同时落在“分配职位”行与关系区域之间约一行高度的空隙中。
        private const float BelowRoleLineGap = 1f;

        /// <summary>
        /// 按钮显示条件，与旧 Gizmo 完全一致，并明确排除文化DLC未启用、Disabled/Basic、
        /// 非正式机械族机械师、非玩家派系、死亡角色、缺少有效 IdeoManager、经典意识形态模式。
        /// </summary>
        public static bool ShouldShowChangeIdeoButton(Pawn? pawn)
        {
            return ModsConfig.IdeologyActive
                && pawn != null
                && !pawn.Dead
                && !pawn.Destroyed
                && pawn.Faction == Faction.OfPlayer
                && Find.IdeoManager != null
                && !Find.IdeoManager.classicMode
                && MechanoidMechanitorIdeologyAdaptationUtility.AllowsIdeologyMembership(pawn);
        }

        /// <summary>
        /// 在原版“分配职位……”按钮正下方绘制“切换意识形态”按钮。
        /// <paramref name="roleRect"/> 为原版 DrawPawnRoleSelection 收到的职位行矩形
        /// （DrawSocialCard 中的 rect4）。Partial 模式下即使原版按钮不显示，本按钮仍以相同位置绘制。
        /// </summary>
        public static void DrawChangeIdeoButton(Pawn? pawn, Rect roleRect)
        {
            if (!ShouldShowChangeIdeoButton(pawn))
            {
                return;
            }

            Color previousColor = GUI.color;
            bool previousEnabled = GUI.enabled;
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            bool previousWordWrap = Text.WordWrap;
            try
            {
                GUI.color = Color.white;
                GUI.enabled = true;
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                Text.WordWrap = true;

                float x = roleRect.width - RoleButtonLeftFromRight;
                float width = roleRect.width - RoleButtonRightInset - x;
                float y = roleRect.yMax + BelowRoleLineGap;
                Rect buttonRect = new Rect(x, y, width, RoleButtonHeight);

                TooltipHandler.TipRegion(buttonRect, ChangeIdeoDescKey.Translate());
                if (Widgets.ButtonText(
                        buttonRect,
                        ChangeIdeoLabelKey.Translate(),
                        drawBackground: true,
                        doMouseoverSound: true,
                        active: true))
                {
                    OpenIdeoFloatMenu(pawn!);
                }
            }
            finally
            {
                GUI.color = previousColor;
                GUI.enabled = previousEnabled;
                Text.Font = previousFont;
                Text.Anchor = previousAnchor;
                Text.WordWrap = previousWordWrap;
            }
        }

        private static void OpenIdeoFloatMenu(Pawn pawn)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            Ideo? current = pawn.Ideo;
            List<Ideo> ordered = BuildOrderedIdeoList(current);
            for (int i = 0; i < ordered.Count; i++)
            {
                Ideo ideo = ordered[i];
                bool isCurrent = ideo == current;
                string label = ideo.name;
                if (isCurrent)
                {
                    label = label + " " + CurrentIdeoSuffixKey.Translate();
                }

                FloatMenuOption option = new FloatMenuOption(
                    label,
                    isCurrent
                        ? null
                        : () =>
                        {
                            MechanoidMechanitorIdeologyAdaptationUtility.TrySetIdeo(
                                pawn,
                                ideo);
                        },
                    ideo.Icon,
                    ideo.Color);
                options.Add(option);
            }

            if (options.Count == 0)
            {
                return;
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static List<Ideo> BuildOrderedIdeoList(Ideo? current)
        {
            List<Ideo> all = Find.IdeoManager.IdeosListForReading.ToList();
            Ideo? primary = Faction.OfPlayer?.ideos?.PrimaryIdeo;
            List<Ideo> minors = Faction.OfPlayer?.ideos?.IdeosMinorListForReading
                ?? new List<Ideo>();

            List<Ideo> ordered = new List<Ideo>(all.Count);
            HashSet<Ideo> seen = new HashSet<Ideo>();

            void TryAdd(Ideo? ideo)
            {
                if (ideo == null || !seen.Add(ideo))
                {
                    return;
                }

                ordered.Add(ideo);
            }

            TryAdd(primary);
            for (int i = 0; i < minors.Count; i++)
            {
                TryAdd(minors[i]);
            }

            List<Ideo> others = all
                .Where(ideo => !seen.Contains(ideo))
                .OrderBy(ideo => ideo.name)
                .ToList();
            for (int i = 0; i < others.Count; i++)
            {
                TryAdd(others[i]);
            }

            return ordered;
        }
    }
}
