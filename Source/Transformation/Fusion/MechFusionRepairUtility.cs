using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 读档后的统一合体修复。所有异常路径都优先保证同一真实 Pawn 身份不丢失，
    /// 找不到有效容器时保留待恢复记录，而不是销毁服装或生成复制 Pawn。
    /// 重复会话按“能证明的链接数量”选出唯一有效记录，其余只清理自身可证明
    /// 拥有的服装，绝不移动或恢复共享的源 Pawn。
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

            List<MechFusionSession> keepers = new List<MechFusionSession>();
            List<MechFusionSession> duplicates = new List<MechFusionSession>();
            SelectUniqueSessions(snapshot, keepers, duplicates);

            for (int i = 0; i < duplicates.Count; i++)
            {
                MechFusionSession duplicate = duplicates[i];
                CleanupDuplicateSession(
                    duplicate,
                    FindKeeperFor(duplicate, keepers));
            }

            for (int i = 0; i < keepers.Count; i++)
            {
                RepairKeeper(keepers[i]);
            }
        }

        private static void SelectUniqueSessions(
            MechFusionSession[] snapshot,
            List<MechFusionSession> keepers,
            List<MechFusionSession> duplicates)
        {
            for (int i = 0; i < snapshot.Length; i++)
            {
                MechFusionSession? session = snapshot[i];
                if (session == null)
                {
                    continue;
                }

                MechFusionSession? conflict = null;
                for (int j = 0; j < keepers.Count; j++)
                {
                    if (SharesSourceOrWearer(keepers[j], session))
                    {
                        conflict = keepers[j];
                        break;
                    }
                }

                if (conflict == null)
                {
                    keepers.Add(session);
                    continue;
                }

                if (ScoreConsistency(session) > ScoreConsistency(conflict))
                {
                    keepers.Remove(conflict);
                    duplicates.Add(conflict);
                    keepers.Add(session);
                }
                else
                {
                    duplicates.Add(session);
                }
            }
        }

        private static bool SharesSourceOrWearer(
            MechFusionSession left,
            MechFusionSession right)
        {
            if (left.SourcePawn != null
                && ReferenceEquals(left.SourcePawn, right.SourcePawn))
            {
                return true;
            }

            return left.WearerPawn != null
                && ReferenceEquals(left.WearerPawn, right.WearerPawn);
        }

        private static MechFusionSession? FindKeeperFor(
            MechFusionSession duplicate,
            List<MechFusionSession> keepers)
        {
            for (int i = 0; i < keepers.Count; i++)
            {
                if (SharesSourceOrWearer(keepers[i], duplicate))
                {
                    return keepers[i];
                }
            }

            return null;
        }

        private static int ScoreConsistency(MechFusionSession session)
        {
            int score = 0;
            Pawn? source = session.SourcePawn;
            Pawn? wearer = session.WearerPawn;
            Apparel? apparel = session.FusionApparel as Apparel;
            if (source != null)
            {
                score++;
            }

            if (wearer != null)
            {
                score++;
            }

            if (apparel != null && !apparel.Destroyed)
            {
                score++;
            }

            if (apparel != null
                && wearer?.apparel != null
                && wearer.apparel.Wearing(apparel))
            {
                score++;
            }

            CompMechFusionShell? shellComp =
                apparel?.TryGetComp<CompMechFusionShell>();
            if (shellComp != null
                && !string.IsNullOrEmpty(shellComp.SessionId)
                && shellComp.SessionId == session.SessionId)
            {
                score++;
            }

            if (source != null
                && apparel != null
                && GameComponent_MechTransformationRegistry.TryGetRecord(
                    source,
                    out MechTransformationRecord? record)
                && record != null
                && apparel.TryGetComp<CompMechFormCarrier>()
                    is CompMechFormCarrier carrier
                && carrier.Matches(record))
            {
                score++;
            }

            return score;
        }

        private static void RepairKeeper(MechFusionSession session)
        {
            Pawn? source = session.SourcePawn;
            if (source == null)
            {
                Log.Error(
                    "[MAP-机械族机械师] 合体会话的源 Pawn 引用无法解析，" +
                    "已保留记录与服装等待延迟修复：" +
                    $"session={session.SessionId}。");
                session.SetState(MechFusionSessionState.PendingRecovery);
                return;
            }

            if (source.Destroyed || source.Discarded)
            {
                Log.Error(
                    "[MAP-机械族机械师] 合体会话的源 Pawn 已销毁或丢弃，" +
                    "已清理目标人类效果与服装，未生成复制 Pawn：" +
                    $"session={session.SessionId}。");
                MechFusionTeardownService.TryTeardown(
                    session,
                    MechFusionExitReason.LoadRepair,
                    force: true);
                return;
            }

            if (session.State == MechFusionSessionState.Starting)
            {
                session.SetState(MechFusionSessionState.Active);
            }

            if (session.IsPendingRecovery)
            {
                MechFusionTeardownService.TryRecoverPendingSession(session);
                return;
            }

            if (session.IsEnding || session.TeardownDeferred)
            {
                if (session.TeardownDeferred)
                {
                    MechFusionTeardownService.TryResumeDeferredTeardown(session);
                }
                else
                {
                    MechFusionTeardownService.TryTeardown(
                        session,
                        MechFusionExitReason.LoadRepair,
                        force: true);
                }
                return;
            }

            RepairActiveSession(session);
        }

        private static void CleanupDuplicateSession(
            MechFusionSession duplicate,
            MechFusionSession? keeper)
        {
            Log.Error(
                "[MAP-机械族机械师] 发现共享源 Pawn 或目标人类的多条合体记录，" +
                "已保留链接最完整的一条，并清理重复记录能够独立证明拥有的临时效果：" +
                $"session={duplicate.SessionId}。");

            Pawn? duplicateWearer = duplicate.WearerPawn;
            bool wearerSharedWithKeeper = keeper != null
                && duplicateWearer != null
                && ReferenceEquals(duplicateWearer, keeper.WearerPawn);

            if (!wearerSharedWithKeeper && duplicateWearer != null)
            {
                TryCleanupDuplicateStep(
                    duplicate,
                    "撤销重复会话临时飞行授权",
                    () =>
                    {
                        if (duplicate.FlightAuthorizationGrantedByFusion
                            && !MechFusionFlightUtility.TryRevokeTemporaryFlight(
                                duplicate))
                        {
                            if (GameComponent_MechanicalFlightRegistry.TryGetRecord(
                                    duplicateWearer,
                                    out MechanicalFlightAuthorizationRecord? record)
                                && record != null)
                            {
                                MechanicalFlightUtility.ClearRuntimeState(
                                    record,
                                    forceLand: true);
                            }

                            if (!MechFusionFlightUtility.TryRevokeTemporaryFlight(
                                    duplicate))
                            {
                                throw new InvalidOperationException(
                                    "重复会话的临时飞行授权无法撤销。");
                            }
                        }
                    });
                TryCleanupDuplicateStep(
                    duplicate,
                    "撤销重复会话合体健康状态",
                    () =>
                    {
                        MechFusionMechanitorSynchronizationService
                            .PrepareForHealthEffectRemoval(
                                duplicate,
                                duplicate.SourcePawn,
                                duplicateWearer,
                                sourceRecoverable: false);
                        if (!MechFusionHealthEffectManager.RevokeAll(
                                duplicate,
                                duplicateWearer))
                        {
                            throw new InvalidOperationException(
                                "重复会话至少一条合体健康状态撤销失败。");
                        }

                        MechFusionMechanitorSynchronizationService
                            .NotifyHealthEffectsRevoked(
                                duplicate,
                                duplicateWearer);
                    });
            }

            Pawn? duplicateSource = duplicate.SourcePawn;
            bool sourceSharedWithKeeper = keeper != null
                && duplicateSource != null
                && ReferenceEquals(duplicateSource, keeper.SourcePawn);
            if (!sourceSharedWithKeeper
                && duplicateSource != null
                && !duplicateSource.Destroyed
                && !duplicateSource.Discarded)
            {
                TryCleanupDuplicateStep(
                    duplicate,
                    "移除重复会话源机械族休眠标记",
                    () => MechFusionSourceUtility.RemoveDormantGuard(
                        duplicateSource));
                TryCleanupDuplicateStep(
                    duplicate,
                    "结算重复会话源机械族耐久",
                    () =>
                    {
                        if (!duplicate.StabilitySettled)
                        {
                            MechFusionStabilityUtility.SettleSourcePartDurability(
                                duplicate,
                                duplicateSource);
                            duplicate.MarkStabilitySettled();
                        }
                    });
                TryCleanupDuplicateStep(
                    duplicate,
                    "恢复重复会话独立源机械族并提交能源",
                    () =>
                    {
                        if (!duplicate.SourceRestored)
                        {
                            bool restored =
                                MechFusionTeardownService.TryRestoreSourcePawn(
                                    duplicate,
                                    duplicateSource,
                                    out bool deferred);
                            if (restored)
                            {
                                duplicate.MarkSourceRestored();
                            }
                            else if (deferred)
                            {
                                // 重复记录不能进入会影响保留会话的通用重试流程；
                                // TryRestoreSourcePawn 已确保真实 Pawn 留在 WorldPawns。
                                duplicate.MarkSourceRestored();
                                Log.Warning(
                                    "[MAP-机械族机械师] 重复会话的独立源机械族暂时无法回到地图，" +
                                    "已安全保留在 WorldPawns：" +
                                    $"pawn={duplicateSource.LabelShort}（{duplicateSource.ThingID}）。");
                            }
                            else
                            {
                                throw new InvalidOperationException(
                                    "重复会话的独立源机械族无法恢复合法容器。");
                            }
                        }

                        if (!duplicate.TransformationRestored)
                        {
                            if (!MechFusionTeardownService
                                    .TryRestoreTransformationRecord(
                                        duplicate,
                                        duplicateSource))
                            {
                                throw new InvalidOperationException(
                                    "重复会话的独立源机械族形态无法恢复。");
                            }

                            duplicate.MarkTransformationRestored();
                        }

                        // 兼容旧存档：旧版的 EnergyWrittenBack 只表示“尝试过”，
                        // 因此恢复容器后始终幂等重写一次最终 Session 能源。
                        if (!duplicateSource.Dead)
                        {
                            if (!MechFusionEnergyUtility.TryWriteBackToSource(
                                    duplicate,
                                    duplicateSource))
                            {
                                throw new InvalidOperationException(
                                    "重复会话源机械族恢复后无法提交最终能源。");
                            }

                            if (!duplicate.EnergyWrittenBack)
                            {
                                duplicate.MarkEnergyWrittenBack();
                            }
                        }
                    });
            }

            TryCleanupDuplicateStep(
                duplicate,
                "清理重复会话服装",
                () =>
                {
                    if (duplicate.FusionApparel is not Apparel apparel
                        || apparel.Destroyed)
                    {
                        return;
                    }

                    CompMechFusionShell? shellComp =
                        apparel.TryGetComp<CompMechFusionShell>();
                    bool ownedByDuplicate = shellComp != null
                        && !string.IsNullOrEmpty(shellComp.SessionId)
                        && shellComp.SessionId == duplicate.SessionId;
                    bool referencedByKeeper = ReferenceEquals(
                        keeper?.FusionApparel,
                        apparel);
                    if (!ownedByDuplicate || referencedByKeeper)
                    {
                        return;
                    }

                    if (duplicateWearer?.apparel != null
                        && duplicateWearer.apparel.Wearing(apparel))
                    {
                        duplicateWearer.apparel.Remove(apparel);
                    }

                    apparel.Destroy(DestroyMode.Vanish);
                });

            GameComponent_MechFusionSessionRegistry.RemoveSession(duplicate);
        }

        private static bool TryCleanupDuplicateStep(
            MechFusionSession session,
            string stepName,
            Action action)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 清理重复合体会话步骤失败（" +
                    stepName +
                    "）：" +
                    $"session={session.SessionId}：{ex}");
                return false;
            }
        }

        private static void RepairActiveSession(MechFusionSession session)
        {
            Pawn? source = session.SourcePawn;
            Pawn? wearer = session.WearerPawn;
            if (source == null)
            {
                return;
            }

            bool wearerUsable =
                wearer != null
                && !wearer.Destroyed
                && !wearer.Discarded
                && !wearer.Dead
                && !wearer.Downed;
            Apparel? apparel = session.FusionApparel as Apparel;
            bool apparelUsable = apparel != null && !apparel.Destroyed;

            if (!wearerUsable || !apparelUsable)
            {
                Log.Warning(
                    "[MAP-机械族机械师] 活动合体会话缺少可用的目标人类或服装，" +
                    "已执行安全异常解除：" +
                    $"session={session.SessionId}，" +
                    $"wearerUsable={wearerUsable}，" +
                    $"apparelUsable={apparelUsable}。");
                TeardownForRepair(session);
                return;
            }

            Pawn wearerPawn = wearer!;
            Apparel worn = apparel!;

            if (wearerPawn.apparel == null
                || !wearerPawn.apparel.Wearing(worn))
            {
                Log.Warning(
                    "[MAP-机械族机械师] 合体服装没有实际穿在记录中的目标人类身上，" +
                    "已执行安全异常解除，不把掉落的服装当作有效载体：" +
                    $"session={session.SessionId}。");
                TeardownForRepair(session);
                return;
            }

            CompMechFusionShell? shellComp =
                worn.TryGetComp<CompMechFusionShell>();
            CompMechFormCarrier? carrierComp =
                worn.TryGetComp<CompMechFormCarrier>();
            if (shellComp == null || carrierComp == null)
            {
                Log.Warning(
                    "[MAP-机械族机械师] 合体服装缺少连接组件或通用载体组件，" +
                    "已执行安全异常解除：" +
                    $"session={session.SessionId}。");
                TeardownForRepair(session);
                return;
            }

            if (!ValidateTransformationLink(
                    session,
                    source,
                    worn,
                    carrierComp))
            {
                TeardownForRepair(session);
                return;
            }

            if (string.IsNullOrEmpty(shellComp.SessionId))
            {
                if (!CanProveShellOwnership(session))
                {
                    Log.Warning(
                        "[MAP-机械族机械师] 合体服装会话ID缺失且无法证明归属，" +
                        "已执行安全异常解除：" +
                        $"session={session.SessionId}。");
                    TeardownForRepair(session);
                    return;
                }

                shellComp.AssignSession(session.SessionId);
                Log.Warning(
                    "[MAP-机械族机械师] 合体服装会话ID缺失，已按唯一源身份修复：" +
                    $"session={session.SessionId}。");
            }
            else if (shellComp.SessionId != session.SessionId)
            {
                if (!CanProveShellOwnership(session))
                {
                    Log.Warning(
                        "[MAP-机械族机械师] 合体服装会话ID与注册表不一致且无法证明归属，" +
                        "已执行安全异常解除：" +
                        $"session={session.SessionId}。");
                    TeardownForRepair(session);
                    return;
                }

                shellComp.AssignSession(session.SessionId);
                Log.Warning(
                    "[MAP-机械族机械师] 合体服装会话ID与注册表不一致，" +
                    "已按唯一源身份修复：" +
                    $"session={session.SessionId}。");
            }

            if (source.Spawned)
            {
                Log.Error(
                    "[MAP-机械族机械师] 合体源 Pawn 与载体同时处于活动状态，" +
                    "已执行安全异常解除：" +
                    $"session={session.SessionId}。");
                TeardownForRepair(session);
                return;
            }

            if (!Find.WorldPawns.Contains(source))
            {
                Log.Warning(
                    "[MAP-机械族机械师] 合体源 Pawn 未由 WorldPawns 管理，尝试补回：" +
                    $"pawn={source.LabelShort}（{source.ThingID}）。");
            }

            MechFusionWorldPawnStorage.EnsureStored(session, source);
            MechFusionSourceUtility.ApplyDormantGuard(source);
            // 旧会话没有机控快照；迁移版首次读取时只针对机械师源补捕获。
            MechFusionMechanitorSynchronizationService.EnsureLegacySnapshot(
                session,
                source,
                wearerPawn);
            MechFusionHealthEffectManager
                .EnsureMechControlSynchronizationEntry(session);
            MechFusionHealthEffectManager.RepairAfterLoad(session, wearerPawn);
            MechFusionMechanitorSynchronizationService.ApplyOrRepair(
                session,
                source,
                wearerPawn);
            MechFusionFlightUtility.RepairAfterLoad(session);
            MechFusionStatCacheUtility.Invalidate(session);
            session.UpdateRecoveryLocation(
                wearerPawn.Map,
                wearerPawn.Position,
                wearerPawn.Rotation);
            wearerPawn.Drawer?.renderer?.SetAllGraphicsDirty();
            source.Drawer?.renderer?.SetAllGraphicsDirty();
        }

        private static void TeardownForRepair(MechFusionSession session)
        {
            MechFusionTeardownService.TryTeardown(
                session,
                MechFusionExitReason.LoadRepair,
                force: true);
        }

        private static bool ValidateTransformationLink(
            MechFusionSession session,
            Pawn source,
            Thing apparel,
            CompMechFormCarrier carrierComp)
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

            if (!carrierComp.Matches(record))
            {
                Log.Warning(
                    "[MAP-机械族机械师] 合体服装的通用形态链接与记录不匹配，" +
                    "已执行安全异常解除：" +
                    $"session={session.SessionId}。");
                return false;
            }

            return true;
        }

        private static bool CanProveShellOwnership(MechFusionSession session)
        {
            Pawn? source = session.SourcePawn;
            Thing? apparel = session.FusionApparel;
            if (source == null || apparel == null)
            {
                return false;
            }

            if (!GameComponent_MechFusionSessionRegistry.TryGetSessionForSource(
                    source,
                    out MechFusionSession? owner)
                || owner == null
                || owner.SessionId != session.SessionId)
            {
                return false;
            }

            return ReferenceEquals(owner.FusionApparel, apparel);
        }
    }
}
