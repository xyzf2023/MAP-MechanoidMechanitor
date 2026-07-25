using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 统一的 MAP 监管者（实际 Overseer）关系写入入口。
    /// 剧本开局、DEV、骇入、机械重构、意识转移、升格回滚等所有
    /// “为某机械体指定新监管者”的业务逻辑都应调用本工具，
    /// 以避免再次出现“只 AddDirectRelation、不刷新带宽/控制组”
    /// 导致新存档开局监管状态不同步的问题。
    ///
    /// 流程：基础验证 → 清除旧实际监管者 → 确保 Overseer 关系存在
    /// → 确保控制组包含 subject → 刷新实际控制名单 → 方向校验。
    /// 本工具不会在 getter 中调用，也不会每 Tick 刷新。
    /// </summary>
    public static class MAPOverseerAssignmentUtility
    {
        public static bool TryAssignActualOverseer(
            Pawn? overseer,
            Pawn? subject,
            bool removeExistingActualOverseers = true)
        {
            if (!ModsConfig.BiotechActive)
            {
                return false;
            }

            if (overseer == null || subject == null || overseer == subject)
            {
                return false;
            }

            if (overseer.Dead || overseer.Destroyed || overseer.Discarded
                || subject.Dead || subject.Destroyed || subject.Discarded)
            {
                return false;
            }

            if (!subject.RaceProps.IsMechanoid || subject.OverseerSubject == null)
            {
                return false;
            }

            if (!MechanitorUtility.IsMechanitor(overseer))
            {
                return false;
            }

            MAPMechanitorNodeLifecycleUtility.EnsureBasicTrackers(overseer);

            Pawn_MechanitorTracker? tracker = overseer.mechanitor;
            if (tracker == null)
            {
                return false;
            }

            // 无需外部监管者的 MAP 节点（如正义）不应获得外部监管者。
            if (MAPMechanitorNodeUtility.HasNode(subject)
                && !MAPMechanitorNodeUtility.RequiresExternalOverseer(subject))
            {
                return false;
            }

            if (!tracker.CanOverseeSubject(subject))
            {
                return false;
            }

            subject.relations ??= new Pawn_RelationsTracker(subject);

            if (removeExistingActualOverseers)
            {
                RemoveExistingActualOverseers(subject, overseer);
            }

            bool relationExisted =
                overseer.relations.DirectRelationExists(PawnRelationDefOf.Overseer, subject);

            if (!relationExisted)
            {
                overseer.relations.AddDirectRelation(PawnRelationDefOf.Overseer, subject);
            }

            bool controlGroupExisted = tracker.GetControlGroup(subject) != null;
            if (!controlGroupExisted)
            {
                if (tracker.CanOverseeSubject(subject))
                {
                    tracker.AssignPawnControlGroup(subject);
                }
                else
                {
                    // 控制组分配失败：撤销刚刚新增的关系（若本次新加），刷新带宽，返回 false。
                    if (!relationExisted
                        && overseer.relations.DirectRelationExists(PawnRelationDefOf.Overseer, subject))
                    {
                        overseer.relations.TryRemoveDirectRelation(PawnRelationDefOf.Overseer, subject);
                    }

                    tracker.Notify_BandwidthChanged();
                    Log.Error(
                        "[MAP-机械族机械师] 无法为 " +
                        $"{subject.LabelShort}（{subject.ThingID}）分配监管者 " +
                        $"{overseer.LabelShort}（{overseer.ThingID}）：控制组分配失败。");
                    return false;
                }
            }

            // 关系和控制组完成后刷新实际控制名单（新存档修复核心步骤之一）。
            tracker.Notify_BandwidthChanged();

            if (!MAPOverseerRelationDirectionUtility.IsActualOverseerOf(overseer, subject))
            {
                Log.Error(BuildVerificationLog(overseer, subject, relationExisted, controlGroupExisted, tracker));
                tracker.Notify_BandwidthChanged();
                if (!MAPOverseerRelationDirectionUtility.IsActualOverseerOf(overseer, subject))
                {
                    return false;
                }
            }

            return true;
        }

        private static void RemoveExistingActualOverseers(Pawn subject, Pawn newOverseer)
        {
            // 局部列表，避免静态共享临时列表造成递归或线程安全问题。
            List<Pawn> oldOverseers = new List<Pawn>();
            MAPOverseerRelationDirectionUtility.CollectActualOverseers(subject, oldOverseers);
            for (int i = 0; i < oldOverseers.Count; i++)
            {
                Pawn oldOver = oldOverseers[i];
                if (oldOver == newOverseer)
                {
                    continue;
                }

                // Overseer 关系是 reflexive 的，只需从一方正确调用一次 TryRemoveDirectRelation。
                if (oldOver.relations != null)
                {
                    oldOver.relations.TryRemoveDirectRelation(PawnRelationDefOf.Overseer, subject);
                }

                oldOver.mechanitor?.Notify_BandwidthChanged();
            }
        }

        private static string BuildVerificationLog(
            Pawn overseer,
            Pawn subject,
            bool relationExisted,
            bool controlGroupExisted,
            Pawn_MechanitorTracker tracker)
        {
            List<Pawn>? controlledPawns = tracker.ControlledPawns;
            bool relationExists =
                overseer.relations?.DirectRelationExists(PawnRelationDefOf.Overseer, subject) == true;
            return
                "[MAP-机械族机械师] 监管者分配方向校验失败：" +
                $"overseer={overseer.LabelShort}({overseer.ThingID}), " +
                $"subject={subject.LabelShort}({subject.ThingID}), " +
                $"relationExists={relationExists}, " +
                $"controlGroupExists={tracker.GetControlGroup(subject) != null}, " +
                $"controlledPawnsNull={controlledPawns == null}, " +
                $"controlledPawnsContainsSubject={controlledPawns != null && controlledPawns.Contains(subject)}, " +
                $"totalBandwidth={tracker.TotalBandwidth}, " +
                $"usedBandwidth={tracker.UsedBandwidth}; " +
                $"relationExisted={relationExisted}, controlGroupExisted={controlGroupExisted}。";
        }
    }
}
