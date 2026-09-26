using System;
using System.Linq;
using RimWorld;
using Verse;
using Verse.Sound;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 整局游戏共用一个主题。构造及读档阶段只恢复数据，在游戏 tick 中轮换和发信。
    /// 切换叙事者只停用效果，保留主题并继续计时；无记录时延迟 300 tick 开始推演。
    /// </summary>
    public sealed class GameComponent_WheelOfFateThemes : GameComponent
    {
        private StorytellerDef? ownerStoryteller;
        private StoryThemeDef? currentTheme;
        private int remainingTicks;
        private int lastUpdateTick = -1;
        private long nextThemeSelectionTick = -1;
        private bool announcementPending;
        private WheelOfFateExtraIncidents extraIncidents = new WheelOfFateExtraIncidents();

        public GameComponent_WheelOfFateThemes(Game game) { }

        public static GameComponent_WheelOfFateThemes? Current =>
            CurrentGameComponentCache<GameComponent_WheelOfFateThemes>.Get();

        /// <summary>纯查询，不抽选主题、不发信，也不初始化状态。</summary>
        public StoryThemeDef? ActiveTheme
        {
            get
            {
                StorytellerDef? storyteller = Find.Storyteller?.def;
                WheelOfFateThemeExtension? extension = storyteller?.GetModExtension<WheelOfFateThemeExtension>();
                return extension != null && ownerStoryteller == storyteller
                    && currentTheme != null && currentTheme.CanSelectInCurrentGame
                    && currentTheme.themePoolTag == extension.themePoolTag
                    ? currentTheme : null;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref ownerStoryteller, "ownerStoryteller");
            Scribe_Defs.Look(ref currentTheme, "currentTheme");
            Scribe_Values.Look(ref remainingTicks, "remainingTicks", 0);
            Scribe_Values.Look(ref lastUpdateTick, "lastUpdateTick", -1);
            Scribe_Values.Look(ref nextThemeSelectionTick, "nextThemeSelectionTick", -1L);
            Scribe_Values.Look(ref announcementPending, "announcementPending", false);
            Scribe_Deep.Look(ref extraIncidents, "extraIncidents");
            if (Scribe.mode == LoadSaveMode.PostLoadInit && extraIncidents == null)
                extraIncidents = new WheelOfFateExtraIncidents();
        }

        public override void GameComponentTick()
        {
            // 每 tick 维护倒计时和首次抽选期限，避免被事件调度的 1000 tick 间隔推迟。
            // 仅在期限到达时查找主题候选；调度组件的同 tick 调用会被去重。
            UpdateTheme();
            extraIncidents.Tick(ActiveTheme);
        }

        /// <summary>主调度也调用此入口，保证地图事件抽选前主题已更新；同 tick 不重复执行。</summary>
        public void UpdateTheme()
        {
            if (Find.TickManager == null) return;
            int now = GenTicks.TicksGame;
            if (lastUpdateTick == now) return;
            long elapsed = lastUpdateTick < 0 ? 0L : Math.Max(0L, (long)now - lastUpdateTick);
            lastUpdateTick = now;
            // 计时与主题是否生效无关，切走后仍按游戏时间扣减，不重新随机持续时间。
            if (currentTheme != null)
                remainingTicks = (int)Math.Max(0L, (long)remainingTicks - elapsed);

            StorytellerDef? storyteller = Find.Storyteller?.def;
            WheelOfFateThemeExtension? extension = storyteller?.GetModExtension<WheelOfFateThemeExtension>();
            if (storyteller == null || extension == null)
            {
                // 保留已记录主题和剩余时间；未开始的首次抽选在下次启用时重新等待。
                nextThemeSelectionTick = -1;
                announcementPending = false;
                return;
            }

            // 不以 ActiveTheme 判定是否保留记录：切换叙事者会使效果失效，但不应抹去周期。
            if (currentTheme != null && (!currentTheme.CanSelectInCurrentGame
                || currentTheme.themePoolTag != extension.themePoolTag))
            {
                currentTheme = null;
                remainingTicks = 0;
                announcementPending = false;
                nextThemeSelectionTick = -1;
            }

            if (currentTheme == null)
            {
                if (nextThemeSelectionTick < 0) nextThemeSelectionTick = (long)now + 300;
                if (now < nextThemeSelectionTick) return;
            }
            else
            {
                ownerStoryteller = storyteller;
            }

            if (currentTheme == null || remainingTicks <= 0)
            {
                if (TryChooseNextTheme(extension.themePoolTag, currentTheme, out var next))
                {
                    BeginTheme(storyteller, next);
                }
                else
                {
                    currentTheme = null;
                    announcementPending = false;
                    // 空池时限频重试，避免逐 tick 扫描全部主题。
                    nextThemeSelectionTick = (long)now + 1000;
                }
            }

            SendPendingAnnouncement();
        }

        /// <summary>
        /// 开发者入口：不指定主题时按正常资格抽选；指定时仅跳过首次出现天数。
        /// 直接开始新周期并公告，暂停时也立即生效，不先初始化一个临时主题。
        /// </summary>
        internal bool TryDevSwitchTheme(StoryThemeDef? requestedTheme, out string message)
        {
            StorytellerDef? storyteller = Find.Storyteller?.def;
            WheelOfFateThemeExtension? extension = storyteller?.GetModExtension<WheelOfFateThemeExtension>();
            if (Find.TickManager == null || storyteller == null || extension == null)
            {
                message = "当前叙事者未启用命运之轮主题池。请先选择「命运之轮」叙事者。";
                return false;
            }

            StoryThemeDef? previous = ActiveTheme;
            StoryThemeDef next;
            if (requestedTheme != null)
            {
                // 保留配置、玩法资格和主题池校验，仅跳过首次出现天数。
                if (!requestedTheme.CanSelectInCurrentGame || requestedTheme.themePoolTag != extension.themePoolTag)
                {
                    message = "指定主题已停用、配置无效、未满足玩法条件或不属于当前叙事者的主题池。";
                    return false;
                }

                next = requestedTheme;
            }
            else if (!TryChooseNextTheme(extension.themePoolTag, previous, out next))
            {
                message = "当前没有达到首次出现天数且配置有效的主题。";
                return false;
            }

            // 指定当前主题也重新计时并公告，便于重复检查信封和提示音。
            BeginTheme(storyteller, next, forceAnnouncement: requestedTheme != null);
            SendPendingAnnouncement();
            message = previous == next
                ? $"命运之轮：已重新开始「{next.label}」主题周期。"
                : $"命运之轮：已切换到「{next.label}」。";
            return true;
        }

        private static bool TryChooseNextTheme(string poolTag, StoryThemeDef? previous, out StoryThemeDef next)
        {
            var candidates = DefDatabase<StoryThemeDef>.AllDefsListForReading
                .Where(theme => theme.CanSelectNow && theme.themePoolTag == poolTag)
                .ToList();
            // 有其他主题时避免立即重复；仅有一个时续期，不伪造主题变更公告。
            if (candidates.Count > 1 && previous != null) candidates.Remove(previous);
            return candidates.TryRandomElementByWeight(theme => theme.selectionWeight, out next);
        }

        private void BeginTheme(StorytellerDef storyteller, StoryThemeDef next, bool forceAnnouncement = false)
        {
            announcementPending |= forceAnnouncement || ownerStoryteller != storyteller || currentTheme != next;
            ownerStoryteller = storyteller;
            currentTheme = next;
            remainingTicks = (int)Math.Max(1d, Math.Min(int.MaxValue,
                Math.Ceiling((double)next.durationDays.RandomInRange * GenDate.TicksPerDay)));
            // 避免下一次自然更新把切换前已经流逝的时间扣到新主题上。
            lastUpdateTick = GenTicks.TicksGame;
            nextThemeSelectionTick = -1;
            extraIncidents.Begin(next);
        }

        private void SendPendingAnnouncement()
        {
            if (!announcementPending || currentTheme == null || Find.LetterStack == null) return;
            bool detailed = MAPMechanitorMod.Settings?.showWheelOfFateThemeDetails ?? true;
            if (detailed)
            {
                Find.LetterStack.ReceiveLetter(
                    "MAP_WheelOfFate.ThemeChanged.Label".Translate(currentTheme.label),
                    "MAP_WheelOfFate.ThemeChanged.Text".Translate(currentTheme.label, currentTheme.description),
                    currentTheme.letterDef ?? LetterDefOf.NeutralEvent,
                    playSound: false);
            }
            else
            {
                Find.LetterStack.ReceiveLetter(
                    "MAP_WheelOfFate.ThemeChanged.HiddenLabel".Translate(),
                    "MAP_WheelOfFate.ThemeChanged.HiddenText".Translate(),
                    LetterDefOf.NeutralEvent,
                    playSound: false);
            }

            DefDatabase<SoundDef>.GetNamedSilentFail("MAP_WheelOfFate_ThemeChanged")
                ?.PlayOneShotOnCamera();
            announcementPending = false;
        }
    }
}
