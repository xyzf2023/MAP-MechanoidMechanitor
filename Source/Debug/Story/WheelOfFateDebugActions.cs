using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>通过原生开发者菜单轮换主题；构造菜单不初始化或修改主题状态。</summary>
    public static class WheelOfFateDebugActions
    {
        [DebugAction("MAP-机械族机械师", "命运之轮测试",
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        private static List<DebugActionNode> BuildRootMenu()
        {
            return new List<DebugActionNode>
            {
                new DebugActionNode("命运之轮：立即轮换主题",
                    DebugActionType.Action, AdvanceTheme),
                new DebugActionNode("命运之轮：切换到指定主题（忽略天数）")
                {
                    childGetter = ChooseTheme
                }
            };
        }

        private static void AdvanceTheme()
        {
            SwitchTheme(null);
        }

        private static List<DebugActionNode> ChooseTheme()
        {
            var nodes = new List<DebugActionNode>();
            WheelOfFateThemeExtension? extension = Find.Storyteller?.def
                ?.GetModExtension<WheelOfFateThemeExtension>();
            if (extension == null)
            {
                nodes.Add(RejectionNode("请先选择「命运之轮」叙事者。"));
                return nodes;
            }

            foreach (StoryThemeDef theme in DefDatabase<StoryThemeDef>.AllDefsListForReading
                .Where(theme => theme.CanSelectInCurrentGame && theme.themePoolTag == extension.themePoolTag)
                .OrderBy(theme => theme.EffectiveMinDaysPassed).ThenBy(theme => theme.defName))
            {
                nodes.Add(new DebugActionNode(
                    $"{theme.label}（{theme.defName}；首次第 {theme.EffectiveMinDaysPassed} 天）",
                    action: () => SwitchTheme(theme)));
            }

            if (nodes.Count == 0)
                nodes.Add(RejectionNode("当前主题池没有启用且配置有效的主题。"));
            return nodes;
        }

        private static void SwitchTheme(StoryThemeDef? theme)
        {
            GameComponent_WheelOfFateThemes? manager = GameComponent_WheelOfFateThemes.Current;
            if (manager == null)
            {
                Messages.Message("命运之轮主题管理器不可用。", MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            // 执行时再次校验当前叙事者和主题池，避免菜单打开后状态变化。
            bool success = manager.TryDevSwitchTheme(theme, out string message);
            Messages.Message(message,
                success ? MessageTypeDefOf.TaskCompletion : MessageTypeDefOf.RejectInput,
                historical: false);
        }

        private static DebugActionNode RejectionNode(string message)
        {
            return new DebugActionNode(message,
                action: () => Messages.Message(message, MessageTypeDefOf.RejectInput, historical: false));
        }
    }
}
