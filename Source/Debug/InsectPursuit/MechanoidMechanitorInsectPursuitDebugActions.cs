using LudeonTK;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 虫巢追杀（Pursuit）模式的开发者测试入口。
    /// 仅用于开发者模式下的手工验证，不污染正式 UI 与正常概率/冷却逻辑。
    /// </summary>
    public static class MechanoidMechanitorInsectPursuitDebugActions
    {
        [DebugAction(
            "MAP-机械族机械师",
            "虫巢追杀：立即触发额外虫灾",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.Playing)]
        public static void DevTriggerExtraInfestation()
        {
            GameComponent_MechanoidMechanitorInsectPursuitManager? manager =
                GameComponent_MechanoidMechanitorInsectPursuitManager.GetManager();
            if (manager == null)
            {
                Messages.Message(
                    "虫巢追杀管理器不可用。",
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            manager.DevTriggerExtraInfestation();
        }

        [DebugAction(
            "MAP-机械族机械师",
            "虫巢追杀：立即开始虫族追猎",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.Playing)]
        public static void DevStartHunt()
        {
            GameComponent_MechanoidMechanitorInsectPursuitManager? manager =
                GameComponent_MechanoidMechanitorInsectPursuitManager.GetManager();
            if (manager == null)
            {
                Messages.Message(
                    "虫巢追杀管理器不可用。",
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            manager.DevStartHunt();
        }

        [DebugAction(
            "MAP-机械族机械师",
            "虫巢追杀：立即发动当前追猎",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.Playing)]
        public static void DevLaunchCurrentHunt()
        {
            GameComponent_MechanoidMechanitorInsectPursuitManager? manager =
                GameComponent_MechanoidMechanitorInsectPursuitManager.GetManager();
            if (manager == null)
            {
                Messages.Message(
                    "虫巢追杀管理器不可用。",
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            manager.DevLaunchCurrentHunt();
        }

        [DebugAction(
            "MAP-机械族机械师",
            "虫巢追杀：清除运行状态",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.Playing)]
        public static void DevClearRuntimeState()
        {
            GameComponent_MechanoidMechanitorInsectPursuitManager? manager =
                GameComponent_MechanoidMechanitorInsectPursuitManager.GetManager();
            if (manager == null)
            {
                Messages.Message(
                    "虫巢追杀管理器不可用。",
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            manager.DevClearRuntimeState();
        }
    }
}
