using System;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 合体开始的唯一事务入口。任一步骤失败都会按反向顺序回滚，
    /// 不留下半合体状态、无主服装或被吞掉的真实 Pawn。
    /// </summary>
    public static class MechFusionStartService
    {
        private const int RestoreSearchRadius = 8;

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

            Map map = source.Map;
            IntVec3 originalPosition = source.Position;
            Rot4 originalRotation = source.Rotation;
            bool transitionStarted = false;
            Thing? apparel = null;
            bool apparelCreated = false;
            bool apparelWorn = false;
            bool sourceStored = false;

            try
            {
                if (!GameComponent_MechTransformationRegistry.TryBeginTransition(
                        source,
                        MechTransformationForm.Merged,
                        out _,
                        out failureReason))
                {
                    RollbackStart(
                        session,
                        source,
                        wearer,
                        map,
                        originalPosition,
                        originalRotation,
                        transitionStarted,
                        apparel,
                        apparelCreated,
                        apparelWorn,
                        sourceStored);
                    return false;
                }

                transitionStarted = true;
                apparel = ThingMaker.MakeThing(shellDef);
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
                    RollbackStart(
                        session,
                        source,
                        wearer,
                        map,
                        originalPosition,
                        originalRotation,
                        transitionStarted,
                        apparel,
                        apparelCreated,
                        apparelWorn,
                        sourceStored);
                    return false;
                }

                shellComp.AssignSession(session.SessionId);
                apparelCreated = true;
                session.BindApparel(apparel);

                MechFusionEnergyUtility.CaptureInitialEnergy(session, source);
                MechFusionStabilityUtility.CaptureInitialStability(
                    session,
                    source);

                wearer.apparel!.Wear(
                    (Apparel)apparel,
                    dropReplacedApparel: true);
                if (!wearer.apparel.Wearing(apparel))
                {
                    failureReason =
                        "MAP_MechanoidMechanitor.Fusion.Failure.CannotWear"
                            .Translate();
                    RollbackStart(
                        session,
                        source,
                        wearer,
                        map,
                        originalPosition,
                        originalRotation,
                        transitionStarted,
                        apparel,
                        apparelCreated,
                        apparelWorn,
                        sourceStored);
                    return false;
                }

                apparelWorn = true;
                source.DeSpawn(DestroyMode.Vanish);
                Find.WorldPawns.PassToWorld(
                    source,
                    PawnDiscardDecideMode.KeepForever);
                sourceStored = true;
                MechFusionSourceUtility.ApplyDormantGuard(source);

                if (!GameComponent_MechTransformationRegistry.TryCommitTransition(
                        source,
                        apparel,
                        out failureReason))
                {
                    RollbackStart(
                        session,
                        source,
                        wearer,
                        map,
                        originalPosition,
                        originalRotation,
                        transitionStarted,
                        apparel,
                        apparelCreated,
                        apparelWorn,
                        sourceStored);
                    return false;
                }

                session.SetState(MechFusionSessionState.Active);
                session.UpdateRecoveryLocation(
                    wearer.Map,
                    wearer.Position,
                    wearer.Rotation);
                MechFusionSnapshotBuilder.Capture(session, source);
                MechFusionBodySynchronizationUtility.ApplyToWearer(session);
                MechFusionWhitelistUtility.ApplyAll(session, wearer);
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
                RollbackStart(
                    session,
                    source,
                    wearer,
                    map,
                    originalPosition,
                    originalRotation,
                    transitionStarted,
                    apparel,
                    apparelCreated,
                    apparelWorn,
                    sourceStored);
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

        private static void RollbackStart(
            MechFusionSession session,
            Pawn source,
            Pawn wearer,
            Map? map,
            IntVec3 originalPosition,
            Rot4 originalRotation,
            bool transitionStarted,
            Thing? apparel,
            bool apparelCreated,
            bool apparelWorn,
            bool sourceStored)
        {
            session.SetState(MechFusionSessionState.Ending);
            try
            {
                if (sourceStored && !source.Destroyed && !source.Discarded)
                {
                    MechFusionSourceUtility.RemoveDormantGuard(source);
                    if (Find.WorldPawns.Contains(source))
                    {
                        Find.WorldPawns.RemovePawn(source);
                    }

                    if (!source.Spawned && map != null && !map.Disposed)
                    {
                        IntVec3 restoreCell = CellFinder.FindNoWipeSpawnLocNear(
                            originalPosition,
                            map,
                            source.def,
                            originalRotation,
                            RestoreSearchRadius);
                        if (restoreCell.IsValid)
                        {
                            GenSpawn.Spawn(
                                source,
                                restoreCell,
                                map,
                                originalRotation,
                                WipeMode.VanishOrMoveAside);
                        }
                        else
                        {
                            Find.WorldPawns.PassToWorld(
                                source,
                                PawnDiscardDecideMode.KeepForever);
                            Log.Error(
                                "[MAP-机械族机械师] 合体回滚时地图上没有安全位置，" +
                                "原始 Pawn 已保留在 WorldPawns：" +
                                $"pawn={source.LabelShort}（{source.ThingID}）。");
                        }
                    }
                }

                if (apparelWorn
                    && apparel != null
                    && !apparel.Destroyed
                    && wearer.apparel != null
                    && apparel is Apparel wornApparel
                    && wearer.apparel.Wearing(wornApparel))
                {
                    wearer.apparel.Remove(wornApparel);
                }

                if (apparelCreated && apparel != null && !apparel.Destroyed)
                {
                    apparel.Destroy(DestroyMode.Vanish);
                }

                if (transitionStarted)
                {
                    GameComponent_MechTransformationRegistry.TryCancelTransition(
                        source);
                }
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 合体回滚过程中发生异常：" +
                    $"source={source.LabelShort}（{source.ThingID}）：{ex}");
            }
            finally
            {
                GameComponent_MechFusionSessionRegistry.RemoveSession(session);
            }
        }

        private static void RefreshAfterStart(Pawn source, Pawn wearer)
        {
            wearer.Drawer?.renderer?.SetAllGraphicsDirty();
            source.Drawer?.renderer?.SetAllGraphicsDirty();
        }
    }
}
