using System;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 解除合体的唯一幂等入口。手动解除、能源归零、稳定值归零、
    /// 目标死亡或倒地、服装异常、读档不一致都必须调用这里，
    /// 不允许复制多套清理代码或直接 Destroy 服装代替解除逻辑。
    /// </summary>
    public static class MechFusionTeardownService
    {
        private const int RestoreSearchRadius = 8;

        public static bool TryTeardown(
            MechFusionSession? session,
            MechFusionExitReason reason,
            bool force)
        {
            if (session == null)
            {
                return false;
            }

            if (session.IsPendingRecovery)
            {
                return TryRecoverPendingSession(session);
            }

            if (session.IsEnding && !session.TeardownDeferred && !force)
            {
                return false;
            }

            session.SetExitReason(reason);
            session.SetState(MechFusionSessionState.Ending);
            session.TeardownDeferred = false;
            return ExecuteTeardown(session);
        }

        internal static void TryResumeDeferredTeardown(MechFusionSession session)
        {
            if (session == null || !session.IsEnding || !session.TeardownDeferred)
            {
                return;
            }

            session.TeardownDeferred = false;
            ExecuteTeardown(session);
        }

        internal static bool TryRecoverPendingSession(MechFusionSession session)
        {
            if (session == null || !session.IsPendingRecovery)
            {
                return false;
            }

            Pawn? source = session.SourcePawn;
            if (source == null || source.Destroyed || source.Discarded)
            {
                Log.Error(
                    "[MAP-机械族机械师] 待恢复合体会话的源 Pawn 已不可用，" +
                    "已移除记录：" + $"session={session.SessionId}。");
                GameComponent_MechFusionSessionRegistry.RemoveSession(session);
                return false;
            }

            if (!TryRestoreSourcePawn(session, source, out bool deferred))
            {
                return !deferred;
            }

            FinishAfterSourceRestored(
                session,
                source,
                TryBeginReturnToPawnForm(session, source),
                session.WearerPawn);
            return true;
        }

        internal static void NotifyShellUnequipped(
            CompMechFusionShell? shell,
            Pawn? pawn)
        {
            NotifyShellLost(shell);
        }

        internal static void NotifyShellDestroyed(
            CompMechFusionShell? shell,
            Map? previousMap)
        {
            NotifyShellLost(shell);
        }

        private static void NotifyShellLost(CompMechFusionShell? shell)
        {
            if (shell == null
                || string.IsNullOrEmpty(shell.SessionId)
                || !GameComponent_MechFusionSessionRegistry.TryGetSessionById(
                    shell.SessionId,
                    out MechFusionSession? session)
                || session == null
                || !session.IsActive
                || !ReferenceEquals(session.FusionApparel, shell.parent))
            {
                return;
            }

            TryTeardown(
                session,
                MechFusionExitReason.ApparelLost,
                force: false);
        }

        private static bool ExecuteTeardown(MechFusionSession session)
        {
            Pawn? source = session.SourcePawn;
            Pawn? wearer = session.WearerPawn;

            // 源引用暂时无法解析时保留记录与服装，等待延迟修复，绝不生成复制 Pawn。
            if (source == null)
            {
                Log.Error(
                    "[MAP-机械族机械师] 合体解除时源 Pawn 引用无法解析，" +
                    "已保留记录与服装等待延迟修复：" +
                    $"session={session.SessionId}。");
                return false;
            }

            if (source.Destroyed || source.Discarded)
            {
                Log.Error(
                    "[MAP-机械族机械师] 合体解除时源 Pawn 已被销毁或丢弃，" +
                    "已直接移除会话记录，未生成复制 Pawn：" +
                    $"session={session.SessionId}。");
                RemoveApparel(session, wearer);
                GameComponent_MechFusionSessionRegistry.RemoveSession(session);
                return false;
            }

            if (wearer != null
                && (wearer.Spawned || wearer.GetCaravan() != null)
                && MechanicalFlightUtility.IsAirborne(wearer))
            {
                if (session.ExitReason == MechFusionExitReason.FlightCrash)
                {
                    MechanicalFlightEmergencyUtility.TryBeginEmergencySequence(wearer);
                }
                else
                {
                    MechanicalFlightUtility.TryBeginLanding(wearer);
                }

                if (MechanicalFlightUtility.IsAirborne(wearer))
                {
                    session.TeardownDeferred = true;
                    return false;
                }
            }

            bool transitionStarted = TryBeginReturnToPawnForm(session, source);

            MechFusionBodySynchronizationUtility.RemoveFromWearer(wearer);
            MechFusionEnergyUtility.WriteBackToSource(session, source);

            if (!TryRestoreSourcePawn(session, source, out bool deferred))
            {
                if (deferred)
                {
                    session.SetState(MechFusionSessionState.PendingRecovery);
                    session.UpdateRecoveryLocation(
                        session.PendingMap,
                        session.PendingPosition,
                        session.PendingRotation);
                    Log.Warning(
                        "[MAP-机械族机械师] 合体解除时找不到安全容器，" +
                        "源 Pawn 保留在 WorldPawns 等待恢复：" +
                        $"pawn={source.LabelShort}（{source.ThingID}）。");
                    return false;
                }

                GameComponent_MechFusionSessionRegistry.RemoveSession(session);
                return false;
            }

            FinishAfterSourceRestored(session, source, transitionStarted, wearer);
            return true;
        }

        private static void FinishAfterSourceRestored(
            MechFusionSession session,
            Pawn source,
            bool transitionStarted,
            Pawn? wearer)
        {
            RemoveApparel(session, wearer);

            if (transitionStarted
                && !GameComponent_MechTransformationRegistry.TryCommitTransition(
                    source,
                    targetCarrier: null,
                    out string? commitFailure))
            {
                Log.Warning(
                    "[MAP-机械族机械师] 合体解除后未能提交形态恢复：" +
                    $"pawn={source.LabelShort}（{source.ThingID}），" +
                    $"reason={commitFailure ?? "未知"}。");
            }

            GameComponent_MechFusionSessionRegistry.RemoveSession(session);
            RefreshAfterEnd(source, wearer);
            Messages.Message(
                "MAP_MechanoidMechanitor.Fusion.Ended".Translate(
                    source.LabelShortCap,
                    wearer?.LabelShortCap ?? "-"),
                source,
                MessageTypeDefOf.NeutralEvent,
                historical: false);
        }

        private static bool TryBeginReturnToPawnForm(
            MechFusionSession session,
            Pawn source)
        {
            if (GameComponent_MechTransformationRegistry.TryBeginTransition(
                    source,
                    MechTransformationForm.Pawn,
                    out _,
                    out _))
            {
                return true;
            }

            Thing? carrier = session.FusionApparel;
            if (GameComponent_MechTransformationRegistry.TryBeginRecoveryToPawn(
                    source,
                    carrier,
                    out _,
                    out _))
            {
                return true;
            }

            return false;
        }

        private static bool TryRestoreSourcePawn(
            MechFusionSession session,
            Pawn source,
            out bool deferred)
        {
            deferred = false;
            if (source.Spawned)
            {
                return true;
            }

            Pawn? wearer = session.WearerPawn;
            Caravan? caravan = wearer?.GetCaravan();
            Map? map = wearer != null && wearer.Spawned
                ? wearer.Map
                : session.PendingMap;
            IntVec3 anchor = wearer != null && wearer.Spawned
                ? wearer.Position
                : session.PendingPosition;
            Rot4 rotation = wearer != null && wearer.Spawned
                ? wearer.Rotation
                : session.PendingRotation;

            if (source.Dead)
            {
                if (map == null || map.Disposed || !anchor.IsValid)
                {
                    EnsureWorldPawn(source);
                    return true;
                }

                RemoveFromWorldPawns(source);
                Corpse corpse = EnsureCorpse(source);
                IntVec3 corpseCell = CellFinder.FindNoWipeSpawnLocNear(
                    anchor,
                    map,
                    corpse.def,
                    rotation,
                    RestoreSearchRadius);
                if (!corpseCell.IsValid)
                {
                    EnsureWorldPawn(source);
                    deferred = true;
                    return false;
                }

                GenSpawn.Spawn(
                    corpse,
                    corpseCell,
                    map,
                    rotation,
                    WipeMode.VanishOrMoveAside);
                return true;
            }

            if (caravan != null)
            {
                RemoveFromWorldPawns(source);
                caravan.AddPawn(source, addCarriedPawnToWorldPawnsIfAny: true);
                return true;
            }

            if (map == null || map.Disposed || !anchor.IsValid)
            {
                EnsureWorldPawn(source);
                deferred = true;
                return false;
            }

            RemoveFromWorldPawns(source);
            IntVec3 spawnCell = CellFinder.FindNoWipeSpawnLocNear(
                anchor,
                map,
                source.def,
                rotation,
                RestoreSearchRadius);
            if (!spawnCell.IsValid)
            {
                EnsureWorldPawn(source);
                deferred = true;
                return false;
            }

            GenSpawn.Spawn(
                source,
                spawnCell,
                map,
                rotation,
                WipeMode.VanishOrMoveAside);
            return true;
        }

        private static void RemoveApparel(
            MechFusionSession session,
            Pawn? wearer)
        {
            Thing? apparel = session.FusionApparel;
            if (apparel == null || apparel.Destroyed)
            {
                return;
            }

            if (wearer?.apparel != null
                && apparel is Apparel wornApparel
                && wearer.apparel.Wearing(wornApparel))
            {
                wearer.apparel.Remove(wornApparel);
            }

            apparel.Destroy(DestroyMode.Vanish);
        }

        private static void RemoveFromWorldPawns(Pawn pawn)
        {
            if (pawn.Spawned)
            {
                return;
            }

            if (Find.WorldPawns.Contains(pawn))
            {
                Find.WorldPawns.RemovePawn(pawn);
            }
        }

        private static void EnsureWorldPawn(Pawn pawn)
        {
            if (pawn.Spawned || pawn.Destroyed || pawn.Discarded)
            {
                return;
            }

            if (!Find.WorldPawns.Contains(pawn))
            {
                Find.WorldPawns.PassToWorld(
                    pawn,
                    PawnDiscardDecideMode.KeepForever);
            }
        }

        private static Corpse EnsureCorpse(Pawn pawn)
        {
            Corpse? corpse = pawn.Corpse;
            if (corpse == null)
            {
                corpse = (Corpse)ThingMaker.MakeThing(pawn.RaceProps.corpseDef);
                corpse.InnerPawn = pawn;
            }

            return corpse;
        }

        private static void RefreshAfterEnd(Pawn source, Pawn? wearer)
        {
            wearer?.Drawer?.renderer?.SetAllGraphicsDirty();
            source.Drawer?.renderer?.SetAllGraphicsDirty();
        }
    }
}
