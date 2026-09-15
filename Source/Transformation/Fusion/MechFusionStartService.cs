using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 合体开始的唯一事务入口。任一步骤失败都会按反向顺序回滚，
    /// 不留下半合体状态、无主服装或被吞掉的真实 Pawn。
    /// 形态提交前后的回滚路径严格区分：未提交用取消锁，已提交则通过
    /// 正式恢复入口把形态记录还原为 Pawn，绝不只依赖 TryCancelTransition。
    /// </summary>
    public static class MechFusionStartService
    {
        private sealed class StartTransaction
        {
            public MechFusionSession Session = null!;
            public Pawn Source = null!;
            public Pawn Wearer = null!;
            public Map? Map;
            public IntVec3 OriginalPosition = IntVec3.Invalid;
            public Rot4 OriginalRotation = Rot4.South;
            public bool TransitionBegun;
            public bool ApparelCreated;
            public bool ApparelWorn;
            public bool SourceStored;
            public bool TransformationCommitted;
            public bool HealthEffectsApplied;
            public bool TemporaryFlightApplied;
            public bool WearAttempted;
            public Thing? Apparel;
            public List<Apparel>? PreWornApparel;
        }

        public static bool TryStartFusion(
            Pawn? sourcePawn,
            Pawn? wearerPawn,
            out string? failureReason)
        {
            failureReason = null;
            if (!MechFusionValidator.CanStart(
                    sourcePawn,
                    wearerPawn,
                    out failureReason))
            {
                return false;
            }

            Pawn source = sourcePawn!;
            Pawn wearer = wearerPawn!;
            ThingDef? shellDef = MechFusionValidator.GetShellDef();
            if (shellDef == null)
            {
                failureReason =
                    "MAP_MechanoidMechanitor.Fusion.Failure.ShellMissing"
                        .Translate();
                Log.Error(
                    "[MAP-机械族机械师] 无法开始合体：未找到合体外甲 Def " +
                    MechFusionDefNames.ShellDefName + "。");
                return false;
            }

            int startTick = Find.TickManager?.TicksGame ?? 0;
            MechFusionSession session = new MechFusionSession(
                source,
                wearer,
                null,
                source.def,
                startTick);
            if (!GameComponent_MechFusionSessionRegistry.RegisterSession(session))
            {
                failureReason =
                    "MAP_MechanoidMechanitor.Fusion.Failure.SessionBusy"
                        .Translate();
                return false;
            }

            StartTransaction transaction = new StartTransaction
            {
                Session = session,
                Source = source,
                Wearer = wearer,
                Map = source.Map,
                OriginalPosition = source.Position,
                OriginalRotation = source.Rotation
            };

            try
            {
                if (!GameComponent_MechTransformationRegistry.TryBeginTransition(
                        source,
                        MechTransformationForm.Merged,
                        out _,
                        out failureReason))
                {
                    RollbackStart(transaction);
                    return false;
                }

                transaction.TransitionBegun = true;
                Thing apparel = ThingMaker.MakeThing(shellDef);
                transaction.Apparel = apparel;
                CompMechFusionShell? shellComp =
                    apparel.TryGetComp<CompMechFusionShell>();
                if (shellComp == null
                    || apparel.TryGetComp<CompMechFormCarrier>() == null)
                {
                    failureReason =
                        "MAP_MechanoidMechanitor.Fusion.Failure.ShellMissing"
                            .Translate();
                    Log.Error(
                        "[MAP-机械族机械师] 合体外甲缺少必要的载体或连接组件。");
                    RollbackStart(transaction);
                    return false;
                }

                shellComp.AssignSession(session.SessionId);
                transaction.ApparelCreated = true;
                session.BindApparel(apparel);

                MechFusionEnergyUtility.CaptureInitialEnergy(session, source);
                MechFusionStabilityUtility.CaptureInitialStability(
                    session,
                    source);

                transaction.PreWornApparel = SnapshotWornApparel(wearer);
                transaction.WearAttempted = true;
                wearer.apparel!.Wear(
                    (Apparel)apparel,
                    dropReplacedApparel: true);
                if (!wearer.apparel.Wearing(apparel))
                {
                    failureReason =
                        "MAP_MechanoidMechanitor.Fusion.Failure.CannotWear"
                            .Translate();
                    RollbackStart(transaction);
                    return false;
                }

                transaction.ApparelWorn = true;
                source.DeSpawn(DestroyMode.Vanish);
                Find.WorldPawns.PassToWorld(
                    source,
                    PawnDiscardDecideMode.KeepForever);
                transaction.SourceStored = true;
                MechFusionSourceUtility.ApplyDormantGuard(source);

                if (!GameComponent_MechTransformationRegistry.TryCommitTransition(
                        source,
                        apparel,
                        out failureReason))
                {
                    RollbackStart(transaction);
                    return false;
                }

                transaction.TransformationCommitted = true;
                session.SetState(MechFusionSessionState.Active);
                session.UpdateRecoveryLocation(
                    wearer.Map,
                    wearer.Position,
                    wearer.Rotation);
                MechFusionHealthEffectManager.CaptureFromSource(
                    session,
                    source);
                MechFusionSnapshotBuilder.Capture(session, source);
                // 先标记“已尝试”，再调用统一健康状态管理层；单条添加失败
                // 只输出警告，若管理层外发生异常，事务回滚仍会幂等清理。
                transaction.HealthEffectsApplied = true;
                MechFusionHealthEffectManager.ApplyAll(session, wearer);
                transaction.TemporaryFlightApplied = true;
                MechFusionFlightUtility.ApplyTemporaryFlight(
                    session,
                    source,
                    wearer);
                MechFusionStatCacheUtility.Invalidate(session);
                RefreshAfterStart(source, wearer);
                Messages.Message(
                    "MAP_MechanoidMechanitor.Fusion.Started".Translate(
                        source.LabelShortCap,
                        wearer.LabelShortCap),
                    source,
                    MessageTypeDefOf.PositiveEvent,
                    historical: false);
                return true;
            }
            catch (Exception ex)
            {
                RollbackStart(transaction);
                Log.Error(
                    "[MAP-机械族机械师] 合体开始事务发生异常，已尝试回滚：" +
                    $"source={source.LabelShort}（{source.ThingID}），" +
                    $"wearer={wearer.LabelShort}（{wearer.ThingID}）：{ex}");
                failureReason =
                    "MAP_MechanoidMechanitor.Fusion.Failure.Unexpected"
                        .Translate();
                return false;
            }
        }

        private static List<Apparel>? SnapshotWornApparel(Pawn wearer)
        {
            if (wearer?.apparel == null)
            {
                return null;
            }

            List<Apparel> worn = wearer.apparel.WornApparel;
            return worn.Count == 0 ? null : new List<Apparel>(worn);
        }

        private static void RollbackStart(StartTransaction transaction)
        {
            MechFusionSession session = transaction.Session;
            Pawn source = transaction.Source;
            Pawn wearer = transaction.Wearer;

            session.SetExitReason(MechFusionExitReason.LoadRepair);
            session.SetState(MechFusionSessionState.Ending);

            bool sourceRestoreDeferred = false;
            bool transformationRestoreFailed = false;
            bool flightRevokeIncomplete = false;
            bool cleanupIncomplete = false;

            if (transaction.TemporaryFlightApplied)
            {
                TryRollbackStep(
                    session,
                    ref cleanupIncomplete,
                    "撤销临时飞行授权",
                    () =>
                    {
                        if (!MechFusionFlightUtility.TryRevokeTemporaryFlight(
                                session))
                        {
                            flightRevokeIncomplete = true;
                        }
                    });
            }

            if (transaction.HealthEffectsApplied)
            {
                TryRollbackStep(
                    session,
                    ref cleanupIncomplete,
                    "撤销合体健康状态",
                    () =>
                    {
                        if (!MechFusionHealthEffectManager.RevokeAll(
                                session,
                                wearer))
                        {
                            throw new InvalidOperationException(
                                "至少一条合体健康状态撤销失败。");
                        }
                    });
            }

            TryRollbackStep(
                session,
                ref cleanupIncomplete,
                "移除休眠维持标记",
                () => MechFusionSourceUtility.RemoveDormantGuard(source));

            if (transaction.SourceStored || !source.Spawned)
            {
                TryRollbackStep(
                    session,
                    ref cleanupIncomplete,
                    "恢复源机械族容器",
                    () =>
                    {
                        session.UpdateRecoveryLocation(
                            transaction.Map,
                            transaction.OriginalPosition,
                            transaction.OriginalRotation);
                        if (!MechFusionTeardownService.TryRestoreSourcePawn(
                                session,
                                source,
                                out _))
                        {
                            sourceRestoreDeferred = true;
                        }
                    });
            }

            if (transaction.TransformationCommitted)
            {
                TryRollbackStep(
                    session,
                    ref cleanupIncomplete,
                    "恢复形态记录",
                    () =>
                    {
                        if (!MechFusionTeardownService
                            .TryRestoreTransformationRecord(session, source))
                        {
                            transformationRestoreFailed = true;
                        }
                    });
            }
            else if (transaction.TransitionBegun)
            {
                TryRollbackStep(
                    session,
                    ref cleanupIncomplete,
                    "取消未提交的形态转换",
                    () =>
                    {
                        if (!GameComponent_MechTransformationRegistry
                                .TryCancelTransition(source))
                        {
                            transformationRestoreFailed = true;
                        }
                    });
            }

            Thing? apparel = transaction.Apparel;
            if (transaction.ApparelCreated && apparel != null)
            {
                TryRollbackStep(
                    session,
                    ref cleanupIncomplete,
                    "移除并销毁合体外甲",
                    () =>
                    {
                        if (apparel.Destroyed)
                        {
                            return;
                        }

                        if (transaction.ApparelWorn
                            && wearer.apparel != null
                            && apparel is Apparel wornApparel
                            && wearer.apparel.Wearing(wornApparel))
                        {
                            wearer.apparel.Remove(wornApparel);
                        }

                        apparel.Destroy(DestroyMode.Vanish);
                    });
            }

            if (transaction.WearAttempted)
            {
                TryRollbackStep(
                    session,
                    ref cleanupIncomplete,
                    "恢复被本次穿戴脱下的服装",
                    () => RestoreDroppedApparel(
                        wearer,
                        transaction.PreWornApparel));
            }

            if (sourceRestoreDeferred
                || transformationRestoreFailed
                || flightRevokeIncomplete
                || cleanupIncomplete)
            {
                // 真实源 Pawn 尚未回到合法容器、形态未恢复或飞行授权未撤销：
                // 保留会话继续延迟修复。
                session.SetState(MechFusionSessionState.PendingRecovery);
                session.TeardownDeferred = false;
                Log.Error(
                    "[MAP-机械族机械师] 合体开始回滚未能完全收束，" +
                    "已保留会话等待延迟修复：" +
                    $"session={session.SessionId}，" +
                    $"sourceRestoreDeferred={sourceRestoreDeferred}，" +
                    $"transformationRestoreFailed={transformationRestoreFailed}，" +
                    $"flightRevokeIncomplete={flightRevokeIncomplete}，" +
                    $"cleanupIncomplete={cleanupIncomplete}。");
                return;
            }

            GameComponent_MechFusionSessionRegistry.RemoveSession(session);
        }

        private static void TryRollbackStep(
            MechFusionSession session,
            ref bool cleanupIncomplete,
            string stepName,
            Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                cleanupIncomplete = true;
                Log.Error(
                    "[MAP-机械族机械师] 合体回滚步骤失败（" + stepName + "），" +
                    "继续尝试其他不依赖步骤：" +
                    $"session={session.SessionId}：{ex}");
            }
        }

        private static void RestoreDroppedApparel(
            Pawn wearer,
            List<Apparel>? preWornApparel)
        {
            if (preWornApparel == null || preWornApparel.Count == 0)
            {
                return;
            }

            if (wearer.Destroyed
                || wearer.Discarded
                || wearer.Dead
                || wearer.apparel == null)
            {
                return;
            }

            for (int i = 0; i < preWornApparel.Count; i++)
            {
                Apparel apparel = preWornApparel[i];
                if (apparel == null || apparel.Destroyed || apparel.Discarded)
                {
                    continue;
                }

                if (wearer.apparel.Wearing(apparel)
                    || apparel.Wearer != null
                    || !ApparelUtility.HasPartsToWear(wearer, apparel.def)
                    || !apparel.PawnCanWear(wearer, ignoreGender: true))
                {
                    continue;
                }

                try
                {
                    wearer.apparel.Wear(apparel, dropReplacedApparel: true);
                }
                catch (Exception ex)
                {
                    // 恢复失败时只保留服装在地图上，绝不销毁。
                    Log.Error(
                        "[MAP-机械族机械师] 恢复合体失败时被脱下的服装失败，" +
                        "服装保留在地图上：" +
                        $"wearer={wearer.LabelShort}（{wearer.ThingID}），" +
                        $"apparel={apparel.ThingID}：{ex}");
                }
            }
        }

        private static void RefreshAfterStart(Pawn source, Pawn wearer)
        {
            wearer.Drawer?.renderer?.SetAllGraphicsDirty();
            source.Drawer?.renderer?.SetAllGraphicsDirty();
        }
    }
}
