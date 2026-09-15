using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 数据处理专用的管辖权解析。记录、预算和负面始终属于 source；
    /// wearer 只代理控制。不得用于原版带宽、控制组或监管关系的读写。
    /// </summary>
    internal static class DataProcessingOverseerResolver
    {
        [ThreadStatic] internal static MechFusionSession? TransferSession;
        [ThreadStatic] internal static HashSet<Pawn>? TransferTargets;

        private sealed class ImportedTargets
        {
            internal readonly HashSet<Pawn> pawns = new HashSet<Pawn>();

            internal ImportedTargets(MechFusionMechanitorSnapshot snapshot)
            {
                foreach (MechFusionControlGroupSnapshot group in snapshot.sourceGroups)
                {
                    if (group == null) continue;
                    foreach (MechFusionControlledMechSnapshot mech in group.mechs)
                    {
                        if (mech?.pawn != null) pawns.Add(mech.pawn);
                    }
                }
            }
        }

        // 只缓存不可变的原始成员集合。键随会话释放，不保存、不跨新游戏保留 Pawn。
        private static readonly ConditionalWeakTable<MechFusionMechanitorSnapshot, ImportedTargets>
            Imported = new ConditionalWeakTable<MechFusionMechanitorSnapshot, ImportedTargets>();

        internal static bool TryGetSourceSession(Pawn? source, out MechFusionSession? session)
        {
            session = FindSession(source, forSource: true);
            return IsUsableSession(session);
        }

        internal static bool TryGetWearerSession(Pawn? wearer, out MechFusionSession? session)
        {
            session = FindSession(wearer, forSource: false);
            return IsUsableSession(session);
        }

        private static MechFusionSession? FindSession(Pawn? pawn, bool forSource)
        {
            if (pawn == null || !GameComponent_MechFusionSessionRegistry.HasAnySession)
                return null;

            // 引用解析完成、LoadedGame 尚未执行时不依赖运行时索引。
            if (Scribe.mode != LoadSaveMode.Inactive)
            {
                foreach (MechFusionSession candidate in
                         GameComponent_MechFusionSessionRegistry.GetSessionsForReading())
                {
                    if (candidate != null && ReferenceEquals(
                            forSource ? candidate.SourcePawn : candidate.WearerPawn, pawn))
                        return candidate;
                }
                return null;
            }

            MechFusionSession? session;
            if (forSource)
                GameComponent_MechFusionSessionRegistry.TryGetSessionForSource(pawn, out session);
            else
                GameComponent_MechFusionSessionRegistry.TryGetSessionForWearer(pawn, out session);
            return session;
        }

        private static bool IsUsableSession(MechFusionSession? session)
        {
            // 不要求 wearer 存活：其死亡的解除事务中仍需识别旧分配的真正所有者。
            return session != null
                && !session.TeardownCompleted
                && session.MechanitorSnapshot?.captured == true
                && session.WearerPawn != null
                && DataProcessingAllocatorEligibilityUtility
                    .IsMechanoidDataProcessingOverseer(session.SourcePawn)
                && session.SourcePawn?.Discarded != true;
        }

        internal static bool WasImported(MechFusionSession session, Pawn target)
        {
            MechFusionMechanitorSnapshot? snapshot = session.MechanitorSnapshot;
            if (snapshot == null) return false;
            if (Scribe.mode == LoadSaveMode.Inactive)
                return Imported.GetValue(snapshot, value => new ImportedTargets(value))
                    .pawns.Contains(target);

            // 读档阶段绝不缓存尚在解析的引用。
            foreach (MechFusionControlGroupSnapshot group in snapshot.sourceGroups)
            {
                if (group == null) continue;
                foreach (MechFusionControlledMechSnapshot mech in group.mechs)
                    if (ReferenceEquals(mech?.pawn, target)) return true;
            }
            return false;
        }

        internal static Pawn? GetAllocationOverseer(Pawn target)
        {
            Pawn? actual = target?.GetOverseer();
            if (target == null) return actual;
            // 仅在同步迁移调用栈内保护“删除旧关系→添加新关系”的短暂空档。
            // 调用结束后完全撤销，不把真正解除监管的目标永久视为有效。
            if (actual == null && IsUsableSession(TransferSession)
                && TransferTargets?.Contains(target) == true)
                return TransferSession!.SourcePawn;
            if (actual == null
                || !TryGetWearerSession(actual, out MechFusionSession? session))
                return actual;

            MechFusionMechanitorSnapshot snapshot = session!.MechanitorSnapshot!;
            if (snapshot.controlsRestored) return actual;

            // 与机控同调解除规则一致：原有机械师的人类保留自己的机械体及新接管者；
            // 原本不是机械师的人类，其合体期间新接管者也属于源机械族。
            return WasImported(session, target) || !snapshot.wearerWasMechanitor
                ? session.SourcePawn
                : actual;
        }

        internal static List<Pawn> GetAllocationSubjects(Pawn_MechanitorTracker tracker)
        {
            if (!GameComponent_MechFusionSessionRegistry.HasAnySession)
                return tracker.OverseenPawns;

            Pawn owner = tracker.Pawn;
            bool isSource = TryGetSourceSession(owner, out MechFusionSession? sourceSession)
                && !sourceSession!.MechanitorSnapshot!.controlsRestored;
            bool isWearer = TryGetWearerSession(owner, out MechFusionSession? wearerSession)
                && !wearerSession!.MechanitorSnapshot!.controlsRestored;
            if (!isSource && !isWearer) return tracker.OverseenPawns;

            // 原版 getter 复用内部 List，绝不原地修改它，也不把它留到下一次 getter 后再用。
            List<Pawn> result = new List<Pawn>();
            AddOwnedSubjects(tracker, owner, result);
            if (isSource && sourceSession!.WearerPawn?.mechanitor != null)
                AddOwnedSubjects(sourceSession.WearerPawn.mechanitor, owner, result);
            return result;
        }

        private static void AddOwnedSubjects(
            Pawn_MechanitorTracker tracker, Pawn owner, List<Pawn> result)
        {
            List<Pawn> subjects = tracker.OverseenPawns;
            for (int i = 0; i < subjects.Count; i++)
            {
                Pawn target = subjects[i];
                if (target != null && ReferenceEquals(GetAllocationOverseer(target), owner)
                    && !result.Contains(target))
                    result.Add(target);
            }
        }

        internal static bool IsFrozenSelf(Pawn? owner, Pawn? target)
        {
            return owner != null && ReferenceEquals(owner, target)
                && TryGetSourceSession(owner, out MechFusionSession? session)
                && session!.IsActive;
        }

        internal static void CaptureFrozenSelf(MechFusionSession session)
        {
            MechFusionMechanitorSnapshot? snapshot = session.MechanitorSnapshot;
            GameComponent_DataProcessingAllocationRegistry? registry =
                GameComponent_DataProcessingAllocationRegistry.CurrentRegistry;
            if (snapshot == null || snapshot.fusionSelfAllocationCaptured
                || registry == null || session.SourcePawn == null)
                return;

            snapshot.fusionSelfAllocationSteps = registry.GetStepsForOverseerTarget(
                session.SourcePawn, session.SourcePawn);
            snapshot.fusionSelfAllocationSpecialization =
                registry.GetSpecializationForOverseerTarget(session.SourcePawn, session.SourcePawn);
            snapshot.fusionSelfAllocationCaptured = true;
        }

        internal static bool TryGetFrozenSelf(Pawn? owner, out MechFusionSession? session)
        {
            if (!TryGetSourceSession(owner, out session) || !session!.IsActive)
                return false;
            // 旧版正在进行的合体没有可靠冻结值；由生命周期入口安全解除，
            // 不把可能已变化的当前数值冒充合体瞬间快照。
            return session.MechanitorSnapshot!.fusionSelfAllocationCaptured;
        }

        internal static void EndBeforeSelfReclaim(Pawn? owner, string reason)
        {
            if (!TryGetSourceSession(owner, out MechFusionSession? session)
                || !session!.IsActive)
                return;
            MechFusionMechanitorSnapshot snapshot = session.MechanitorSnapshot!;
            if (snapshot.fusionSelfAllocationCaptured && snapshot.fusionSelfAllocationSteps <= 0)
                return;
            ExitForSafety(session, reason);
        }

        internal static void ExitForSafety(MechFusionSession session, string reason)
        {
            if (!session.IsActive) return;
            Log.Warning("[MAP-机械族机械师] 数据处理合体保护触发，安全解除合体："
                + $"source={session.SourcePawn?.ThingID}，原因={reason}。");
            MechFusionTeardownService.TryTeardown(
                session, MechFusionExitReason.LoadRepair, force: true);
            // 即使飞行/容器恢复需要延迟，Ending 状态也不再消费活动合体属性快照。
            MechFusionStatCacheUtility.Invalidate(session);
        }
    }
}
