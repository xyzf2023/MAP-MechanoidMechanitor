using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 读档后的统一合体修复。所有异常路径都优先保证同一真实 Pawn 身份不丢失，
    /// 找不到有效容器时保留待恢复记录，而不是销毁服装或生成复制 Pawn。
    /// </summary>
    internal static class MechFusionRepairUtility
    {
        internal static void RepairAfterLoad()
        {
            IReadOnlyList<MechFusionSession> sessions =
                GameComponent_MechFusionSessionRegistry.GetSessionsForReading();
            if (sessions.Count == 0)
            {
                return;
            }

            MechFusionSession[] snapshot = new MechFusionSession[sessions.Count];
            for (int i = 0; i < sessions.Count; i++)
            {
                snapshot[i] = sessions[i];
            }

            HashSet<Pawn> seenSources = new HashSet<Pawn>();
            HashSet<Pawn> seenWearers = new HashSet<Pawn>();

            for (int i = 0; i < snapshot.Length; i++)
            {
                MechFusionSession? session = snapshot[i];
                if (session == null)
                {
                    continue;
                }

                Pawn? source = session.SourcePawn;
                Pawn? wearer = session.WearerPawn;

                if (source == null)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 合体会话的源 Pawn 引用无法解析，" +
                        "已保留记录与服装等待延迟修复：" +
                        $"session={session.SessionId}。");
                    continue;
                }

                if (source.Destroyed || source.Discarded)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 合体会话的源 Pawn 已销毁或丢弃，" +
                        "已清理会话，未生成复制 Pawn：" +
                        $"session={session.SessionId}。");
                    RemoveOrphanApparel(session, wearer);
                    GameComponent_MechFusionSessionRegistry.RemoveSession(session);
                    continue;
                }

                bool duplicate =
                    !seenSources.Add(source)
                    || (wearer != null && !seenWearers.Add(wearer));
                if (duplicate)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 发现同一源 Pawn 或目标人类的多条合体记录，" +
                        "已对重复记录执行安全清理：" +
                        $"session={session.SessionId}。");
                    MechFusionTeardownService.TryTeardown(
                        session,
                        MechFusionExitReason.LoadRepair,
                        force: true);
                    continue;
                }

                if (session.State == MechFusionSessionState.Starting)
                {
                    session.SetState(MechFusionSessionState.Active);
                }

                if (session.IsEnding || session.TeardownDeferred)
                {
                    if (session.TeardownDeferred)
                    {
                        MechFusionTeardownService.TryResumeDeferredTeardown(
                            session);
                    }
                    else
                    {
                        MechFusionTeardownService.TryTeardown(
                            session,
                            MechFusionExitReason.LoadRepair,
                            force: true);
                    }
                    continue;
                }

                if (session.IsPendingRecovery)
                {
                    MechFusionTeardownService.TryRecoverPendingSession(session);
                    continue;
                }

                RepairActiveSession(session, source, wearer);
            }
        }

        private static void RepairActiveSession(
            MechFusionSession session,
            Pawn source,
            Pawn? wearer)
        {
            Thing? apparel = session.FusionApparel;
            bool wearerUsable =
                wearer != null && !wearer.Destroyed && !wearer.Discarded;
            bool apparelUsable = apparel != null && !apparel.Destroyed;

            if (!wearerUsable || !apparelUsable)
            {
                Log.Warning(
                    "[MAP-机械族机械师] 活动合体会话缺少可用的目标人类或服装，" +
                    "已执行安全异常解除：" +
                    $"session={session.SessionId}，" +
                    $"wearerUsable={wearerUsable}，apparelUsable={apparelUsable}。");
                MechFusionTeardownService.TryTeardown(
                    session,
                    MechFusionExitReason.LoadRepair,
                    force: true);
                return;
            }

            if (source.Spawned)
            {
                Log.Error(
                    "[MAP-机械族机械师] 合体源 Pawn 与载体同时处于活动状态，" +
                    "已执行安全异常解除：" +
                    $"session={session.SessionId}。");
                MechFusionTeardownService.TryTeardown(
                    session,
                    MechFusionExitReason.LoadRepair,
                    force: true);
                return;
            }

            if (!ValidateTransformationLink(session, source, apparel!))
            {
                MechFusionTeardownService.TryTeardown(
                    session,
                    MechFusionExitReason.LoadRepair,
                    force: true);
                return;
            }

            RepairShellSessionId(session, apparel!);
            MechFusionBodySynchronizationUtility.ApplyToWearer(session);
            MechFusionWhitelistUtility.RepairAfterLoad(session, wearer);
            MechFusionFlightUtility.RepairAfterLoad(session);
            MechFusionStatCacheUtility.Invalidate(session);
            session.UpdateRecoveryLocation(
                wearer!.Map,
                wearer.Position,
                wearer.Rotation);
            wearer.Drawer?.renderer?.SetAllGraphicsDirty();
            source.Drawer?.renderer?.SetAllGraphicsDirty();
        }

        private static bool ValidateTransformationLink(
            MechFusionSession session,
            Pawn source,
            Thing apparel)
        {
            if (!GameComponent_MechTransformationRegistry.TryGetRecord(
                    source,
                    out MechTransformationRecord? record)
                || record == null)
            {
                Log.Warning(
                    "[MAP-机械族机械师] 合体会话缺少形态记录，已执行安全异常解除：" +
                    $"session={session.SessionId}。");
                return false;
            }

            if (record.TransitionInProgress)
            {
                Log.Warning(
                    "[MAP-机械族机械师] 合体会话的形态转换锁未完成，" +
                    "已回退到最近一次稳定形态并执行安全异常解除：" +
                    $"session={session.SessionId}。");
                GameComponent_MechTransformationRegistry.TryCancelTransition(
                    source);
                return false;
            }

            if (record.CurrentForm != MechTransformationForm.Merged
                || !ReferenceEquals(record.ExternalCarrier, apparel))
            {
                Log.Warning(
                    "[MAP-机械族机械师] 合体会话与形态记录不一致，" +
                    "已执行安全异常解除：" +
                    $"session={session.SessionId}。");
                return false;
            }

            return true;
        }

        private static void RepairShellSessionId(
            MechFusionSession session,
            Thing apparel)
        {
            CompMechFusionShell? shellComp =
                apparel.TryGetComp<CompMechFusionShell>();
            if (shellComp == null
                || shellComp.SessionId == session.SessionId)
            {
                return;
            }

            if (GameComponent_MechFusionSessionRegistry.TryGetSessionForSource(
                    session.SourcePawn,
                    out MechFusionSession? owner)
                && owner != null
                && owner.SessionId == session.SessionId)
            {
                shellComp.AssignSession(session.SessionId);
                Log.Warning(
                    "[MAP-机械族机械师] 合体外甲的会话ID与注册表不一致，" +
                    "已按可证明源身份的活动记录修复：" +
                    $"session={session.SessionId}。");
            }
        }

        private static void RemoveOrphanApparel(
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
    }
}
