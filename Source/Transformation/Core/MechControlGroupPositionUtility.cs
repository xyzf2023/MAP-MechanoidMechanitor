using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 保存并恢复机械体在监管者控制组列表中的位置。
    /// 仅保存列表索引，避免把原版非 LoadReferenceable 的控制组对象直接写入存档。
    /// </summary>
    internal static class MechControlGroupPositionUtility
    {
        internal static int Capture(Pawn? overseer, Pawn? subject)
        {
            Pawn_MechanitorTracker? tracker = overseer?.mechanitor;
            List<MechanitorControlGroup>? groups = tracker?.controlGroups;
            MechanitorControlGroup? group =
                subject != null ? tracker?.GetControlGroup(subject) : null;
            if (groups == null || group == null)
            {
                return -1;
            }

            return groups.IndexOf(group);
        }

        internal static bool TryRestore(
            Pawn? overseer,
            Pawn? subject,
            int preferredIndex)
        {
            if (overseer == null || subject == null)
            {
                return false;
            }

            if (!MAPOverseerRelationDirectionUtility.IsActualOverseerOf(
                    overseer,
                    subject)
                && !MAPOverseerAssignmentUtility.TryAssignActualOverseer(
                    overseer,
                    subject))
            {
                return false;
            }

            Pawn_MechanitorTracker? tracker = overseer.mechanitor;
            List<MechanitorControlGroup>? groups = tracker?.controlGroups;
            if (tracker == null || groups == null)
            {
                return false;
            }

            // 旧存档没有组位置快照时，能够恢复监管关系即可。
            if (preferredIndex < 0)
            {
                return tracker.GetControlGroup(subject) != null;
            }

            if (preferredIndex >= groups.Count)
            {
                Log.Warning(
                    "[MAP-机械族机械师] 原控制组位置已不存在，" +
                    "保留原版自动分配的控制组：" +
                    $"subject={subject.LabelShort}（{subject.ThingID}），" +
                    $"overseer={overseer.LabelShort}（{overseer.ThingID}），" +
                    $"groupIndex={preferredIndex}，groupCount={groups.Count}。");
                return tracker.GetControlGroup(subject) != null;
            }

            MechanitorControlGroup desired = groups[preferredIndex];
            MechanitorControlGroup? current = tracker.GetControlGroup(subject);
            if (ReferenceEquals(current, desired))
            {
                return true;
            }

            tracker.UnassignPawnFromAnyControlGroup(subject);
            tracker.AssignPawnControlGroup(subject, desired);
            tracker.Notify_BandwidthChanged();
            if (ReferenceEquals(tracker.GetControlGroup(subject), desired))
            {
                return true;
            }

            // 精确位置恢复失败时优先恢复到任一合法控制组，避免留下关系存在、
            // 但控制组与带宽未登记的半完成状态。
            tracker.UnassignPawnFromAnyControlGroup(subject);
            if (current != null && groups.Contains(current))
            {
                tracker.AssignPawnControlGroup(subject, current);
            }
            else
            {
                tracker.AssignPawnControlGroup(subject, null);
            }

            tracker.Notify_BandwidthChanged();
            return false;
        }
    }
}
