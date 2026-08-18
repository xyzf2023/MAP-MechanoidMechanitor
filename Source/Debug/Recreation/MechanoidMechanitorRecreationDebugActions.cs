using LudeonTK;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 机械族机械师娱乐与灵感 DEV 指令。
    /// 归类于 “MAP-机械族机械师”。
    /// </summary>
    public static class MechanoidMechanitorRecreationDebugActions
    {
        /// <summary>
        /// 标记目标机械族机械师下一次空闲时强制尝试娱乐。
        /// DEV 临时标记，不保存，不强制打断当前高优先级工作。
        /// </summary>
        [DebugAction(
            "MAP-机械族机械师",
            "机械族机械师：下一次空闲强制尝试娱乐...",
            false,
            false,
            false,
            false,
            false,
            0,
            false,
            actionType = DebugActionType.ToolMapForPawns,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ForceNextRecreation(Pawn clickedPawn)
        {
            if (clickedPawn == null)
            {
                Messages.Message(
                    "拒绝：没有选择任何有效 Pawn。",
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            if (!MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(clickedPawn))
            {
                Messages.Message(
                    "拒绝：目标不是机械族机械师。",
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            if (clickedPawn.Dead
                || clickedPawn.Destroyed
                || clickedPawn.Discarded
                || !clickedPawn.Spawned)
            {
                Messages.Message(
                    "拒绝：目标已死亡、已销毁、已丢弃或未生成。",
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            if (!MechanoidMechanitorRecreationUtility.Enabled)
            {
                Messages.Message(
                    "拒绝：机械族机械师娱乐与灵感功能未开启。",
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            MechanoidMechanitorRecreationUtility
                .RequestDebugForceNextRecreation(clickedPawn);

            // 如果 Pawn 当前恰好已进入可重新评估的 Idle，
            // 尽快让 ThinkTree 重新判断；若仍有高优先级工作则不打断，
            // force 标记保留到下一次真正运行我们的 JobGiver。
            clickedPawn.jobs?.CheckForJobOverride();

            Messages.Message(
                "已标记目标下一次空闲强制尝试娱乐：" + clickedPawn.LabelShortCap + "。",
                MessageTypeDefOf.TaskCompletion,
                historical: false);
        }

        /// <summary>
        /// 立即为目标机械族机械师尝试获得一个当前合法的随机原版灵感。
        /// 绕过 5% 概率，但仍受白名单、Skill、WorkType、Capacity、Stat、
        /// Trait、WorkTag、blocking Hediff 约束。
        /// </summary>
        [DebugAction(
            "MAP-机械族机械师",
            "机械族机械师：立即尝试获得随机灵感...",
            false,
            false,
            false,
            false,
            false,
            0,
            false,
            actionType = DebugActionType.ToolMapForPawns,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void TryGrantInspirationNow(Pawn clickedPawn)
        {
            if (clickedPawn == null)
            {
                Messages.Message(
                    "拒绝：没有选择任何有效 Pawn。",
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            if (!MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(clickedPawn))
            {
                Messages.Message(
                    "拒绝：目标不是机械族机械师。",
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            if (!MechanoidMechanitorRecreationUtility.Enabled)
            {
                Messages.Message(
                    "拒绝：机械族机械师娱乐与灵感功能未开启。",
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            if (clickedPawn.Inspired)
            {
                Messages.Message(
                    "目标已拥有灵感，不会覆盖或生成第二个灵感：" + clickedPawn.LabelShortCap + "。",
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            bool success =
                MechanoidMechanitorInspirationUtility
                    .TryGrantRandomEligibleInspiration(
                        clickedPawn,
                        sendLetter: true);

            if (success)
            {
                Messages.Message(
                    "已为 " + clickedPawn.LabelShortCap + " 成功获得一个符合条件的原版灵感。",
                    MessageTypeDefOf.TaskCompletion,
                    historical: false);
            }
            else
            {
                Messages.Message(
                    "当前没有可用灵感，或目标状态不允许获得灵感。",
                    MessageTypeDefOf.RejectInput,
                    historical: false);
            }
        }
    }
}
