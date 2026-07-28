using System;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public enum MechanoidMechanitorFactionRelationNotificationMode
    {
        Immediate,
        Deferred
    }

    internal sealed class MechanoidMechanitorPendingFactionRelationNotification
    {
        public Faction Subject { get; }

        public Faction Other { get; }

        public FactionRelationKind PreviousKind { get; }

        public FactionRelationKind ExpectedCurrentKind { get; }

        public string Context { get; }

        public MechanoidMechanitorPendingFactionRelationNotification(
            Faction subject,
            Faction other,
            FactionRelationKind previousKind,
            FactionRelationKind expectedCurrentKind,
            string context)
        {
            Subject = subject;
            Other = other;
            PreviousKind = previousKind;
            ExpectedCurrentKind = expectedCurrentKind;
            Context = context;
        }
    }

    internal static class MechanoidMechanitorFactionRelationNotificationUtility
    {
        public static void Dispatch(
            GameComponent_MechanoidMechanitorStoryState? storyState,
            MechanoidMechanitorFactionRelationNotificationMode notificationMode,
            Faction subject,
            Faction other,
            FactionRelationKind previousKind,
            FactionRelationKind expectedCurrentKind,
            string context)
        {
            if (subject == null || other == null || subject == other)
            {
                Log.Error(
                    "[MAP-机械族机械师] 无法发送派系关系变化通知：派系参数无效。上下文="
                    + context);
                return;
            }

            if (previousKind == expectedCurrentKind)
            {
                return;
            }

            bool shouldDefer =
                notificationMode
                    == MechanoidMechanitorFactionRelationNotificationMode.Deferred
                || Current.ProgramState != ProgramState.Playing;

            if (shouldDefer && storyState != null)
            {
                storyState.QueueFactionRelationNotification(
                    subject,
                    other,
                    previousKind,
                    expectedCurrentKind,
                    context);
                return;
            }

            if (shouldDefer)
            {
                Log.Error(
                    "[MAP-机械族机械师] 派系关系变化通知原本应延后执行，"
                    + "但无法取得剧情状态组件，将尝试使用安全包装立即发送。上下文="
                    + context);
            }

            NotifySafely(
                subject,
                other,
                previousKind,
                expectedCurrentKind,
                context);
        }

        public static bool NotifySafely(
            Faction subject,
            Faction other,
            FactionRelationKind previousKind,
            FactionRelationKind expectedCurrentKind,
            string context)
        {
            if (subject == null || other == null || subject == other)
            {
                Log.Error(
                    "[MAP-机械族机械师] 跳过派系关系变化通知：派系参数无效。上下文="
                    + context);
                return false;
            }

            try
            {
                FactionRelation? currentRelation =
                    subject.RelationWith(other, allowNull: true);

                if (currentRelation == null)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 跳过派系关系变化通知："
                        + "通知执行时双方关系记录不存在。主体="
                        + GetFactionName(subject)
                        + "，另一方="
                        + GetFactionName(other)
                        + "，上下文="
                        + context);
                    return false;
                }

                if (currentRelation.kind != expectedCurrentKind)
                {
                    Log.Warning(
                        "[MAP-机械族机械师] 跳过已过期的派系关系变化通知。主体="
                        + GetFactionName(subject)
                        + "，另一方="
                        + GetFactionName(other)
                        + "，排队时预期关系="
                        + expectedCurrentKind
                        + "，当前关系="
                        + currentRelation.kind
                        + "，上下文="
                        + context);
                    return false;
                }

                subject.Notify_RelationKindChanged(
                    other,
                    previousKind,
                    canSendLetter: false,
                    reason: null,
                    GlobalTargetInfo.Invalid,
                    out _);

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 派系关系已经成功写入，"
                    + "但发送关系变化通知时发生兼容性异常。"
                    + "该异常可能来自第三方 Harmony 补丁；"
                    + "关系不会回滚，也不会重复发送本条通知。主体="
                    + GetFactionName(subject)
                    + "，另一方="
                    + GetFactionName(other)
                    + "，旧关系="
                    + previousKind
                    + "，目标关系="
                    + expectedCurrentKind
                    + "，上下文="
                    + context
                    + "\n"
                    + ex);
                return false;
            }
        }

        private static string GetFactionName(Faction? faction)
        {
            if (faction == null)
            {
                return "null";
            }

            if (!string.IsNullOrEmpty(faction.Name))
            {
                return faction.Name;
            }

            return faction.def?.defName ?? "unknown";
        }
    }
}
