using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>无外框的太阳血条；复用原生字体与平面色条，临时反馈不参与战斗或存档。</summary>
    internal sealed class SunBossStatusPanel
    {
        private const float PanelHeight = 64f;
        private const float DefeatDuration = 0.8f;
        private static readonly Color SecondaryTextColor = new Color(0.95f, 0.95f, 0.95f);
        private int lastFrame = -1;
        private int lastTick = -1;
        private int encounterSeed = -1;
        private int encounterActivation = -1;
        private float previousFraction;
        private float trailFraction;
        private float defeatStarted = -1f;
        private Pawn? observedBoss;
        private bool wasAlive;

        internal void Draw(Map map, Pawn? boss, Building? core, bool activated,
            int activationStartedTick, int stabilizers)
        {
            // IMGUI 的 Layout/鼠标事件不推进表现；隐藏界面时也不留下可补播的瞬时反馈。
            if (Event.current.type != EventType.Repaint) return;
            if (Find.CurrentMap != map || WorldRendererUtility.WorldSelected
                || Find.UIRoot?.screenshotMode.FiltersCurrentEvent == true)
            {
                lastFrame = -1;
                return;
            }
            bool alive = activated && boss != null && boss.Spawned && !boss.Dead
                && !boss.Destroyed && boss.Map == map;
            bool awakening = !activated && activationStartedTick >= 0 && core != null
                && core.Spawned && !core.Destroyed && core.Map == map;
            // 销毁/离图不等于击败；退场只接受上一帧仍在本图显示的 BOSS 的真实死亡。
            bool defeated = activated && boss?.Dead == true;
            CompSunBossState? state = boss?.GetComp<CompSunBossState>();
            if ((!awakening && !alive && !defeated) || (!awakening && state == null)
                || !TryGetPanelRect(out Rect panel))
            {
                lastFrame = -1;
                return;
            }

            int now = Find.TickManager.TicksGame;
            int seed = awakening ? core!.thingIDNumber : state!.LightSeed;
            int count = Mathf.Clamp(stabilizers, 0, 6);
            float fraction = awakening ? 1f : Mathf.Clamp01(state!.Structure / Mathf.Max(1f, state.MaxStructure));
            bool continuous = lastFrame >= 0 && Time.frameCount - lastFrame <= 1 && now >= lastTick
                && encounterSeed == seed && encounterActivation == activationStartedTick;
            if (defeated)
            {
                if (!continuous || observedBoss != boss || (!wasAlive && defeatStarted < 0f))
                {
                    lastFrame = -1;
                    return;
                }
                // 死亡信件可能暂停游戏；短暂退场使用界面时间，避免整条血条停留在尸体上。
                if (wasAlive) defeatStarted = Time.realtimeSinceStartup;
            }
            else
            {
                if (!continuous || defeatStarted >= 0f)
                    Synchronize(fraction);
                else
                    UpdateFeedback(fraction, now);
            }
            lastFrame = Time.frameCount;
            lastTick = now;
            encounterSeed = seed;
            encounterActivation = activationStartedTick;
            observedBoss = boss;
            wasAlive = alive;

            float deathProgress = defeated ? Mathf.Clamp01((Time.realtimeSinceStartup - defeatStarted) / DefeatDuration) : 0f;
            if (defeated && deathProgress >= 1f) return;
            float startup = awakening
                ? SunSkillAnimation.Progress(now - activationStartedTick, MapComponent_SunBossArena.ActivationDurationTicks)
                : 1f;
            // 文字快速达到可读亮度；血条按同一苏醒时间线从中心向两侧揭示，不改变真实血量。
            float reveal = SunSkillAnimation.Smooth(Mathf.InverseLerp(0.55f, 0.9f, startup));
            float alpha = SunSkillAnimation.Smooth(Mathf.InverseLerp(0.55f, 0.65f, startup))
                * (1f - SunSkillAnimation.Smooth(deathProgress));
            if (alpha <= 0f) return;

            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;
            bool oldWordWrap = Text.WordWrap;
            try
            {
                Text.WordWrap = false;
                DrawContents(panel, boss, state, awakening, defeated, count, fraction,
                    alpha, reveal);
                if (!awakening && !defeated && state != null && alpha > 0.9f && Mouse.IsOver(panel))
                    DrawTooltip(panel, state, count);
            }
            finally
            {
                GUI.color = oldColor;
                Text.Anchor = oldAnchor;
                Text.Font = oldFont;
                Text.WordWrap = oldWordWrap;
            }
        }

        private void Synchronize(float fraction)
        {
            // 首次显示、读档、切图返回或新一轮 DEV 激活直接对齐真实状态，不制造历史掉血。
            previousFraction = trailFraction = fraction;
            defeatStarted = -1f;
        }

        private void UpdateFeedback(float fraction, int now)
        {
            // 不重置延迟计时：持续自损时残影也会追上，避免被每次灼烧无限延长。
            float follow = 1f - Mathf.Exp(-Mathf.Max(0, now - lastTick) / 7f);
            trailFraction = Mathf.Lerp(trailFraction, fraction, follow);
            if (fraction < previousFraction) trailFraction = Mathf.Max(trailFraction, previousFraction);
            trailFraction = Mathf.Clamp(trailFraction, fraction, 1f);
            previousFraction = fraction;
        }

        private void DrawContents(Rect panel, Pawn? boss, CompSunBossState? state, bool awakening,
            bool defeated, int count, float fraction, float alpha, float reveal)
        {
            string title = defeated ? "MAP_SunBoss_DefeatedLabel".Translate().ToString()
                : awakening ? "MAP_SunBoss_ReactorAwakeningLabel".Translate().ToString() : boss!.LabelShort;
            Label(new Rect(panel.x, panel.y, panel.width, 24f), title,
                Color.white, alpha, GameFont.Small, TextAnchor.MiddleCenter);

            // 唯一的底色限定在细血条内；名称和状态用黑色描边与地图背景分离。
            Rect bar = new Rect(panel.x, panel.y + 29f, panel.width, 9f);
            Color tint = Color.Lerp(new Color(0.55f, 0.51f, 0.43f), SunLightPresentation.BossTint(fraction), 0.4f);
            DrawBar(bar, fraction, defeated ? fraction : trailFraction, tint, alpha, reveal, defeated);

            if (defeated) return;
            Rect footer = new Rect(panel.x, panel.y + 42f, panel.width, 22f);
            // 沿用旧状态标题的稳定器描述；空参数省略已在其他位置显示的名称和阶段。
            Label(new Rect(footer.x, footer.y, footer.width * 0.45f, footer.height),
                "MAP_SunBoss_StatusHeading".Translate(string.Empty, string.Empty, count).ToString().Trim(),
                SecondaryTextColor, alpha, GameFont.Small);
            if (!awakening)
            {
                string stage = state!.Structure <= 0f ? "MAP_SunBoss_StructureCollapsing".Translate().ToString()
                    : SunBossStage.For(count).LabelKey.Translate().ToString();
                Label(new Rect(footer.x + footer.width * 0.5f, footer.y, footer.width * 0.5f, footer.height),
                    stage, SecondaryTextColor, alpha, GameFont.Small, TextAnchor.MiddleRight);
            }
        }

        private static void DrawBar(Rect bar, float fraction, float trail, Color tint,
            float alpha, float reveal, bool defeated)
        {
            float visibleWidth = bar.width * Mathf.Clamp01(reveal);
            if (visibleWidth <= 0f) return;
            float hiddenOnEachSide = (bar.width - visibleWidth) * 0.5f;
            GUI.BeginGroup(new Rect(bar.x + hiddenOnEachSide, bar.y, visibleWidth, bar.height));
            try
            {
                // 内容仍按完整条宽计算，只有裁剪区域对称展开；不会把入场进度误当成回血。
                Rect local = new Rect(-hiddenOnEachSide, 0f, bar.width, bar.height);
                Fill(local, new Color(0.08f, 0.08f, 0.08f, 0.8f * alpha));
                if (!defeated)
                {
                    float end = local.width * fraction;
                    Fill(new Rect(local.x + end, 0f, local.width * Mathf.Max(0f, trail - fraction), local.height),
                        Fade(Color.Lerp(tint, Color.gray, 0.5f), alpha * 0.45f));
                    Fill(new Rect(local.x, 0f, end, local.height), Fade(tint, alpha));
                }
            }
            finally { GUI.EndGroup(); }
        }

        private static Color Fade(Color color, float alpha)
        {
            color.a *= Mathf.Clamp01(alpha);
            return color;
        }

        private static void Fill(Rect rect, Color color)
        {
            if (rect.width <= 0f || color.a <= 0f) return;
            GUI.color = color;
            GUI.DrawTexture(rect, BaseContent.WhiteTex);
        }

        private static void Label(Rect rect, string text, Color color, float alpha, GameFont font,
            TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            if (rect.width <= 0f || alpha <= 0f) return;
            Text.Font = font;
            Text.Anchor = anchor;
            string displayed = text.Truncate(rect.width);
            // 一像素的全向黑色描边，比单侧投影更能抵抗明亮地面和战斗特效的干扰。
            GUI.color = new Color(0f, 0f, 0f, alpha);
            for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                    if (x != 0 || y != 0)
                        Widgets.Label(new Rect(rect.x + x, rect.y + y, rect.width, rect.height), displayed);
            GUI.color = Fade(color, alpha);
            Widgets.Label(rect, displayed);
        }

        private static void DrawTooltip(Rect panel, CompSunBossState state, int count)
        {
            SunBossStage stage = SunBossStage.For(count);
            string text = "MAP_SunBoss_StatusHeading".Translate(state.Boss.LabelShort,
                stage.LabelKey.Translate(), count).ToString();
            text += "\n" + "MAP_SunBoss_StructureValue".Translate(Mathf.CeilToInt(state.Structure),
                state.MaxStructure.ToString("0"));
            text += "\n" + "MAP_SunBoss_StageDamage".Translate((stage.DamageFactor * 100f).ToString("0"));
            text += "\n" + "MAP_SunBoss_StageHeatField".Translate(stage.HeatRadius.ToString("0.#"));
            // 固定提示标识：结构值持续变化时只刷新内容，避免反复重置原版悬停延迟。
            TooltipHandler.TipRegion(panel, new TipSignal(text, state.Boss.thingIDNumber ^ 0x53424F53));
        }

        private static bool TryGetPanelRect(out Rect panel)
        {
            float width = Mathf.Min(500f, UI.screenWidth - 32f);
            float left = (UI.screenWidth - width) * 0.5f;
            float top = 80f;
            ColonistBar? colonistBar = Find.ColonistBar;
            if (colonistBar != null && UI.screenWidth >= 800 && UI.screenHeight >= 500
                && Find.PlaySettings.showColonistBar && !Find.TilePicker.Active)
            {
                // Entries 先让原版刷新布局；地图 OnGUI 在头像栏之前执行，不使用上一帧的旧位置。
                var entries = colonistBar.Entries;
                var positions = colonistBar.DrawLocs;
                Vector2 size = colonistBar.Size;
                for (int i = 0; i < positions.Count; i++)
                {
                    Vector2 position = positions[i];
                    if (position.x + size.x < left || position.x > left + width) continue;
                    float bottom = position.y + size.y + 24f * colonistBar.Scale;
                    Pawn? pawn = i < entries.Count ? entries[i].pawn : null;
                    bool weaponShown = pawn?.equipment?.Primary?.def.IsWeapon == true
                        && (Prefs.ShowWeaponsUnderPortraitMode == ShowWeaponsUnderPortraitMode.Always
                            || (Prefs.ShowWeaponsUnderPortraitMode == ShowWeaponsUnderPortraitMode.WhileDrafted && pawn.Drafted));
                    if (weaponShown) bottom = Mathf.Max(bottom, position.y + size.y * 1.9f);
                    top = Mathf.Max(top, bottom + 8f);
                }
            }
            top = Mathf.Min(top, Mathf.Max(8f, UI.screenHeight - PanelHeight - 36f));
            panel = new Rect(left, top, width, PanelHeight);
            return width >= 260f && UI.screenHeight >= PanelHeight + 16f;
        }
    }
}
