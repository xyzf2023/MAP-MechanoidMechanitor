using System;
using MAP_MechanoidMechanitor;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 肃清指令专用 DEV 测试面板。仅能由通讯窗口内的 DEV 入口打开。
    /// </summary>
    internal sealed class Dialog_PurgeDirectiveDev : Window
    {
        private const float ContentHeight = 760f;
        private readonly Dialog_MechanoidOvermindCommunication owner;
        private Vector2 scrollPosition;

        public override Vector2 InitialSize => new Vector2(780f, 650f);

        internal Dialog_PurgeDirectiveDev(Dialog_MechanoidOvermindCommunication owner)
        {
            this.owner = owner;
            forcePause = false;
            doCloseX = true;
            doCloseButton = false;
            absorbInputAroundWindow = false;
            closeOnClickedOutside = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (!Prefs.DevMode)
            {
                Close(doCloseSound: false);
                return;
            }

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 30f), "肃清指令 DEV 测试");
            Text.Font = GameFont.Small;

            Rect scrollOut = new Rect(inRect.x, inRect.y + 36f, inRect.width, inRect.height - 36f);
            Rect scrollView = new Rect(0f, 0f, scrollOut.width - 18f, ContentHeight);
            Widgets.BeginScrollView(scrollOut, ref scrollPosition, scrollView);

            float y = 0f;
            DrawCurrentState(scrollView, ref y);

            DrawSectionHeader(scrollView, ref y, "肃清额度");
            DrawButtonRow(
                scrollView,
                ref y,
                Command("+100", () => owner.DevAdjustPurgeCredits(100)),
                Command("-100", () => owner.DevAdjustPurgeCredits(-100)),
                Command("+1000", () => owner.DevAdjustPurgeCredits(1000)),
                Command("-1000", () => owner.DevAdjustPurgeCredits(-1000)));

            DrawSectionHeader(scrollView, ref y, "节点评级增减");
            DrawButtonRow(
                scrollView,
                ref y,
                Command("+100", () => ChangeRating(100)),
                Command("-100", () => ChangeRating(-100)),
                Command("+750", () => ChangeRating(750)),
                Command("-750", () => ChangeRating(-750)));

            DrawSectionHeader(scrollView, ref y, "节点评级边界");
            DrawButtonRow(
                scrollView,
                ref y,
                Command("设为 0", () => SetRating(0)),
                Command("设为 749", () => SetRating(749)),
                Command("设为 750", () => SetRating(750)),
                Command("设为 1499", () => SetRating(1499)));
            DrawButtonRow(
                scrollView,
                ref y,
                Command("设为 1500", () => SetRating(1500)),
                Command("设为 2249", () => SetRating(2249)),
                Command("设为 2250", () => SetRating(2250)),
                Command("设为 2999", () => SetRating(2999)));
            DrawButtonRow(
                scrollView,
                ref y,
                Command("设为 3000", () => SetRating(3000)),
                Command("设为 4000", () => SetRating(4000)));

            DrawSectionHeader(scrollView, ref y, "肃清任务");
            DrawButtonRow(
                scrollView,
                ref y,
                Command("执行协议检查", ForceProtocolCheck),
                Command("任务立即到期", ForceQuestDue),
                Command("清除任务冷却", ClearQuestCooldown));
            DrawButtonRow(
                scrollView,
                ref y,
                Command("强制任务成功", ForceQuestSuccess),
                Command("强制任务失败", ForceQuestFail),
                Command("输出任务状态", LogQuestState));

            DrawSectionHeader(scrollView, ref y, "交易状态");
            DrawButtonRow(
                scrollView,
                ref y,
                Command("切换为常态", SwitchToNormal),
                Command("切换为主脑受控", SwitchToControlled));

            DrawSectionHeader(scrollView, ref y, "通讯界面测试");
            bool forceMojibake = owner.DevForceMojibake;
            Widgets.CheckboxLabeled(
                new Rect(scrollView.x, y, scrollView.width, 28f),
                "强制乱码通讯文本",
                ref forceMojibake);
            owner.DevForceMojibake = forceMojibake;
            y += 32f;

            bool bootLoop = owner.DevBootLoopTest;
            Widgets.CheckboxLabeled(
                new Rect(scrollView.x, y, scrollView.width, 28f),
                "循环播放启动动画（保持本窗口开启以便停止）",
                ref bootLoop);
            owner.DevBootLoopTest = bootLoop;
            y += 34f;

            DrawButtonRow(
                scrollView,
                ref y,
                Command("重新播放一次启动动画", owner.DevRestartBootOnce));

            Widgets.EndScrollView();
        }

        private static DevCommand Command(string label, Action action)
        {
            return new DevCommand(label, action);
        }

        private static void DrawCurrentState(Rect view, ref float y)
        {
            GameComponent_MechanoidMechanitorStoryState? story =
                Current.Game?.GetComponent<GameComponent_MechanoidMechanitorStoryState>();
            int rating = story?.PurgeDirectiveRuntimeState?.RatingValue ?? 0;
            int level = PurgeDirectiveRatingUtility.CurrentRatingLevel;
            int credits =
                GameComponent_MechanoidMechanitorStoryState.GetPurgeDirectiveRewardPoints();
            string takeover = GameComponent_CerebrexTakeoverState.IsActive ? "主脑受控" : "常态";

            Rect rect = new Rect(view.x, y, view.width, 54f);
            Widgets.DrawMenuSection(rect);
            Widgets.Label(
                rect.ContractedBy(8f),
                $"当前状态：节点评级 {rating}（{level}级）　肃清额度 {credits}　交易状态 {takeover}");
            y += rect.height + 8f;
        }

        private static void DrawSectionHeader(Rect view, ref float y, string title)
        {
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(view.x, y, view.width, 26f), title);
            y += 28f;
        }

        private static void DrawButtonRow(Rect view, ref float y, params DevCommand[] commands)
        {
            const float gap = 6f;
            const float height = 30f;
            if (commands.Length == 0)
            {
                return;
            }

            float width = (view.width - gap * (commands.Length - 1)) / commands.Length;
            for (int i = 0; i < commands.Length; i++)
            {
                Rect buttonRect = new Rect(view.x + i * (width + gap), y, width, height);
                if (Widgets.ButtonText(buttonRect, commands[i].Label))
                {
                    try
                    {
                        commands[i].Action();
                    }
                    catch (Exception exception)
                    {
                        Log.Error("[MAP-机械族机械师] 肃清指令 DEV 操作失败：" + exception);
                        ShowResult(false, "DEV 操作执行失败，详情已写入日志。");
                    }
                }
            }

            y += height + 6f;
        }

        private static void ChangeRating(int delta)
        {
            PurgeDirectiveRatingUtility.TryAddRating(delta);
        }

        private static void SetRating(int value)
        {
            PurgeDirectiveRatingUtility.SetRatingDirect(value);
        }

        private static void ForceProtocolCheck()
        {
            MechanoidMechanitorPurgeDirectiveUtility.ForceProtocolCheckNow();
            ShowResult(true, "已立即执行一次肃清指令协议检查。");
        }

        private static void ForceQuestDue()
        {
            PurgeDirectiveQuestScheduler.ForceDueNow();
            ShowResult(true, "肃清任务已设为立即到期。");
        }

        private static void ClearQuestCooldown()
        {
            PurgeDirectiveQuestScheduler.ClearCooldown();
            ShowResult(true, "肃清任务冷却已清除。");
        }

        private static void ForceQuestSuccess()
        {
            bool success =
                PurgeDirectiveRatingDebugUtility.TryForceActiveQuestSuccess(out string message);
            ShowResult(success, message);
        }

        private static void ForceQuestFail()
        {
            bool success =
                PurgeDirectiveRatingDebugUtility.TryForceActiveQuestFail(out string message);
            ShowResult(success, message);
        }

        private static void LogQuestState()
        {
            string state = PurgeDirectiveRatingDebugUtility.BuildTaskStateText();
            Log.Message("[肃清指令 DEV]\n" + state);
            ShowResult(true, "任务状态已写入开发者日志。");
        }

        private static void SwitchToNormal()
        {
            bool success =
                PurgeDirectiveTradingStateDebugUtility.TrySwitchToNormal(out string message);
            ShowResult(success, message);
        }

        private static void SwitchToControlled()
        {
            bool success =
                PurgeDirectiveTradingStateDebugUtility.TrySwitchToControlled(out string message);
            ShowResult(success, message);
        }

        private static void ShowResult(bool success, string message)
        {
            Messages.Message(
                message,
                success ? MessageTypeDefOf.TaskCompletion : MessageTypeDefOf.RejectInput,
                historical: false);
        }

        private sealed class DevCommand
        {
            internal readonly string Label;
            internal readonly Action Action;

            internal DevCommand(string label, Action action)
            {
                Label = label;
                Action = action;
            }
        }
    }
}
