using System;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor.GD5
{
    // 只在末尾追加状态，保留旧存档的枚举值。
    internal enum GD5BlackHiveVisitPhase
    {
        None, Arriving, Visiting, Departing, WaitingForCredits, FadingToCredits, Returning,
        ReturningSpeaker, CreditsShown
    }

    public sealed partial class GameComponent_GD5StoryState
    {
        private const int DepartureChargeTicks = 180;
        private const int CreditsDelayTicks = 60;
        // 与原版 ShipCountdown 的渐白时长一致；ScreenFader 使用现实时间，不受游戏倍速影响。
        private const float CreditsFadeSeconds = 7.2f;

        private GD5BlackHiveVisitPhase visitPhase;
        private Map? visitMap;
        private Pawn? visitor;
        private int departureTick = -1;
        private int creditsTick = -1;
        private float creditsFadeSecondsLeft;
        private string creditsJoinedNames = string.Empty;
        // 只保存剩余时长，不把上次程序运行的现实时间戳写入存档。
        private float creditsFadeLastRealtime;
        private bool creditsFadeStarted;
        private float creditsCleanupRetryRealtime;

        internal bool HasVisit => visitPhase != GD5BlackHiveVisitPhase.None;
        internal bool IsArriving => visitPhase == GD5BlackHiveVisitPhase.Arriving;
        internal bool IsDeparting => visitPhase == GD5BlackHiveVisitPhase.Departing;
        internal bool IsCompletingEnding => visitPhase == GD5BlackHiveVisitPhase.WaitingForCredits
            || visitPhase == GD5BlackHiveVisitPhase.FadingToCredits
            || visitPhase == GD5BlackHiveVisitPhase.CreditsShown;
        internal float DepartureProgress => IsDeparting
            ? Mathf.Clamp01(1f - (float)(departureTick - Find.TickManager.TicksGame) / DepartureChargeTicks) : 0f;
        internal bool IsVisitor(Pawn? pawn) => pawn != null && ReferenceEquals(pawn, visitor)
            && (IsArriving || visitPhase == GD5BlackHiveVisitPhase.Visiting || IsDeparting
                || visitPhase == GD5BlackHiveVisitPhase.Returning
                || visitPhase == GD5BlackHiveVisitPhase.ReturningSpeaker);
        internal bool CanTalkTo(Pawn pawn) => IsVisitor(pawn) && visitPhase == GD5BlackHiveVisitPhase.Visiting
            && pawn.Spawned && pawn.Map == visitMap && GD5BlackHiveEndingService.IsFriendly;

        internal bool TrySchedule(Map? map, Pawn? speaker)
        {
            if (GD5BlackHiveEndingService.ContactDisabledReason(map) != null || map == null) return false;
            dryseaVisit = true;
            returnMap = map;
            returnCell = speaker?.MapHeld == map ? speaker.PositionHeld : map.Center;
            visitPhase = GD5BlackHiveVisitPhase.Arriving;
            QueueDryseaTravel();
            return true;
        }

        internal bool TryStartDeparture(Pawn? target)
        {
            if (target == null || !CanTalkTo(target) || visitMap == null
                || MechanoidStoryDepartureUtility.GetCandidates(visitMap).Count == 0) return false;
            int now = Find.TickManager.TicksGame;
            departureTick = now + DepartureChargeTicks;
            visitPhase = GD5BlackHiveVisitPhase.Departing;
            visitor?.jobs?.EndCurrentJob(JobCondition.InterruptForced);
            return true;
        }

        private void TickEnding()
        {
            if (!HasVisit) return;
            try
            {
                int now = Find.TickManager.TicksGame;
                // 离场已经提交后不再依赖地图、派系或访客引用；此阶段只等待并播放字幕。
                if (IsCompletingEnding)
                {
                    if (visitPhase == GD5BlackHiveVisitPhase.WaitingForCredits && now >= creditsTick)
                    {
                        visitPhase = GD5BlackHiveVisitPhase.FadingToCredits;
                        creditsTick = -1;
                        creditsFadeSecondsLeft = CreditsFadeSeconds;
                        creditsFadeStarted = false;
                    }
                    return;
                }
                // 旧版本的预约/到访记录仍指向殖民地图；迁入新流程，不把旧地图当枯海销毁。
                MigrateLegacyVisit();
                if (IsArriving || visitPhase == GD5BlackHiveVisitPhase.Returning
                    || visitPhase == GD5BlackHiveVisitPhase.ReturningSpeaker)
                {
                    if (now >= travelRetryTick) QueueDryseaTravel();
                    return;
                }
                if (!GD5StoryFlowService.IsEnabled || visitMap == null || !Find.Maps.Contains(visitMap)
                    || !GD5BlackHiveEndingService.IsFriendly)
                {
                    DismissVisitor();
                    return;
                }
                if (visitor == null || visitor.Destroyed || visitor.Dead
                    || !visitor.Spawned || visitor.Map != visitMap)
                {
                    DismissVisitor();
                    return;
                }
                if (IsDeparting)
                {
                    if (now < departureTick) return;
                    // 提交前清掉期限；本次执行失败也不能在下一 Tick 自动重复离场。
                    departureTick = -1;
                    Map map = visitMap;
                    var departed = MechanoidStoryDepartureUtility.Depart(map,
                        GD5BlackHiveEndingService.Hive!,
                        (pawn, cell) => GD5BlackHiveEndingService.PlaySkip(map, cell, arriving: false),
                        (pawn, reason) => Messages.Message("MAP_GD5.Ending.Skipped".Translate(pawn.LabelShortCap, reason),
                            pawn, MessageTypeDefOf.RejectInput));
                    if (departed.Count > 0)
                    {
                        // 只快照本批实际加入者的显示名，跨等待、渐白和读档保留，不混入历史批次。
                        var joinedNames = new StringBuilder();
                        foreach (Pawn pawn in departed)
                            joinedNames.AppendLine("   " + pawn.LabelCap);
                        // 人员和毒蜂先折跃离图；之后的等待/渐白只负责表现，不会再次接人。
                        creditsJoinedNames = joinedNames.ToString().TrimEnd('\r', '\n');
                        endingAfterReturn = true;
                        DismissVisitor();
                    }
                    else ResumeVisit();
                }
            }
            catch (Exception ex)
            {
                Log.Error("[MAP-GD5] 黑衣结局来访处理失败：\n" + ex);
                if (visitPhase == GD5BlackHiveVisitPhase.ReturningSpeaker)
                    travelRetryTick = Find.TickManager.TicksGame + 250;
                else if (IsDeparting && visitor?.Spawned == true) ResumeVisit();
                else DismissVisitor();
            }
        }

        private void UpdateEnding()
        {
            if (visitPhase == GD5BlackHiveVisitPhase.CreditsShown)
            {
                // 原版字幕会暂停游戏 Tick，清图必须由仍在运行的 Update 驱动。
                // ShowCredits 返回后的下一帧才清理，让字幕窗口先覆盖枯海画面。
                if (Verse.Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting
                    || Time.realtimeSinceStartup < creditsCleanupRetryRealtime) return;
                creditsCleanupRetryRealtime = Time.realtimeSinceStartup + 1f;
                try { FinishDryseaTravel(); }
                catch (Exception ex) { ReportTravelFailure("字幕期间清理枯海失败：" + ex); }
                return;
            }
            if (visitPhase != GD5BlackHiveVisitPhase.FadingToCredits) return;
            if (Verse.Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting)
            {
                creditsFadeStarted = false;
                return;
            }
            try
            {
                float now = Time.realtimeSinceStartup;
                if (!creditsFadeStarted)
                {
                    creditsFadeSecondsLeft = Mathf.Clamp(creditsFadeSecondsLeft, 0f, CreditsFadeSeconds);
                    // 读档后从保存的渐白程度继续，使用原版 ScreenFader 的绘制和时间基准。
                    float progress = 1f - creditsFadeSecondsLeft / CreditsFadeSeconds;
                    ScreenFader.SetColor(Color.Lerp(Color.clear, Color.white, progress));
                    ScreenFader.StartFade(Color.white, creditsFadeSecondsLeft);
                    creditsFadeLastRealtime = now;
                    creditsFadeStarted = true;
                }
                else
                {
                    creditsFadeSecondsLeft = Mathf.Max(0f, creditsFadeSecondsLeft - (now - creditsFadeLastRealtime));
                    creditsFadeLastRealtime = now;
                }
                if (creditsFadeSecondsLeft > 0f) return;

                ScreenFader.SetColor(Color.white);
                string creditsText = "MAP_GD5.Ending.Credits".Translate();
                if (!string.IsNullOrEmpty(creditsJoinedNames))
                    creditsText += "\n\n" + "MAP_GD5.Ending.JoinedNames".Translate() + "\n" + creditsJoinedNames;
                // 先记录字幕已显示，防止重入/读档重播，但保留地图引用直到字幕期间清理成功。
                visitPhase = GD5BlackHiveVisitPhase.CreditsShown;
                GameVictoryUtility.ShowCredits(creditsText, SongDefOf.EndCreditsSong);
            }
            catch (Exception ex)
            {
                // 字幕异常也保留待清理地图引用，由下一次 Update 收尾。
                visitPhase = GD5BlackHiveVisitPhase.CreditsShown;
                ScreenFader.SetColor(Color.clear);
                Log.Error("[MAP-GD5] 黑衣结局字幕过渡失败：\n" + ex);
            }
        }

        private void BeginEndingCredits()
        {
            ClearSkipCameraJump();
            dryseaRemovalTick = -1;
            travelRetryTick = -1;
            // 字幕期间如果清图检查发现滞留者，送回后仍继续清图，不能重播字幕。
            if (IsCompletingEnding) return;
            visitPhase = GD5BlackHiveVisitPhase.WaitingForCredits;
            creditsTick = Find.TickManager.TicksGame + CreditsDelayTicks;
        }

        private void ResumeVisit()
        {
            visitPhase = GD5BlackHiveVisitPhase.Visiting;
            departureTick = -1;
        }

        internal void DismissVisitor()
        {
            if (dryseaRemovalTick >= 0 || IsCompletingEnding) return;
            ClearSkipCameraJump();
            returningSpeaker = null;
            // 受伤、外交变化和主动取消共用返程路径，不能只清除访客而把玩家留在口袋地图。
            visitPhase = GD5BlackHiveVisitPhase.Returning;
            departureTick = -1;
            travelRetryTick = -1;
            QueueDryseaTravel();
        }

        private void ResetVisit()
        {
            visitor = null;
            visitMap = null;
            visitPhase = GD5BlackHiveVisitPhase.None;
            departureTick = -1;
            creditsTick = -1;
            creditsFadeSecondsLeft = 0f;
            creditsJoinedNames = string.Empty;
            creditsFadeStarted = false;
            creditsFadeLastRealtime = 0f;
            creditsCleanupRetryRealtime = 0f;
            ResetDryseaTravel();
        }

        private void ExposeEndingData()
        {
            Scribe_Values.Look(ref visitPhase, "gd5BlackHiveVisitPhase", GD5BlackHiveVisitPhase.None);
            Scribe_References.Look(ref visitMap, "gd5BlackHiveVisitMap");
            Scribe_References.Look(ref visitor, "gd5BlackHiveVisitor");
            Scribe_Values.Look(ref departureTick, "gd5BlackHiveDepartureTick", -1);
            Scribe_Values.Look(ref creditsTick, "gd5BlackHiveCreditsTick", -1);
            Scribe_Values.Look(ref creditsFadeSecondsLeft, "gd5BlackHiveCreditsFadeSecondsLeft", 0f);
            Scribe_Values.Look(ref creditsJoinedNames, "gd5BlackHiveCreditsJoinedNames", string.Empty);
            ExposeDryseaTravel();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                creditsFadeStarted = false;
                creditsFadeLastRealtime = 0f;
                creditsCleanupRetryRealtime = 0f;
            }
            // 引用在读档完成后由第一次 Tick 验证，不在交叉引用尚未恢复时清理。
        }
    }
}
